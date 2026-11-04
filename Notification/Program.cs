using System.Diagnostics;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Notification.Infrastructure;
// FILLED BY the sibling slice skill — the usings for what it wires below (e.g. using Notification.Infrastructure.Pgsql;
// when the first data slice lands). Non-System usings only: System.* sort first (IDE0055).
using Notification.Infrastructure.Pgsql;
using Amazon.SimpleEmail;
using Amazon.SQS;
using Notification.Features.Notification.PlanDepartureReminder;
using Notification.Features.Notification.RecordEmailServiceReport;
using Notification.Features.Notification.SendDepartureReminderEmail;
using Notification.Infrastructure.Messaging;
using Notification.Infrastructure.Reminders;

// Configuration reaches this app through ONE channel in every environment: environment variables,
// flat-named so there is no ':' / '__' separator to mistype (Infrastructure/ConfigKeys.cs). The
// deployed values come from the deployment template; the local ones from LocalEntryPoints, which
// MUST run before the builder is created — its environment-variable provider snapshots the process
// block once, so anything set after that line never reaches IConfiguration
// (references/configuration.md).
LocalEntryPoints.Apply();
// FILLED BY the sibling slice skill when the first data slice lands — BEFORE the builder, so nothing
// can query first: Dapper bakes its name mapping per (query, type) at first execution, so a late call
// is a silent no-op (skills-db-postgres #11/#25; its check_dapper_mapping.py D3 anchors on this line).
// The timeout is DERIVED from this API's caller deadline, with the derivation in a why-comment here:
//   DapperConfiguration.Apply(commandTimeout: TimeSpan.FromSeconds(...));
// 10 s: inside the caller's own deadline, so a query that runs past it has already failed upstream.
DapperConfiguration.Apply(commandTimeout: TimeSpan.FromSeconds(10));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// DI scope validation, on in EVERY environment (skills-db-postgres #26). CreateBuilder turns
// these on in Development only, so a captive dependency throws on a laptop and goes unnoticed in
// production: a singleton that takes a scoped IWriterConnectionFactory or ICurrentActor resolves
// it from the ROOT scope and freezes the first request's actor for the life of the process, and
// every later user's writes are stamped with that identity. ValidateOnBuild catches the capture
// at startup; ValidateScopes catches a scoped service pulled from app.Services at runtime.
// A startup failure here is the finding, not a regression - read the exception, fix the capture.
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// Fail at startup naming every missing key at once, rather than on whichever request first needs
// one. List only what the app cannot start without; a legitimately optional key is left out.
// FILLED BY the sibling slice skill as it introduces keys:
//   builder.Configuration.RequireKeys(ConfigKeys.BackendBaseUrl, ConfigKeys.ClientId);
builder.Configuration.RequireKeys(
    ConfigKeys.DbConnectionProfile,
    ConfigKeys.DbMaxPoolSize,
    ConfigKeys.NotificationQueueUrl,
    ConfigKeys.EmailServiceReportsQueueUrl,
    ConfigKeys.SenderAddress,
    ConfigKeys.PortalBaseUrl,
    ConfigKeys.PortalSignInPath);

// Log levels, in code for the same reason every other setting is: one place, and no appsettings
// file to disagree with it. Everything defaults to Information; the framework's own request/routing
// chatter stays at Warning so the telemetry pipeline is not flooded, except in Development where it
// is the most useful thing in the console. A deployed run can still be turned up without a code
// change: Logging__LogLevel__Default is an ordinary environment variable on the container.
// Logs go to the console only; on EC2 the awslogs driver ships them to /routego/notification.
LogLevel frameworkLogLevel = LogLevel.Warning;
if (builder.Environment.IsDevelopment())
{
    frameworkLogLevel = LogLevel.Information;
}

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", frameworkLogLevel);

// Secrets: standard AWS Secrets Manager access (references/secrets.md). ONE fixed name per secret;
// the SOURCE is the environment — Development reads configuration, everything else Secrets Manager.
builder.Services.AddSingleton<SecretResolver>();

// Wire naming contract (references/host-components.md): camelCase properties are the web
// default; enums serialize as camelCase STRINGS (without this converter they serialize as
// numbers and every enum in every response breaks the authored contract). Same policy as
// Common/WireJson.cs, which covers HttpClient reads and hand-rolled serialization.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// Errors: RFC 7807 ProblemDetails + unhandled-exception handler (references/host-components.md).
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails =
    c => c.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString());
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

// Infrastructure ports — registered once. No per-slice registrations.
builder.Services.AddSingleton<IClock, SystemClock>();
// FILLED BY skills-aspnetcore-api-vertical-slice when the first data slice lands:
//   the reader/writer connection-port registrations (files copied from skills-db-postgres —
//   reader singleton; writer scoped, it carries the per-request actor) (+ any other ports).
// Database (skills-db-postgres, Setting up a project, step 2). The secret's NAME is a fixed platform
// constant; the SOURCE is the environment — Development reads user-secrets, anything else Secrets
// Manager through the execution role — and the one shared mapper turns its JSON into a connection string.
var startupSecrets = new SecretResolver(builder.Configuration, builder.Environment);
string? databaseSecretJson = await startupSecrets.ResolveAsync(DbSecret.SecretId);
if (string.IsNullOrEmpty(databaseSecretJson))
{
    string databaseSecretHelp = $"grant the execution role secretsmanager:GetSecretValue on '{DbSecret.SecretId}'";
    if (builder.Environment.IsDevelopment())
    {
        databaseSecretHelp = $"set it once: dotnet user-secrets set \"{DbSecret.SecretId}\" '<the platform JSON>' --id routego-platform";
    }
    throw new InvalidOperationException($"No database secret — {databaseSecretHelp}.");
}
// The pool profile rides on the same seam: the deployment template names the Lambda execution model
// (DB_CONNECTION_PROFILE, DB_MAX_POOL_SIZE) and the profile turns that into pool settings plus whether a
// dropped connection also clears the idle pool (Infrastructure/Pgsql/ConnectionProfile.cs).
var databaseProfile = ConnectionProfile.FromConfiguration(builder.Configuration);
string databaseConnectionString = databaseProfile.Apply(DbSecretValue.ToConnectionString(databaseSecretJson));
// Nobody is ever signed in to this service: the email workers act on events, and the internal
// support calls carry only the shared internal key, never a person. So every write is made by the
// system actor, and the writer is a singleton like the reader, with no per-request identity.
builder.Services.AddSingleton<ICurrentActor>(SystemActor.Default);
builder.Services.AddSingleton<IReaderConnectionFactory>(new ReaderConnectionFactory(databaseConnectionString));
builder.Services.AddSingleton<IWriterConnectionFactory>(
    new WriterConnectionFactory(databaseConnectionString, SystemActor.Default, databaseProfile.Retry));
// The email workers: SQS and SES clients from the SDK's default credential chain and region
// (AWS_ENDPOINT_URL points both at LocalStack on a laptop), the email service port, and the
// listener that reads the notification queue for as long as the process runs.
builder.Services.AddSingleton(builder.Configuration.GetAWSOptions().CreateServiceClient<IAmazonSQS>());
builder.Services.AddSingleton(builder.Configuration.GetAWSOptions().CreateServiceClient<IAmazonSimpleEmailService>());
builder.Services.AddSingleton<IEmailSender, SesEmailSender>();
builder.Services.AddSingleton<EmailWorkerPorts>();
builder.Services.AddSingleton(NotificationSettings.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<EmailSendRecorder>();
builder.Services.AddSingleton<KnownBookingStore>();
builder.Services.AddSingleton<PlanDepartureReminderHandler>();
builder.Services.AddSingleton<NotificationEventDispatcher>();
builder.Services.AddHostedService<NotificationQueueListener>();
// Bounce and complaint reports from the email service keep the do-not-send list.
builder.Services.AddSingleton<RecordEmailServiceReportHandler>();
builder.Services.AddHostedService<EmailServiceReportListener>();
// Departure reminders fire from the known bookings' planned reminders, on a timer inside this process.
builder.Services.AddSingleton(provider => provider.GetRequiredService<NotificationSettings>().Links);
builder.Services.AddSingleton<SendDepartureReminderEmailHandler>();
builder.Services.AddHostedService<DepartureReminderScheduler>();

// The internal support operations (search the send log, resend an email) are called only by RouteGo's
// own services, which present the shared internal key. It is a secret, read once here; without it
// the service refuses to start, rather than starting with operations nobody can call.
string? internalKey = await startupSecrets.ResolveAsync(InternalKeySecret.SecretId);
if (string.IsNullOrEmpty(internalKey))
{
    string internalKeyHelp = $"grant the execution role secretsmanager:GetSecretValue on '{InternalKeySecret.SecretId}'";
    if (builder.Environment.IsDevelopment())
    {
        internalKeyHelp = $"set it once: dotnet user-secrets set \"{InternalKeySecret.SecretId}\" '<the internal key>' --id routego-platform";
    }
    throw new InvalidOperationException($"No internal key — {internalKeyHelp}.");
}
builder.Services.AddAuthentication(InternalKeyAuthenticationOptions.SchemeName)
    .AddScheme<InternalKeyAuthenticationOptions, InternalKeyAuthenticationHandler>(
        InternalKeyAuthenticationOptions.SchemeName, options => options.Key = internalKey);
builder.Services.AddAuthorization();

// OpenAPI: one document per Feature Area, from the explicit list (references/host-components.md).
// new_slice.py appends an area string here when a NEW area's first slice lands.
string[] areas = new[] { "Notification" };
foreach (string area in areas)
{
    builder.Services.AddOpenApi(area, o =>
    {
        string areaName = area;
        o.ShouldInclude = api =>
            api.GroupName is null || string.Equals(api.GroupName, areaName, StringComparison.OrdinalIgnoreCase);
        o.AddOperationTransformer<ResponseHeadersTransformer>();
    });
}

WebApplication app = builder.Build();

app.UseExceptionHandler();                       // unhandled -> RFC 7807 problem+json
app.UseMiddleware<ResponseMetadataMiddleware>(); // X-Trace-Id, Server-Timing, ... on every response
app.UseAuthentication();                         // the internal key, on the operations marked [Authorize]
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();                                // /openapi/{area}.json per document
if (app.Environment.IsDevelopment())             // dev-only — the doc is not the published contract
{
    app.MapScalarApiReference(o =>
    {
        o.WithTitle("API Reference");
        for (int i = 0; i < areas.Length; i++)
        {
            o.AddDocument(areas[i], areas[i], $"/openapi/{areas[i]}.json", isDefault: i == 0);
        }
    });
    app.MapGet("/", () => Results.Redirect("/scalar")); // one URL to remember
}
app.MapGet("/health", () => Results.Ok("healthy")); // placeholder until the first slice lands

app.Run();

public partial class Program { } // exposes Program to WebApplicationFactory<Program> in tests

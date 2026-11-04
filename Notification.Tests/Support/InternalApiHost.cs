using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notification.Infrastructure;
using Notification.Infrastructure.Messaging;
using Notification.Infrastructure.Reminders;

namespace Notification.Tests.Support;

/// <summary>
/// Boots the real host for the internal support operations: over the fixture's database, holding
/// the test internal key, with the given email service and clock in place of the real ones, and
/// without the queue listeners and the reminder timer — nothing these tests do reads a queue or waits
/// for a reminder, and the listeners would try to reach a queue at startup.
/// </summary>
public static class InternalApiHost
{
    /// <summary>The internal key every host in the run holds, and every authorised test call presents.</summary>
    public const string InternalKey = "test-internal-key";

    // Read by LocalEntryPoints before the host exists, so it is the process environment rather than
    // the factory's UseEnvironment that makes the app seed its local configuration values.
    private const string EnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string Development = "Development";

    /// <summary>A host over <paramref name="database"/>, sending through <paramref name="emailSender"/> and reading <paramref name="clock"/>.</summary>
    public static ApiFactory Create(DatabaseFixture database, IEmailSender emailSender, IClock clock)
    {
        // Process-global, like the database secret the factory injects: every host in the run is
        // given these same values, so hosts created by different test classes never disagree.
        Environment.SetEnvironmentVariable(EnvironmentVariable, Development);
        Environment.SetEnvironmentVariable(InternalKeySecret.SecretId, InternalKey);
        return TestHost.For(database, services => ReplacePorts(services, emailSender, clock));
    }

    /// <summary>A client presenting the right internal key.</summary>
    public static HttpClient CreateClient(ApiFactory host)
    {
        return CreateClient(host, InternalKey);
    }

    /// <summary>A client presenting <paramref name="internalKey"/>, or no key at all when it is null.</summary>
    public static HttpClient CreateClient(ApiFactory host, string? internalKey)
    {
        HttpClient client = host.CreateClient();
        if (internalKey != null)
        {
            client.DefaultRequestHeaders.Add(InternalKeyAuthenticationOptions.HeaderName, internalKey);
        }

        return client;
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
    {
        ServiceDescriptor? hostedService = services.FirstOrDefault(descriptor => descriptor.ImplementationType == typeof(T));
        if (hostedService != null)
        {
            services.Remove(hostedService);
        }
    }

    private static void ReplacePorts(IServiceCollection services, IEmailSender emailSender, IClock clock)
    {
        RemoveHostedService<NotificationQueueListener>(services);
        RemoveHostedService<EmailServiceReportListener>(services);
        RemoveHostedService<DepartureReminderScheduler>(services);

        services.RemoveAll<IEmailSender>();
        services.AddSingleton(emailSender);
        services.RemoveAll<IClock>();
        services.AddSingleton(clock);
    }
}

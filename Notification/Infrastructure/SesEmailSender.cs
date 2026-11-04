using Amazon.Runtime;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;

namespace Notification.Infrastructure;

/// <summary>
/// Sends email through Amazon SES. Only SES's MessageRejected is a refusal for good; every other
/// failure — throttling, an outage, a timeout, and also a missing send permission or an unverified
/// sender — is temporary, because it is fixed outside the email and then retried or replayed.
/// It calls the original SES API rather than SES v2: both deliver the same email on AWS, and the
/// original is the one LocalStack's free edition emulates for local runs.
/// </summary>
public sealed class SesEmailSender : IEmailSender
{
    private const string DefaultDisplayName = "RouteGo";
    private const string Utf8 = "UTF-8";

    private readonly IAmazonSimpleEmailService _ses;
    private readonly string _fromAddress;

    public SesEmailSender(IAmazonSimpleEmailService ses, IConfiguration configuration)
    {
        _ses = ses;
        string displayName = configuration[ConfigKeys.SenderDisplayName] ?? DefaultDisplayName;
        _fromAddress = $"\"{displayName}\" <{configuration[ConfigKeys.SenderAddress]}>";
    }

    /// <inheritdoc />
    public async Task<EmailSendResult> SendAsync(
        string toAddress, EmailMessage message, CancellationToken cancellationToken)
    {
        SendEmailRequest request = BuildRequest(_fromAddress, toAddress, message);
        try
        {
            SendEmailResponse response = await _ses.SendEmailAsync(request, cancellationToken);
            return EmailSendResult.Accepted(response.MessageId);
        }
        catch (MessageRejectedException exception)
        {
            return EmailSendResult.Refused(exception.Message);
        }
        catch (AmazonServiceException exception)
        {
            return EmailSendResult.Unavailable($"{exception.ErrorCode}: {exception.Message}");
        }
        catch (AmazonClientException exception)
        {
            return EmailSendResult.Unavailable(exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return EmailSendResult.Unavailable(exception.Message);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The SDK's own request timeout, not a shutdown: the email service was too slow.
            return EmailSendResult.Unavailable(exception.Message);
        }
    }

    private static SendEmailRequest BuildRequest(string fromAddress, string toAddress, EmailMessage message)
    {
        var body = new Body
        {
            Html = new Content { Charset = Utf8, Data = message.HtmlBody },
            Text = new Content { Charset = Utf8, Data = message.TextBody },
        };
        var content = new Message
        {
            Subject = new Content { Charset = Utf8, Data = message.Subject },
            Body = body,
        };
        return new SendEmailRequest
        {
            Source = fromAddress,
            Destination = new Destination { ToAddresses = new List<string> { toAddress } },
            Message = content,
        };
    }
}

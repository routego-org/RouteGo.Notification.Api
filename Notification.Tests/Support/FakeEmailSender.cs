namespace Notification.Tests.Support;

/// <summary>
/// An email service that answers each send with the next scripted result — accepted by default —
/// and remembers every email it was handed.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private const string DefaultReceipt = "ses-receipt-1";

    private readonly Queue<EmailSendResult> _results;
    private readonly List<SentEmail> _sent = new List<SentEmail>();

    public FakeEmailSender(params EmailSendResult[] results)
    {
        _results = new Queue<EmailSendResult>(results);
    }

    /// <summary>Every email handed to the service, in order, whatever the service answered.</summary>
    public IReadOnlyList<SentEmail> Sent
    {
        get { return _sent; }
    }

    /// <inheritdoc />
    public Task<EmailSendResult> SendAsync(string toAddress, EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Add(new SentEmail(toAddress, message));
        var result = EmailSendResult.Accepted(DefaultReceipt);
        if (_results.Count > 0)
        {
            result = _results.Dequeue();
        }

        return Task.FromResult(result);
    }
}

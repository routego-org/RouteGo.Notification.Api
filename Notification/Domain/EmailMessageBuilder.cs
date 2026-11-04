using System.Net;
using System.Text;

namespace Notification.Domain;

/// <summary>
/// Builds an email in the layout every RouteGo email shares: an HTML and a plain-text version with
/// the same words, a link shown as a button in HTML and as its full web address in plain text,
/// and the RouteGo sign-off at the end. Each email supplies only its own subject and paragraphs.
/// </summary>
public sealed class EmailMessageBuilder
{
    private const string SignOff = "— Team RouteGo";
    private const string ButtonStyle =
        "display:inline-block;padding:10px 18px;background:#1a73e8;color:#ffffff;"
        + "text-decoration:none;border-radius:4px";

    private readonly StringBuilder _html = new StringBuilder();
    private readonly StringBuilder _text = new StringBuilder();

    /// <summary>Adds one paragraph; each line keeps its own line inside the paragraph.</summary>
    public void Paragraph(params string[] lines)
    {
        string[] encodedLines = lines.Select(line => WebUtility.HtmlEncode(line)).ToArray();
        string htmlParagraph = $"<p>{string.Join("<br>", encodedLines)}</p>\n";
        string textParagraph = $"{string.Join("\n", lines)}\n\n";
        _html.Append(htmlParagraph);
        _text.Append(textParagraph);
    }

    /// <summary>Adds a link: a button in the HTML version, the label and full address in plain text.</summary>
    public void Button(string label, string url)
    {
        string href = WebUtility.HtmlEncode(url);
        string caption = WebUtility.HtmlEncode(label);
        string htmlButton = $"<p><a href=\"{href}\" style=\"{ButtonStyle}\">{caption}</a></p>\n";
        string textButton = $"{label}: {url}\n\n";
        _html.Append(htmlButton);
        _text.Append(textButton);
    }

    /// <summary>Ends the message with the sign-off and returns the finished email.</summary>
    public EmailMessage Build(string subject)
    {
        Paragraph(SignOff);
        string html = _html.ToString().TrimEnd('\n');
        string text = _text.ToString().TrimEnd('\n');
        return new EmailMessage(subject, html, text);
    }
}

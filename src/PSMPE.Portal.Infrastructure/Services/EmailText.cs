using System.Net;
using System.Text.RegularExpressions;

namespace PSMPE.Portal.Infrastructure.Services;

/// <summary>
/// Derives the plain-text alternative for an HTML email. Mail with only an HTML part, and no text
/// part, is a classic spam signal - Outlook.com in particular files it as junk - so every email the
/// app sends carries both, built here from the one HTML body callers already supply.
/// </summary>
public static partial class EmailText
{
    public static string FromHtml(string html)
    {
        // A link whose label differs from its target keeps the target: "Confirm my email: <url>".
        // Otherwise a text-only reader would see the label and no way to follow it.
        var text = AnchorRegex().Replace(html, match =>
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var label = WebUtility.HtmlDecode(TagRegex().Replace(match.Groups["label"].Value, string.Empty)).Trim();
            return label == href || label.Length == 0 ? href : $"{label}: {href}";
        });

        text = BreakRegex().Replace(text, Environment.NewLine);
        text = ParagraphEndRegex().Replace(text, Environment.NewLine + Environment.NewLine);
        text = WebUtility.HtmlDecode(TagRegex().Replace(text, string.Empty));

        // Collapse the runs of blank lines the paragraph handling can leave behind.
        text = Regex.Replace(text, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        return text.Trim();
    }

    [GeneratedRegex("<a\\s[^>]*href=\"(?<href>[^\"]*)\"[^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("</p\\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphEndRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}

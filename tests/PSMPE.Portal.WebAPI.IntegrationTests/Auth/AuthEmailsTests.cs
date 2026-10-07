using PSMPE.Portal.WebAPI.Extensions;
using Xunit;

namespace PSMPE.Portal.WebAPI.IntegrationTests.Auth;

public class AuthEmailsTests
{
    private const string Link = "https://portal.psmpe.org/verify-email?userId=1&token=abc%2Fdef";

    [Fact]
    public void VerifyEmail_CarriesTheLinkAsAButtonAndAsPlainTextForClientsThatStripLinks()
    {
        var (subject, html) = AuthEmails.VerifyEmail("Juan Dela Cruz", Link);

        // The link sits inside HTML, where "&" must be written "&amp;" - the browser turns it back.
        var encodedLink = System.Net.WebUtility.HtmlEncode(Link);

        Assert.Contains("PSMPE Portal", subject);
        Assert.Contains($"href=\"{encodedLink}\"", html);
        // The bare address appears a second time outside the anchor, so it survives a client that
        // strips or rewrites links - and a plain-text rendering still has something to copy.
        var occurrences = html.Split(encodedLink).Length - 1;
        Assert.Equal(2, occurrences);
    }

    [Fact]
    public void VerifyEmail_SaysWhyItWasSentAndThatItCanBeIgnored()
    {
        var (_, html) = AuthEmails.VerifyEmail("Juan", Link);

        Assert.Contains("If that wasn't you", html);
    }

    [Fact]
    public void VerifyEmail_EncodesTheDisplayNameSoItCannotInjectMarkup()
    {
        var (_, html) = AuthEmails.VerifyEmail("<script>alert(1)</script>", Link);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}

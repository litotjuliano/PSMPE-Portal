using PSMPE.Portal.Infrastructure.Services;
using Xunit;

namespace PSMPE.Portal.Infrastructure.UnitTests.Services;

public class EmailTextTests
{
    [Fact]
    public void FromHtml_PutsEachParagraphOnItsOwnLine()
    {
        var text = EmailText.FromHtml("<p>First.</p><p>Second.</p>");

        Assert.Equal($"First.{Environment.NewLine}{Environment.NewLine}Second.", text);
    }

    [Fact]
    public void FromHtml_KeepsALinksTargetWhenTheLabelDiffers()
    {
        var text = EmailText.FromHtml("<p><a href=\"https://portal.psmpe.org/verify?x=1&amp;y=2\">Confirm my email</a></p>");

        Assert.Equal("Confirm my email: https://portal.psmpe.org/verify?x=1&y=2", text);
    }

    [Fact]
    public void FromHtml_DoesNotRepeatALinkWhoseLabelIsItsAddress()
    {
        var text = EmailText.FromHtml("<a href=\"https://example.com\">https://example.com</a>");

        Assert.Equal("https://example.com", text);
    }

    [Fact]
    public void FromHtml_TurnsLineBreaksIntoNewlinesAndDecodesEntities()
    {
        var text = EmailText.FromHtml("<p>Tom &amp; Jerry<br>next line</p>");

        Assert.Equal($"Tom & Jerry{Environment.NewLine}next line", text);
    }
}

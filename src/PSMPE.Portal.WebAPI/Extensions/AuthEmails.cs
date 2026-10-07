using System.Net;

namespace PSMPE.Portal.WebAPI.Extensions;

/// <summary>
/// The wording of the account emails, in one place. The verification email used to be a bare
/// "click the link" snippet copied into four call sites - register, public resend, and the admin
/// single and bulk resend - and a bare link with no context reads as spam to Outlook.com, which
/// was filing it as junk. One template means one thing to improve, and all four stay in step.
/// </summary>
public static class AuthEmails
{
    public static (string Subject, string Html) VerifyEmail(string displayName, string verificationLink)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayName) ? "there" : displayName.Trim());
        var link = WebUtility.HtmlEncode(verificationLink);

        var html =
            $"<p>Hi {name},</p>" +
            "<p>Thanks for registering with the PSMPE Portal. Please confirm your email address to activate your account:</p>" +
            $"<p><a href=\"{link}\">Confirm my email</a></p>" +
            $"<p>If the link above doesn't work, copy and paste this address into your browser:<br>{link}</p>" +
            "<p>You received this email because this address was used to register an account on the PSMPE Portal. " +
            "If that wasn't you, you can ignore this message - no account will be activated.</p>" +
            "<p>PSMPE Portal</p>";

        return ("Confirm your email address - PSMPE Portal", html);
    }
}

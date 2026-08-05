namespace PSMPE.Portal.Application.Common.Interfaces;

/// <summary>
/// Caps outbound account emails per address. Partitioned on the email address rather than the
/// client IP, so it lives here instead of in the rate limiter middleware - a limiter partition
/// function can't read the request body without buffering it for every request.
/// </summary>
public interface IEmailSendThrottle
{
    /// <summary>
    /// Records a send against <paramref name="emailAddress"/> and returns false if the address
    /// has already used its allowance for the current window.
    /// </summary>
    bool TryRecordSend(string emailAddress);
}

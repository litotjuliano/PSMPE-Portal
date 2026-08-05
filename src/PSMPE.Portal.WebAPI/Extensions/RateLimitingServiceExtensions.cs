using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace PSMPE.Portal.WebAPI.Extensions;

/// <summary>
/// Fixed-window limits on the public auth surface, partitioned by resolved client IP (see
/// UseForwardedHeaders in Program.cs - without it every partition key is the Docker bridge
/// gateway and all traffic shares one bucket).
///
/// Fixed window rather than sliding/token bucket: one counter per partition instead of a
/// timestamp log, and the limits are loose enough that a 2x burst across a window edge doesn't
/// matter.
///
/// This is deliberately only half the defence. A limiter partitioned on IP cannot see accounts,
/// and members sharing an office IP would be throttled collectively if it tried, so per-account
/// protection lives elsewhere: Identity lockout (see AuthController.Login and the options.Lockout
/// block in Infrastructure's DependencyInjection) and a per-address email send throttle (see
/// MemoryCacheEmailSendThrottle). Both are required for this to be meaningful - on their own the
/// limits here are per IP, and an attacker with a handful of proxies gets a fresh bucket per
/// address.
/// </summary>
public static class RateLimitingServiceExtensions
{
    public const string AuthIpPolicy = "auth-ip";
    public const string AuthEmailSendPolicy = "auth-email-send";
    public const string UsernameProbePolicy = "username-probe";

    private static int _proxyIpWarningLogged;

    /// <summary>
    /// The config sections each carrying a PermitLimit/WindowMinutes pair. EmailSendPerAddress is
    /// validated here despite being enforced by MemoryCacheEmailSendThrottle rather than by a
    /// limiter policy: a WindowMinutes of 0 there makes every window end the instant it opens, so
    /// the throttle silently permits every send. That fails open on the control protecting our
    /// SMTP reputation, so it gets the same startup rejection as the rest.
    /// </summary>
    private static readonly string[] LimitSections =
        ["AuthIp", "AuthEmailSend", "UsernameProbe", "Global", "EmailSendPerAddress"];

    private static readonly string[] LimitSettings = ["PermitLimit", "WindowMinutes"];

    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Reject a malformed value here, while the host is still starting, mirroring Program.cs's
        // KnownNetworks guard. This matters most for the kill switch: it is what an operator
        // reaches for when limiting is already hurting members, so a typo in it ("no", "0",
        // "off") must not be able to make things worse than the problem being rolled back. A
        // named startup crash is diagnosable; a deferred throw is not, because the reads below
        // are memoised and an exception raised inside one would be cached and replayed on every
        // request - including /health - for the life of the process.
        ValidateSettings(configuration);

        // The reads themselves stay deferred. This runs while the host is still being built, and
        // under WebApplicationFactory the test's configuration sources are only layered on at
        // Build() - so an eager read sees neither the kill switch nor the configured proxy
        // networks and silently falls back to the defaults. Lazy keeps the read-once cost while
        // moving it to the first request, by which point configuration is complete in every host.
        // Every factory below is total by construction; see ReadBool/ReadInt.
        var enabled = new Lazy<bool>(() => ReadBool(configuration, "RateLimit:Enabled", true));
        var knownNetworks = new Lazy<IPNetwork[]>(() => ParseKnownNetworks(configuration));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddFixedWindowPolicy(options, AuthIpPolicy, configuration, "AuthIp", 20, 5, enabled, knownNetworks);
            AddFixedWindowPolicy(options, AuthEmailSendPolicy, configuration, "AuthEmailSend", 10, 60, enabled, knownNetworks);
            AddFixedWindowPolicy(options, UsernameProbePolicy, configuration, "UsernameProbe", 30, 1, enabled, knownNetworks);

            // Applies on top of the endpoint policies above, as a blanket ceiling on everything else.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!enabled.Value)
                {
                    return RateLimitPartition.GetNoLimiter("disabled");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientIpPartitionKey(context, knownNetworks.Value),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = ReadInt(configuration, "RateLimit:Global:PermitLimit", 300),
                        Window = TimeSpan.FromMinutes(ReadInt(configuration, "RateLimit:Global:WindowMinutes", 1)),
                        QueueLimit = 0
                    });
            });

            // Matches ExceptionHandlingMiddleware's shape so the API has one error contract.
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    // Round up, never down, and never below 1 - a "Retry-After: 0" would tell a
                    // compliant client to retry straight into another 429.
                    // Note this OVER-states the wait: FixedWindowRateLimiter reports the whole
                    // window here, not the time left in it, so a rejection 4 seconds into a
                    // 5-minute window still advertises 5 minutes. That is the safe direction for
                    // a header, but it is why the UI says "a few minutes" rather than counting
                    // down - see LoginPage and ResetPasswordPage.
                    var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    context.HttpContext.Response.Headers.RetryAfter =
                        seconds.ToString(NumberFormatInfo.InvariantInfo);
                }

                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests.",
                    Detail = "You've made too many requests in a short period. Please wait and try again."
                };

                context.HttpContext.Response.StatusCode = problem.Status.Value;

                // contentType must be passed to WriteAsJsonAsync, not assigned to Response.ContentType
                // beforehand: the overload without it unconditionally overwrites the header with
                // "application/json", and clients keying off "problem+json" would never see it.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    problem, options: null, contentType: "application/problem+json", cancellationToken);
            };
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        IConfiguration configuration,
        string configSection,
        int defaultPermitLimit,
        int defaultWindowMinutes,
        Lazy<bool> enabled,
        Lazy<IPNetwork[]> knownNetworks)
    {
        options.AddPolicy(policyName, context =>
        {
            if (!enabled.Value)
            {
                return RateLimitPartition.GetNoLimiter("disabled");
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                $"{policyName}:{ClientIpPartitionKey(context, knownNetworks.Value)}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = ReadInt(configuration, $"RateLimit:{configSection}:PermitLimit", defaultPermitLimit),
                    Window = TimeSpan.FromMinutes(
                        ReadInt(configuration, $"RateLimit:{configSection}:WindowMinutes", defaultWindowMinutes)),
                    QueueLimit = 0
                });
        });
    }

    /// <summary>
    /// Rejects a value the readers below would otherwise have to cope with silently. Only values
    /// that are actually present are checked - an absent key legitimately means "use the default".
    /// Limits must be at least 1, because FixedWindowRateLimiterOptions throws on a PermitLimit
    /// below 1 or a non-positive Window, and it would do so inside the partition factory, i.e.
    /// once per request rather than once at startup.
    /// </summary>
    private static void ValidateSettings(IConfiguration configuration)
    {
        const string enabledKey = "RateLimit:Enabled";
        var enabled = configuration[enabledKey];
        if (enabled is not null && !bool.TryParse(enabled, out _))
        {
            throw new InvalidOperationException(
                $"{enabledKey} has an invalid value '{enabled}'. Expected 'true' or 'false'.");
        }

        foreach (var section in LimitSections)
        {
            foreach (var setting in LimitSettings)
            {
                var key = $"RateLimit:{section}:{setting}";
                var value = configuration[key];
                if (value is null)
                {
                    continue;
                }

                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    || parsed < 1)
                {
                    throw new InvalidOperationException(
                        $"{key} has an invalid value '{value}'. Expected a whole number of at least 1.");
                }
            }
        }
    }

    /// <summary>
    /// Total by construction - never throws. ValidateSettings has already rejected anything
    /// unparseable at startup, so the fallback is unreachable in a real host. It exists because
    /// both readers run inside memoised paths (a Lazy, and a partition factory whose result is
    /// cached per partition), where a throw would be replayed on every later request instead of
    /// surfacing once. Falling back to the caller's default keeps limiting on, which is the safe
    /// direction for a value that failed to parse.
    /// </summary>
    private static bool ReadBool(IConfiguration configuration, string key, bool defaultValue) =>
        bool.TryParse(configuration[key], out var value) ? value : defaultValue;

    /// <inheritdoc cref="ReadBool"/>
    private static int ReadInt(IConfiguration configuration, string key, int defaultValue) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && value >= 1
            ? value
            : defaultValue;

    /// <summary>
    /// Read purely to recognise a proxy address in the warning below. The actual trust decision
    /// belongs to ForwardedHeadersMiddleware, configured from this same key in Program.cs, which
    /// is also where an invalid value is rejected with a named, actionable startup error. Two
    /// consequences follow. Blank, whitespace and "," must still yield the default - each reaches
    /// an empty entry list by a different route, and an empty list would silently disable the one
    /// warning that catches a broken trust chain (the same fail-open shape Program.cs guards
    /// against, for a smaller stake). And a malformed entry is skipped rather than thrown on:
    /// this resolves on the first request, so throwing would turn a diagnostic aid into a 500 on
    /// live traffic over a value Program.cs has already had its say about.
    /// </summary>
    private static IPNetwork[] ParseKnownNetworks(IConfiguration configuration)
    {
        const string defaultKnownNetworks = "172.16.0.0/12";
        var cidrs = configuration["ForwardedHeaders:KnownNetworks"] ?? string.Empty;
        var entries = cidrs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            entries = [defaultKnownNetworks];
        }

        return entries
            .Select(cidr => IPNetwork.TryParse(cidr, out var network) ? network : (IPNetwork?)null)
            .OfType<IPNetwork>()
            .ToArray();
    }

    /// <summary>
    /// A resolved IP still inside the proxy network means X-Forwarded-For isn't arriving, which
    /// would silently put every caller in one bucket and look like "the whole site is throttled".
    /// Warn once per process rather than per request.
    /// </summary>
    private static string ClientIpPartitionKey(HttpContext context, IPNetwork[] knownNetworks)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (knownNetworks.Any(network => network.Contains(ip))
            && Interlocked.Exchange(ref _proxyIpWarningLogged, 1) == 0)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RateLimiting")
                .LogWarning(
                    "Resolved client IP {ClientIp} is inside a known-proxy network - X-Forwarded-For is probably not being set by nginx. Every request is sharing one rate limit partition.",
                    ip);
        }

        return ip.ToString();
    }
}

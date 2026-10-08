using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace Cv.Editor.Security;

/// <summary>Who Cloudflare Access signed in.</summary>
public sealed record AccessIdentity(string Email);

/// <summary>The public keys that sign Access tokens. Replaced by a fixed key in tests.</summary>
public interface IAccessKeySource
{
    /// <param name="refresh">Fetch again, because a token names a key that isn't cached (keys rotate every 6 weeks).</param>
    Task<ICollection<SecurityKey>> GetKeysAsync(bool refresh, CancellationToken cancellationToken);
}

/// <summary>
/// Checks the <c>Cf-Access-Jwt-Assertion</c> header that Cloudflare Access adds to every request
/// it lets through (ADR 0004 §3): an RS256 token signed by the team's keys, issued by the team
/// domain, for this application's AUD tag, and not expired. Access is the gate; this check means
/// a request that reaches the editor some other way (a misconfigured or deleted Access
/// application, another tunnel route) still gets nothing.
/// </summary>
public sealed class AccessTokenValidator(IOptions<EditorOptions> options, IAccessKeySource keys, ILogger<AccessTokenValidator> logger)
{
    public const string HeaderName = "Cf-Access-Jwt-Assertion";

    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>The signed-in identity, or null when the token is missing or invalid (the reason is logged).</summary>
    public async Task<AccessIdentity?> ValidateAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        JsonWebToken jwt;
        try
        {
            jwt = Handler.ReadJsonWebToken(token);
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Refused a request through the tunnel: the Access token is malformed");
            return null;
        }

        var signingKeys = await keys.GetKeysAsync(refresh: false, cancellationToken);
        if (!signingKeys.Any(k => k.KeyId == jwt.Kid))
        {
            signingKeys = await keys.GetKeysAsync(refresh: true, cancellationToken); // rotated since the last fetch
        }

        var result = await Handler.ValidateTokenAsync(jwt, Parameters(options.Value, signingKeys));
        if (!result.IsValid)
        {
            logger.LogWarning("Refused a request through the tunnel: {Reason}", result.Exception?.Message);
            return null;
        }

        return result.Claims.TryGetValue("email", out var email) && email is string { Length: > 0 } address
            ? new AccessIdentity(address)
            : null;
    }

    internal static string Issuer(EditorOptions options) => $"https://{options.AccessTeamDomain}";

    private static TokenValidationParameters Parameters(EditorOptions options, ICollection<SecurityKey> signingKeys) => new()
    {
        ValidIssuer = Issuer(options),
        ValidAudience = options.AccessAudience,
        IssuerSigningKeys = signingKeys,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}

/// <summary>
/// The team's keys from <c>https://{team domain}/cdn-cgi/access/certs</c>, cached and refreshed
/// by <see cref="ConfigurationManager{T}"/>: at most every 5 minutes on demand, and every 12 hours
/// regardless.
/// </summary>
internal sealed class CloudflareAccessKeySource(IOptions<EditorOptions> options) : IAccessKeySource
{
    private readonly Lazy<ConfigurationManager<JsonWebKeySet>> _keys = new(() => new(
        $"{AccessTokenValidator.Issuer(options.Value)}/cdn-cgi/access/certs",
        new KeySetRetriever(),
        new HttpDocumentRetriever { RequireHttps = true }));

    public async Task<ICollection<SecurityKey>> GetKeysAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (refresh)
        {
            _keys.Value.RequestRefresh();
        }
        return (await _keys.Value.GetConfigurationAsync(cancellationToken)).GetSigningKeys();
    }

    private sealed class KeySetRetriever : IConfigurationRetriever<JsonWebKeySet>
    {
        public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel) =>
            new(await retriever.GetDocumentAsync(address, cancel));
    }
}

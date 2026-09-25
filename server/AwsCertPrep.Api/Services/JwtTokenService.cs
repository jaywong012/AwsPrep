using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AwsCertPrep.Api.Services;

/// <summary>An issued access token and the moment it stops being accepted.</summary>
public readonly record struct AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Mints the bearer tokens the SPA holds.
///
/// Takes <see cref="TimeProvider"/> rather than reading the clock directly, so expiry is testable
/// without sleeping - the same reason the exam deadline logic is written the way it is.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken Issue(AppUser user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),

            // The security stamp changes when the password does. Nothing checks it yet, but
            // carrying it is what makes "sign out everywhere" a later one-line validation rather
            // than a token-format change.
            new("ast", user.SecurityStamp ?? string.Empty),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256));

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    /// <summary>
    /// The key the tokens are signed with and validated against. One method so the two can never
    /// drift apart - a mismatch here rejects every token the API itself just issued.
    /// </summary>
    public static SymmetricSecurityKey SigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.Key ?? throw new InvalidOperationException(
            "Jwt:Key is not configured. The API should have refused to start.")));

    public static TokenValidationParameters ValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey(options),

        // The default five minutes is generous enough to let a just-expired token through well
        // past the point a timed exam would care about. Thirty seconds covers real clock drift.
        ClockSkew = TimeSpan.FromSeconds(30),

        NameClaimType = ClaimTypes.NameIdentifier,
    };
}

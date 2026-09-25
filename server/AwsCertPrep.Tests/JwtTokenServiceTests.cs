using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Services;
using Microsoft.IdentityModel.Tokens;

namespace AwsCertPrep.Tests;

/// <summary>
/// The access tokens every signed-in request is judged by.
///
/// The claims tests say the token carries who it is for; the two that matter most say what it
/// refuses. A token that validates under the wrong key, or after it has expired, is not a bug in
/// one endpoint - it is every endpoint at once.
///
/// Note the asymmetry in how time is handled. Issuing reads the injected
/// <see cref="TimeProvider"/>, so expiry arithmetic is testable exactly. Validating does not:
/// <see cref="JwtSecurityTokenHandler"/> compares against the real system clock and takes no
/// provider. So a test that wants a token accepted must issue it around the real present, and a
/// test that wants one rejected as expired backdates the issuing clock rather than advancing a
/// validation clock that does not exist.
/// </summary>
public class JwtTokenServiceTests
{
    private const string Key = "a-test-signing-key-of-at-least-32-bytes";

    /// <summary>A clock parked at one instant, so an issued token's lifetime is exact.</summary>
    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static JwtOptions Options(string key = Key, int minutes = 60) =>
        new() { Key = key, Issuer = "AwsCertPrep", Audience = "AwsCertPrep.Spa", AccessTokenMinutes = minutes };

    private static AppUser User() =>
        new() { Id = "user-id-1", Email = "learner@example.com", SecurityStamp = "stamp-1" };

    private static JwtTokenService Subject(JwtOptions options, DateTime? issuedAt = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options),
            new FixedClock(issuedAt ?? DateTime.UtcNow));

    /// <summary>
    /// Validates the way the API does. MapInboundClaims is off in both places: left on, the
    /// handler rewrites "sub" and "email" into WS-Federation URIs, and a test that validated with
    /// the mapping on would pass against an API that could not find its own claims.
    /// </summary>
    private static ClaimsPrincipal Validate(string token, JwtOptions options) =>
        new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
            token, JwtTokenService.ValidationParameters(options), out _);

    [Fact]
    public void The_token_names_the_user_it_was_issued_for()
    {
        var token = Subject(Options()).Issue(User()).Token;

        var principal = Validate(token, Options());

        // NameClaimType is NameIdentifier, which is what CurrentUser.GetUserId reads.
        Assert.Equal("user-id-1", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("learner@example.com", principal.FindFirstValue(JwtRegisteredClaimNames.Email));
    }

    [Fact]
    public void The_token_expires_after_the_configured_lifetime()
    {
        var issuedAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

        var issued = Subject(Options(minutes: 45), issuedAt).Issue(User());

        Assert.Equal(issuedAt.AddMinutes(45), issued.ExpiresAtUtc);
    }

    [Fact]
    public void A_token_signed_with_another_key_is_refused()
    {
        var token = Subject(Options(key: "an-entirely-different-key-also-32-bytes")).Issue(User()).Token;

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() => Validate(token, Options()));
    }

    [Fact]
    public void An_expired_token_is_refused()
    {
        // Issued two hours ago with a one-hour life, so it expired an hour before this ran -
        // comfortably past the 30-second clock skew the parameters allow.
        var token = Subject(Options(minutes: 60), DateTime.UtcNow.AddHours(-2)).Issue(User()).Token;

        Assert.Throws<SecurityTokenExpiredException>(() => Validate(token, Options()));
    }

    [Fact]
    public void A_token_for_another_audience_is_refused()
    {
        var token = Subject(Options()).Issue(User()).Token;

        var elsewhere = Options();
        elsewhere.Audience = "SomeOtherApp";

        Assert.Throws<SecurityTokenInvalidAudienceException>(() => Validate(token, elsewhere));
    }
}

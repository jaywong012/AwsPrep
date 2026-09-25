using AwsCertPrep.Api.Application.Options;

namespace AwsCertPrep.Tests;

/// <summary>
/// The startup check on the signing key.
///
/// Same shape as <see cref="AdminKeyTests"/>, and for the same reason: the cases worth pinning are
/// the misconfigurations a running app should never reach. The asymmetry between environments is
/// the point - Development must stay runnable on a fresh clone with no secrets set, and every
/// other environment must refuse to start rather than serve tokens anyone could forge.
/// </summary>
public class JwtOptionsTests
{
    private const string GoodKey = "a-signing-key-that-is-at-least-32-bytes";

    private static JwtOptions Options(string? key) => new() { Key = key };

    [Fact]
    public void A_missing_key_is_fatal_outside_development()
    {
        Assert.NotNull(Options(null).Validate(isDevelopment: false));
    }

    [Fact]
    public void A_missing_key_is_allowed_in_development()
    {
        // Program.cs mints a throwaway key for the process in this case.
        Assert.Null(Options(null).Validate(isDevelopment: true));
    }

    [Fact]
    public void A_blank_key_counts_as_missing()
    {
        Assert.NotNull(Options("   ").Validate(isDevelopment: false));
    }

    [Fact]
    public void A_short_key_is_refused_even_in_development()
    {
        // SymmetricSecurityKey accepts a short key happily, and a short key is the entire attack
        // on HS256 - so this one is rejected wherever it appears, deliberately unlike a missing
        // key. Someone who set a key meant it; a 16-byte one is a mistake worth stopping.
        Assert.NotNull(Options("too-short").Validate(isDevelopment: true));
        Assert.NotNull(Options("too-short").Validate(isDevelopment: false));
    }

    [Fact]
    public void A_long_enough_key_is_accepted()
    {
        Assert.Null(Options(GoodKey).Validate(isDevelopment: false));
    }

    [Fact]
    public void An_implausible_token_lifetime_is_refused()
    {
        var options = Options(GoodKey);

        options.AccessTokenMinutes = 0;
        Assert.NotNull(options.Validate(isDevelopment: false));

        // Past a year the token has stopped being a session and become a permanent credential
        // living in localStorage. That may be what someone wants, but it should be a decision
        // rather than an extra zero in a config file.
        options.AccessTokenMinutes = 525_601;
        Assert.NotNull(options.Validate(isDevelopment: false));
    }

    [Fact]
    public void A_long_lived_session_is_allowed()
    {
        // The default is 30 days and the SPA renews it on every start, which is what keeps a
        // learner signed in between study sessions. Pinned here so shortening the ceiling later
        // has to be a deliberate change rather than a silent one.
        var options = Options(GoodKey);

        Assert.Equal(43_200, options.AccessTokenMinutes);
        Assert.Null(options.Validate(isDevelopment: false));

        options.AccessTokenMinutes = 525_600;
        Assert.Null(options.Validate(isDevelopment: false));
    }
}

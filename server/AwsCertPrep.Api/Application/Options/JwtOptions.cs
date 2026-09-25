using System.Text;

namespace AwsCertPrep.Api.Application.Options;

/// <summary>
/// How access tokens are signed and how long they last.
///
/// The signing key is the one secret in this app that cannot fail soft. An unset
/// <see cref="AdminOptions.ApiKey"/> disables the endpoints it guards, which is safe; an unset
/// signing key has no safe interpretation, so the API refuses to start without one outside
/// Development. See <see cref="Validate"/>.
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 signing secret. At least 32 bytes: <c>SymmetricSecurityKey</c> will accept a
    /// shorter one, and a short key is the whole attack on HS256.
    /// </summary>
    public string? Key { get; set; }

    public string Issuer { get; set; } = "AwsCertPrep";

    public string Audience { get; set; } = "AwsCertPrep.Spa";

    /// <summary>
    /// How long a token is good for. Thirty days, and the SPA rolls it forward through
    /// <c>POST /api/auth/refresh</c> every time it starts, so a learner who opens the app at all
    /// within a month is never signed out - which is the point: this is a study app someone comes
    /// back to between sessions, not a bank.
    ///
    /// The cost is stated rather than hidden: there is no refresh-token store and nothing checks
    /// the security stamp, so this is also how long a stolen token stays usable, and a password
    /// change does not end a session already in flight. Shorten it if the deployment is somewhere
    /// less trusted than one person's machine.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 43_200;

    /// <summary>The smallest key we will sign with, in bytes.</summary>
    internal const int MinimumKeyBytes = 32;

    /// <summary>
    /// Says what is wrong with these options, or null when they are usable.
    ///
    /// Pure and separately testable rather than inline in Program.cs, for the same reason
    /// <see cref="Services.AdminKey.Matches"/> is: the interesting cases are the ones a running
    /// app should never reach.
    /// </summary>
    internal string? Validate(bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            // Development mints a throwaway key per process instead (see Program.cs), so an
            // absent key is only fatal everywhere else.
            return isDevelopment
                ? null
                : $"{SectionName}:Key is not configured. Set it as an environment variable "
                  + $"({SectionName}__Key) or from a secret store before starting the API.";
        }

        var bytes = Encoding.UTF8.GetByteCount(Key);
        if (bytes < MinimumKeyBytes)
        {
            return $"{SectionName}:Key is {bytes} bytes; HMAC-SHA256 needs at least "
                   + $"{MinimumKeyBytes}. Use a longer random secret.";
        }

        // A year is the ceiling rather than the intent. Past that the token stops being a session
        // and becomes a permanent credential sitting in localStorage, which is a different thing
        // and should be a deliberate decision, not a config typo.
        if (AccessTokenMinutes is < 1 or > 525_600)
        {
            return $"{SectionName}:AccessTokenMinutes is {AccessTokenMinutes}; it must be between "
                   + "1 and 525600 (one year). A token cannot be revoked before it expires.";
        }

        return null;
    }
}

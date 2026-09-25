using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Identity;

namespace AwsCertPrep.Api.Application.Auth;

/// <summary>Why an attempt to sign in or register did not produce a token.</summary>
public enum AuthFailure
{
    None,

    /// <summary>Wrong password, or no such account. Deliberately indistinguishable.</summary>
    InvalidCredentials,

    /// <summary>Identity's per-account lockout has engaged.</summary>
    LockedOut,

    /// <summary>Identity refused the registration - duplicate email, weak password.</summary>
    Rejected,
}

/// <summary>
/// A token, or the reason there isn't one.
///
/// Returned rather than thrown. A wrong password is the single most ordinary thing that can
/// happen to a sign-in endpoint, and modelling it as an exception made every one of them a
/// stack trace logged at error level - noise that buries real faults, and that anyone guessing
/// passwords could generate at will.
/// </summary>
public sealed record AuthOutcome(AuthResultDto? Success, AuthFailure Failure, string? Message)
{
    public static AuthOutcome Ok(AuthResultDto result) => new(result, AuthFailure.None, null);

    public static AuthOutcome Refused(AuthFailure failure, string message) =>
        new(null, failure, message);
}

/// <summary>Creates an account and signs it straight in, so registering is one round trip.</summary>
public record RegisterCommand(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password) : ICommand<AuthOutcome>;

internal class RegisterHandler(
    UserManager<AppUser> users,
    JwtTokenService tokens,
    ILogger<RegisterHandler> logger)
    : ICommandHandler<RegisterCommand, AuthOutcome>
{
    public async Task<AuthOutcome> HandleAsync(RegisterCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();

        // UserName is required by Identity and unused by this app, so it mirrors the email.
        var user = new AppUser { UserName = email, Email = email };

        var created = await users.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            // Identity's codes are machine-readable and its descriptions are already written for
            // a person ("Passwords must be at least 10 characters."), so the descriptions are
            // what crosses the wire. Joined, because a weak password usually breaks several
            // rules at once and fixing them one refusal at a time is miserable.
            var reason = string.Join(" ", created.Errors.Select(e => e.Description));
            logger.LogInformation("Registration refused for {Email}: {Codes}",
                email, string.Join(",", created.Errors.Select(e => e.Code)));

            return AuthOutcome.Refused(AuthFailure.Rejected, reason);
        }

        logger.LogInformation("Registered {UserId}.", user.Id);

        var token = tokens.Issue(user);
        return AuthOutcome.Ok(new AuthResultDto(token.Token, token.ExpiresAtUtc, user.Id, user.Email!));
    }
}

/// <summary>Exchanges an email and password for an access token.</summary>
public record LoginCommand(
    [property: Required] string Email,
    [property: Required] string Password) : ICommand<AuthOutcome>;

internal class LoginHandler(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    JwtTokenService tokens,
    ILogger<LoginHandler> logger)
    : ICommandHandler<LoginCommand, AuthOutcome>
{
    /// <summary>The one answer an unsuccessful caller gets, whichever half of it was wrong.</summary>
    private const string RefusedMessage = "Email or password is incorrect.";

    public async Task<AuthOutcome> HandleAsync(LoginCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            logger.LogInformation("Login refused: no account for the address given.");
            return AuthOutcome.Refused(AuthFailure.InvalidCredentials, RefusedMessage);
        }

        // lockoutOnFailure is what makes Identity's per-account lockout actually engage. It is the
        // per-account half of the brute-force defence; the "auth" rate-limit policy is the
        // per-address half, and neither covers the other's case.
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            logger.LogWarning("Login refused: {UserId} is locked out.", user.Id);
            return AuthOutcome.Refused(AuthFailure.LockedOut,
                "Too many failed attempts. Try again in a few minutes.");
        }

        if (!result.Succeeded)
        {
            logger.LogInformation("Login refused for {UserId}: wrong password.", user.Id);
            return AuthOutcome.Refused(AuthFailure.InvalidCredentials, RefusedMessage);
        }

        var token = tokens.Issue(user);
        return AuthOutcome.Ok(new AuthResultDto(token.Token, token.ExpiresAtUtc, user.Id, user.Email!));
    }
}

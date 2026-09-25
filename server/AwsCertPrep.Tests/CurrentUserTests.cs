using System.Security.Claims;
using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// Reading the learner identity from the authenticated principal.
///
/// This is the function standing between a missing claim and a row written with a blank user key.
/// The old header accessor answered "local" when it could not find a usable value, which quietly
/// merged callers into one shared learner; this one throws, and these tests are what keeps it
/// throwing.
/// </summary>
public class CurrentUserTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Test"));

    [Fact]
    public void The_name_identifier_claim_is_the_user_id()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "user-id-1"));

        Assert.Equal("user-id-1", principal.GetUserId());
    }

    [Fact]
    public void A_bare_sub_claim_is_accepted()
    {
        // A token validated with a different NameClaimType mapping still carries "sub".
        var principal = PrincipalWith(new Claim("sub", "user-id-2"));

        Assert.Equal("user-id-2", principal.GetUserId());
    }

    [Fact]
    public void A_principal_with_no_subject_is_refused()
    {
        Assert.Throws<UnauthorizedAccessException>(() => new ClaimsPrincipal().GetUserId());
    }

    [Fact]
    public void A_blank_subject_is_refused()
    {
        var principal = PrincipalWith(new Claim(ClaimTypes.NameIdentifier, "   "));

        Assert.Throws<UnauthorizedAccessException>(() => principal.GetUserId());
    }
}

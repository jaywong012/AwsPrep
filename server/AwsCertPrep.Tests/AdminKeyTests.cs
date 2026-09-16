using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// The administrator key comparison.
///
/// This guards the endpoints that destroy shared data, so the cases that matter most are the ones
/// where a caller presents nothing at all. An empty header must never match, including against an
/// empty configured key - a misconfiguration must fail closed rather than open the door.
/// </summary>
public class AdminKeyTests
{
    [Fact]
    public void The_configured_key_matches()
    {
        Assert.True(AdminKey.Matches("correct-horse", "correct-horse"));
    }

    [Fact]
    public void A_different_key_does_not_match()
    {
        Assert.False(AdminKey.Matches("wrong-key", "correct-horse"));
    }

    [Fact]
    public void An_absent_header_does_not_match()
    {
        // Headers["X-Admin-Key"].ToString() on a missing header yields "", which is what arrives
        // here for an unauthenticated call.
        Assert.False(AdminKey.Matches("", "correct-horse"));
    }

    [Fact]
    public void An_absent_header_does_not_match_an_empty_configured_key()
    {
        // The dangerous case: if this returned true, every caller would be an operator the moment
        // the key was left blank.
        Assert.False(AdminKey.Matches("", ""));
    }

    [Fact]
    public void The_comparison_is_case_sensitive()
    {
        Assert.False(AdminKey.Matches("CORRECT-HORSE", "correct-horse"));
    }

    [Fact]
    public void A_prefix_of_the_key_does_not_match()
    {
        // Hashing both sides means a shorter presented key cannot short-circuit the comparison.
        Assert.False(AdminKey.Matches("correct", "correct-horse"));
    }

    [Fact]
    public void A_key_with_a_trailing_space_does_not_match()
    {
        Assert.False(AdminKey.Matches("correct-horse ", "correct-horse"));
    }
}

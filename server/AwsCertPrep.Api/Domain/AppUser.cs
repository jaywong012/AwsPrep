using Microsoft.AspNetCore.Identity;

namespace AwsCertPrep.Api.Domain;

/// <summary>
/// A learner's account.
///
/// Deliberately empty: <see cref="IdentityUser"/> already carries the email, the password hash,
/// the security stamp and the lockout counters, and nothing this app shows about a learner is a
/// property of the person rather than of their answers. What they have studied lives in
/// <see cref="ExamSession"/> and <see cref="LessonProgress"/>, keyed by this user's id.
/// </summary>
public class AppUser : IdentityUser
{
}

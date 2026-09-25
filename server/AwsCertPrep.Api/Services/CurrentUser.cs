using System.Security.Claims;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Reads the learner identity from the authenticated principal.
///
/// This replaced a self-asserted <c>X-User-Key</c> header. Everything a learner owns - exam
/// sessions, history, readiness, lesson progress - is scoped to the value this returns, so it
/// throws rather than falling back: the old accessor treated an unusable key as a shared
/// anonymous learner, and silently merging two people's answers is worse than a 401.
/// </summary>
public static class CurrentUser
{
    /// <summary>
    /// The signed-in user's id, as stored in <c>ExamSession.UserKey</c> and
    /// <c>LessonProgress.UserKey</c>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// The principal carries no usable subject. With <c>[Authorize]</c> on the action this can
    /// only mean the pipeline is misconfigured - authentication running after authorization, say -
    /// and a loud 401 is how that gets noticed rather than written to a blank user key.
    /// </exception>
    public static string GetUserId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new UnauthorizedAccessException("The request carries no authenticated user.");
        }

        return id;
    }
}

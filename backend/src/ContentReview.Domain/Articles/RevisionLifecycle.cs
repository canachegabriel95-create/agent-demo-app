using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.Domain.Articles;

/// <summary>Something that can be done to a revision of a published article.</summary>
public enum RevisionAction
{
    Edit,
    Submit,
    Withdraw,
    Approve,
    Reject,
}

/// <summary>
/// The revision lifecycle, defined once next to <see cref="ArticleLifecycle"/>. It has the same shape (edit, submit,
/// withdraw, approve, reject) but the end state is <see cref="RevisionStatus.Approved"/>: approving replaces the content
/// of the published article instead of publishing it. An approved revision is history and allows nothing.
/// </summary>
public static class RevisionLifecycle
{
    private sealed record Transition(RevisionStatus From, RevisionAction Action, RevisionStatus To, ActorKind Actor);

    private static readonly Transition[] Transitions =
    [
        new(RevisionStatus.Draft, RevisionAction.Edit, RevisionStatus.Draft, ActorKind.Owner),
        new(RevisionStatus.Draft, RevisionAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
        new(RevisionStatus.PendingReview, RevisionAction.Withdraw, RevisionStatus.Draft, ActorKind.Owner),
        new(RevisionStatus.PendingReview, RevisionAction.Approve, RevisionStatus.Approved, ActorKind.Administrator),
        new(RevisionStatus.PendingReview, RevisionAction.Reject, RevisionStatus.Rejected, ActorKind.Administrator),
        new(RevisionStatus.Rejected, RevisionAction.Edit, RevisionStatus.Rejected, ActorKind.Owner),
        new(RevisionStatus.Rejected, RevisionAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
    ];

    /// <summary>Whether a revision is still being worked on (anything but Approved).</summary>
    public static bool IsOpen(RevisionStatus status) => status != RevisionStatus.Approved;

    public static bool CanPerform(RevisionStatus status, RevisionAction action, ActorKind actor) =>
        Transitions.Any(t => t.From == status && t.Action == action && t.Actor == actor);

    /// <exception cref="InvalidTransitionException">No actor can do <paramref name="action"/> in <paramref name="status"/>.</exception>
    public static RevisionStatus Next(RevisionStatus status, RevisionAction action) =>
        (Transitions.FirstOrDefault(t => t.From == status && t.Action == action)
         ?? throw new InvalidTransitionException(Describe(status, action))).To;

    /// <summary>The actions <paramref name="actor"/> may take now, in the order of <see cref="RevisionAction"/>.</summary>
    public static IReadOnlyList<RevisionAction> AllowedActions(RevisionStatus status, ActorKind actor) =>
        Transitions
            .Where(t => t.From == status && t.Actor == actor)
            .Select(t => t.Action)
            .Order()
            .ToList();

    /// <exception cref="InvalidTransitionException">The action is not allowed for this actor in this status (409).</exception>
    public static void EnsureCanPerform(RevisionStatus status, RevisionAction action, ActorKind actor)
    {
        if (!CanPerform(status, action, actor))
        {
            throw new InvalidTransitionException(Describe(status, action));
        }
    }

    private static string Describe(RevisionStatus status, RevisionAction action)
    {
        var what = action switch
        {
            RevisionAction.Edit => "modified",
            RevisionAction.Submit => "submitted",
            RevisionAction.Withdraw => "withdrawn",
            RevisionAction.Approve => "approved",
            RevisionAction.Reject => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown revision action."),
        };

        return $"Revision cannot be {what} in status {status}.";
    }
}

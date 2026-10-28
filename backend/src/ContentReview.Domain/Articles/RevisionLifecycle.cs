using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.Domain.Articles;

/// <summary>
/// The lifecycle of a revision of a published article, defined once like <see cref="ArticleLifecycle"/>. It mirrors the
/// article lifecycle (Draft, PendingReview, Rejected) and ends in Approved, which is final.
/// </summary>
public static class RevisionLifecycle
{
    /// <summary><c>To</c> is <c>null</c> when the action removes the revision (Delete).</summary>
    private sealed record Transition(RevisionStatus From, ArticleAction Action, RevisionStatus? To, ActorKind Actor);

    private static readonly Transition[] Transitions =
    [
        new(RevisionStatus.Draft, ArticleAction.Edit, RevisionStatus.Draft, ActorKind.Owner),
        new(RevisionStatus.Draft, ArticleAction.Delete, null, ActorKind.Owner),
        new(RevisionStatus.Draft, ArticleAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
        new(RevisionStatus.PendingReview, ArticleAction.Withdraw, RevisionStatus.Draft, ActorKind.Owner),
        new(RevisionStatus.PendingReview, ArticleAction.Approve, RevisionStatus.Approved, ActorKind.Administrator),
        new(RevisionStatus.PendingReview, ArticleAction.Reject, RevisionStatus.Rejected, ActorKind.Administrator),
        new(RevisionStatus.Rejected, ArticleAction.Edit, RevisionStatus.Rejected, ActorKind.Owner),
        new(RevisionStatus.Rejected, ArticleAction.Delete, null, ActorKind.Owner),
        new(RevisionStatus.Rejected, ArticleAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
    ];

    public static bool CanPerform(RevisionStatus status, ArticleAction action, ActorKind actor) =>
        Transitions.Any(t => t.From == status && t.Action == action && t.Actor == actor);

    /// <summary>The status after the action; <c>null</c> for Delete.</summary>
    /// <exception cref="InvalidTransitionException">No actor can do the action in this status.</exception>
    public static RevisionStatus? Next(RevisionStatus status, ArticleAction action)
    {
        var transition = Transitions.FirstOrDefault(t => t.From == status && t.Action == action)
                         ?? throw new InvalidTransitionException(Describe(status, action));
        return transition.To;
    }

    public static IReadOnlyList<ArticleAction> AllowedActions(RevisionStatus status, ActorKind actor) =>
        Transitions
            .Where(t => t.From == status && t.Actor == actor)
            .Select(t => t.Action)
            .Order()
            .ToList();

    /// <exception cref="InvalidTransitionException">The action is not allowed for this actor in this status (409).</exception>
    public static void EnsureCanPerform(RevisionStatus status, ArticleAction action, ActorKind actor)
    {
        if (!CanPerform(status, action, actor))
        {
            throw new InvalidTransitionException(Describe(status, action));
        }
    }

    private static string Describe(RevisionStatus status, ArticleAction action)
    {
        var what = action switch
        {
            ArticleAction.Edit or ArticleAction.Delete => "modified",
            ArticleAction.Submit => "submitted",
            ArticleAction.Withdraw => "withdrawn",
            ArticleAction.Approve => "approved",
            ArticleAction.Reject => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown revision action."),
        };

        return $"Revision cannot be {what} in status {status}.";
    }
}

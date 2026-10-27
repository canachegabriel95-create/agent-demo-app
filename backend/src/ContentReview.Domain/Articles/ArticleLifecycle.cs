using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.Domain.Articles;

/// <summary>Something that can be done to an article.</summary>
public enum ArticleAction
{
    Edit,
    Delete,
    Submit,
    Withdraw,
    Approve,
    Reject,
}

/// <summary>Who is acting: the author who owns the article, or an administrator.</summary>
public enum ActorKind
{
    Owner,
    Administrator,
}

/// <summary>
/// The article lifecycle, defined once. The API, the portals and the tests all ask this class what is allowed, so
/// there is a single table of transitions (see <c>docs/article-lifecycle.md</c>). A <see cref="ArticleStatus.Published"/>
/// article allows nothing.
/// </summary>
public static class ArticleLifecycle
{
    /// <summary><c>To</c> is <c>null</c> when the action removes the article (Delete).</summary>
    private sealed record Transition(ArticleStatus From, ArticleAction Action, ArticleStatus? To, ActorKind Actor);

    private static readonly Transition[] Transitions =
    [
        new(ArticleStatus.Draft, ArticleAction.Edit, ArticleStatus.Draft, ActorKind.Owner),
        new(ArticleStatus.Draft, ArticleAction.Delete, null, ActorKind.Owner),
        new(ArticleStatus.Draft, ArticleAction.Submit, ArticleStatus.PendingReview, ActorKind.Owner),
        new(ArticleStatus.PendingReview, ArticleAction.Withdraw, ArticleStatus.Draft, ActorKind.Owner),
        new(ArticleStatus.PendingReview, ArticleAction.Approve, ArticleStatus.Published, ActorKind.Administrator),
        new(ArticleStatus.PendingReview, ArticleAction.Reject, ArticleStatus.Rejected, ActorKind.Administrator),
        new(ArticleStatus.Rejected, ArticleAction.Edit, ArticleStatus.Rejected, ActorKind.Owner),
        new(ArticleStatus.Rejected, ArticleAction.Submit, ArticleStatus.PendingReview, ActorKind.Owner),
    ];

    /// <summary>Whether <paramref name="actor"/> may do <paramref name="action"/> to an article in <paramref name="status"/>.</summary>
    public static bool CanPerform(ArticleStatus status, ArticleAction action, ActorKind actor) =>
        Transitions.Any(t => t.From == status && t.Action == action && t.Actor == actor);

    /// <summary>
    /// The status after <paramref name="action"/>: the same status for Edit, the new one for a transition, and
    /// <c>null</c> for Delete (the article no longer exists). Who is acting is not part of this question; use
    /// <see cref="CanPerform"/> or <see cref="EnsureCanPerform"/> for that.
    /// </summary>
    /// <exception cref="InvalidTransitionException">No actor can do <paramref name="action"/> in <paramref name="status"/>.</exception>
    public static ArticleStatus? Next(ArticleStatus status, ArticleAction action)
    {
        var transition = Transitions.FirstOrDefault(t => t.From == status && t.Action == action)
                         ?? throw new InvalidTransitionException(Describe(status, action));
        return transition.To;
    }

    /// <summary>The actions <paramref name="actor"/> may take now, in the order of <see cref="ArticleAction"/>.</summary>
    public static IReadOnlyList<ArticleAction> AllowedActions(ArticleStatus status, ActorKind actor) =>
        Transitions
            .Where(t => t.From == status && t.Actor == actor)
            .Select(t => t.Action)
            .Order()
            .ToList();

    /// <summary>Throws the conflict the API answers with (409) when the action is not allowed.</summary>
    /// <exception cref="InvalidTransitionException">The action is not allowed for this actor in this status.</exception>
    public static void EnsureCanPerform(ArticleStatus status, ArticleAction action, ActorKind actor)
    {
        if (!CanPerform(status, action, actor))
        {
            throw new InvalidTransitionException(Describe(status, action));
        }
    }

    /// <summary>The sentence shown to the user, e.g. "Article cannot be modified in status PendingReview.".</summary>
    private static string Describe(ArticleStatus status, ArticleAction action)
    {
        var what = action switch
        {
            ArticleAction.Edit or ArticleAction.Delete => "modified",
            ArticleAction.Submit => "submitted",
            ArticleAction.Withdraw => "withdrawn",
            ArticleAction.Approve => "approved",
            ArticleAction.Reject => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown article action."),
        };

        return $"Article cannot be {what} in status {status}.";
    }
}

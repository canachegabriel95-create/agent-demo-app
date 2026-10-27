using ContentReview.Domain.Articles;
using ContentReview.Domain.Entities;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class ArticleLifecycleTests
{
    private static readonly ArticleStatus[] Statuses = Enum.GetValues<ArticleStatus>();
    private static readonly ArticleAction[] Actions = Enum.GetValues<ArticleAction>();
    private static readonly ActorKind[] Actors = Enum.GetValues<ActorKind>();

    /// <summary>
    /// The documented transition table, written out again here on purpose: the production table and this one must agree
    /// row for row, so a change to either is noticed. <c>To == null</c> means the article is removed.
    /// </summary>
    private static readonly (ArticleStatus From, ArticleAction Action, ArticleStatus? To, ActorKind Actor)[] Documented =
    [
        (ArticleStatus.Draft, ArticleAction.Edit, ArticleStatus.Draft, ActorKind.Owner),
        (ArticleStatus.Draft, ArticleAction.Delete, null, ActorKind.Owner),
        (ArticleStatus.Draft, ArticleAction.Submit, ArticleStatus.PendingReview, ActorKind.Owner),
        (ArticleStatus.PendingReview, ArticleAction.Withdraw, ArticleStatus.Draft, ActorKind.Owner),
        (ArticleStatus.PendingReview, ArticleAction.Approve, ArticleStatus.Published, ActorKind.Administrator),
        (ArticleStatus.PendingReview, ArticleAction.Reject, ArticleStatus.Rejected, ActorKind.Administrator),
        (ArticleStatus.Rejected, ArticleAction.Edit, ArticleStatus.Rejected, ActorKind.Owner),
        (ArticleStatus.Rejected, ArticleAction.Submit, ArticleStatus.PendingReview, ActorKind.Owner),
    ];

    public static TheoryData<ArticleStatus, ArticleAction, ActorKind> EveryCombination()
    {
        var data = new TheoryData<ArticleStatus, ArticleAction, ActorKind>();
        foreach (var status in Statuses)
        {
            foreach (var action in Actions)
            {
                foreach (var actor in Actors)
                {
                    data.Add(status, action, actor);
                }
            }
        }

        return data;
    }

    private static bool IsDocumented(ArticleStatus status, ArticleAction action, ActorKind actor) =>
        Documented.Any(d => d.From == status && d.Action == action && d.Actor == actor);

    [Fact]
    public void The_matrix_covers_every_status_action_and_actor()
    {
        Assert.Equal(4 * 6 * 2, Statuses.Length * Actions.Length * Actors.Length);
        Assert.Equal(48, EveryCombination().Count);
        Assert.Equal(8, Documented.Length);
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void CanPerform_is_true_exactly_for_the_documented_transitions(ArticleStatus status, ArticleAction action, ActorKind actor)
    {
        Assert.Equal(IsDocumented(status, action, actor), ArticleLifecycle.CanPerform(status, action, actor));
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EnsureCanPerform_accepts_the_documented_transitions_and_refuses_all_others(
        ArticleStatus status, ArticleAction action, ActorKind actor)
    {
        if (IsDocumented(status, action, actor))
        {
            ArticleLifecycle.EnsureCanPerform(status, action, actor);
        }
        else
        {
            Assert.Throws<InvalidTransitionException>(() => ArticleLifecycle.EnsureCanPerform(status, action, actor));
        }
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void Next_gives_the_documented_status_for_any_actor_that_can_do_the_action(
        ArticleStatus status, ArticleAction action, ActorKind actor)
    {
        // Next does not look at the actor; it answers for the (status, action) pair.
        var row = Documented.Where(d => d.From == status && d.Action == action).Cast<(ArticleStatus, ArticleAction, ArticleStatus?, ActorKind)?>().FirstOrDefault();

        if (row is { } found)
        {
            Assert.Equal(found.Item3, ArticleLifecycle.Next(status, action));
        }
        else
        {
            Assert.Throws<InvalidTransitionException>(() => ArticleLifecycle.Next(status, action));
        }

        _ = actor;
    }

    [Theory]
    [InlineData(ArticleStatus.Draft, ActorKind.Owner, new[] { ArticleAction.Edit, ArticleAction.Delete, ArticleAction.Submit })]
    [InlineData(ArticleStatus.PendingReview, ActorKind.Owner, new[] { ArticleAction.Withdraw })]
    [InlineData(ArticleStatus.Rejected, ActorKind.Owner, new[] { ArticleAction.Edit, ArticleAction.Submit })]
    [InlineData(ArticleStatus.PendingReview, ActorKind.Administrator, new[] { ArticleAction.Approve, ArticleAction.Reject })]
    [InlineData(ArticleStatus.Draft, ActorKind.Administrator, new ArticleAction[0])]
    [InlineData(ArticleStatus.Rejected, ActorKind.Administrator, new ArticleAction[0])]
    [InlineData(ArticleStatus.Published, ActorKind.Owner, new ArticleAction[0])]
    [InlineData(ArticleStatus.Published, ActorKind.Administrator, new ArticleAction[0])]
    public void AllowedActions_lists_what_the_actor_may_do_in_order(ArticleStatus status, ActorKind actor, ArticleAction[] expected)
    {
        Assert.Equal(expected, ArticleLifecycle.AllowedActions(status, actor));
    }

    [Fact]
    public void A_published_article_allows_nothing_to_anyone()
    {
        foreach (var action in Actions)
        {
            foreach (var actor in Actors)
            {
                Assert.False(ArticleLifecycle.CanPerform(ArticleStatus.Published, action, actor));
            }
        }

        Assert.Empty(ArticleLifecycle.AllowedActions(ArticleStatus.Published, ActorKind.Owner));
        Assert.Empty(ArticleLifecycle.AllowedActions(ArticleStatus.Published, ActorKind.Administrator));
    }

    [Fact]
    public void AllowedActions_agrees_with_CanPerform_everywhere()
    {
        foreach (var status in Statuses)
        {
            foreach (var actor in Actors)
            {
                var allowed = ArticleLifecycle.AllowedActions(status, actor);
                Assert.Equal(Actions.Where(a => ArticleLifecycle.CanPerform(status, a, actor)), allowed);
            }
        }
    }

    [Theory]
    [InlineData(ArticleStatus.PendingReview, ArticleAction.Edit, "Article cannot be modified in status PendingReview.")]
    [InlineData(ArticleStatus.Published, ArticleAction.Edit, "Article cannot be modified in status Published.")]
    [InlineData(ArticleStatus.PendingReview, ArticleAction.Delete, "Article cannot be modified in status PendingReview.")]
    [InlineData(ArticleStatus.Rejected, ArticleAction.Delete, "Article cannot be modified in status Rejected.")]
    [InlineData(ArticleStatus.Published, ArticleAction.Submit, "Article cannot be submitted in status Published.")]
    [InlineData(ArticleStatus.Draft, ArticleAction.Withdraw, "Article cannot be withdrawn in status Draft.")]
    [InlineData(ArticleStatus.Draft, ArticleAction.Approve, "Article cannot be approved in status Draft.")]
    [InlineData(ArticleStatus.Rejected, ArticleAction.Reject, "Article cannot be rejected in status Rejected.")]
    public void A_refused_action_explains_itself_in_one_sentence_with_the_status(
        ArticleStatus status, ArticleAction action, string message)
    {
        var owner = Assert.Throws<InvalidTransitionException>(() => ArticleLifecycle.EnsureCanPerform(status, action, ActorKind.Owner));
        Assert.Equal(message, owner.Message);
        Assert.IsAssignableFrom<ConflictException>(owner); // the API answers 409
    }

    [Fact]
    public void An_actor_who_may_not_do_a_valid_action_is_refused_with_the_same_kind_of_conflict()
    {
        Assert.Throws<InvalidTransitionException>(
            () => ArticleLifecycle.EnsureCanPerform(ArticleStatus.PendingReview, ArticleAction.Approve, ActorKind.Owner));
        Assert.Throws<InvalidTransitionException>(
            () => ArticleLifecycle.EnsureCanPerform(ArticleStatus.Draft, ArticleAction.Submit, ActorKind.Administrator));
    }

    [Fact]
    public void An_unknown_action_is_a_programming_error_not_a_conflict()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArticleLifecycle.Next(ArticleStatus.Draft, (ArticleAction)99));
    }
}

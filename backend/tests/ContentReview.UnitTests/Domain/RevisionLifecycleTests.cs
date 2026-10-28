using ContentReview.Domain.Articles;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class RevisionLifecycleTests
{
    private static readonly RevisionStatus[] Statuses = Enum.GetValues<RevisionStatus>();
    private static readonly ArticleAction[] Actions =
        [ArticleAction.Edit, ArticleAction.Delete, ArticleAction.Submit, ArticleAction.Withdraw, ArticleAction.Approve, ArticleAction.Reject];
    private static readonly ActorKind[] Actors = Enum.GetValues<ActorKind>();

    /// <summary>The documented revision transitions, written out again so a change to either table is noticed.</summary>
    private static readonly (RevisionStatus From, ArticleAction Action, RevisionStatus? To, ActorKind Actor)[] Documented =
    [
        (RevisionStatus.Draft, ArticleAction.Edit, RevisionStatus.Draft, ActorKind.Owner),
        (RevisionStatus.Draft, ArticleAction.Delete, null, ActorKind.Owner),
        (RevisionStatus.Draft, ArticleAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
        (RevisionStatus.PendingReview, ArticleAction.Withdraw, RevisionStatus.Draft, ActorKind.Owner),
        (RevisionStatus.PendingReview, ArticleAction.Approve, RevisionStatus.Approved, ActorKind.Administrator),
        (RevisionStatus.PendingReview, ArticleAction.Reject, RevisionStatus.Rejected, ActorKind.Administrator),
        (RevisionStatus.Rejected, ArticleAction.Edit, RevisionStatus.Rejected, ActorKind.Owner),
        (RevisionStatus.Rejected, ArticleAction.Delete, null, ActorKind.Owner),
        (RevisionStatus.Rejected, ArticleAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
    ];

    public static TheoryData<RevisionStatus, ArticleAction, ActorKind> EveryCombination()
    {
        var data = new TheoryData<RevisionStatus, ArticleAction, ActorKind>();
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

    private static bool IsDocumented(RevisionStatus status, ArticleAction action, ActorKind actor) =>
        Documented.Any(d => d.From == status && d.Action == action && d.Actor == actor);

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void CanPerform_is_true_exactly_for_the_documented_transitions(RevisionStatus status, ArticleAction action, ActorKind actor)
    {
        Assert.Equal(IsDocumented(status, action, actor), RevisionLifecycle.CanPerform(status, action, actor));
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EnsureCanPerform_refuses_everything_else_with_a_conflict(RevisionStatus status, ArticleAction action, ActorKind actor)
    {
        if (IsDocumented(status, action, actor))
        {
            RevisionLifecycle.EnsureCanPerform(status, action, actor);
        }
        else
        {
            Assert.Throws<InvalidTransitionException>(() => RevisionLifecycle.EnsureCanPerform(status, action, actor));
        }
    }

    [Fact]
    public void Next_gives_the_documented_status()
    {
        foreach (var row in Documented)
        {
            Assert.Equal(row.To, RevisionLifecycle.Next(row.From, row.Action));
        }

        Assert.Throws<InvalidTransitionException>(() => RevisionLifecycle.Next(RevisionStatus.Approved, ArticleAction.Edit));
    }

    [Fact]
    public void An_approved_revision_is_final()
    {
        foreach (var action in Actions)
        {
            foreach (var actor in Actors)
            {
                Assert.False(RevisionLifecycle.CanPerform(RevisionStatus.Approved, action, actor));
            }
        }
    }

    [Theory]
    [InlineData(RevisionStatus.Draft, ArticleAction.Withdraw, "Revision cannot be withdrawn in status Draft.")]
    [InlineData(RevisionStatus.PendingReview, ArticleAction.Edit, "Revision cannot be modified in status PendingReview.")]
    [InlineData(RevisionStatus.Approved, ArticleAction.Submit, "Revision cannot be submitted in status Approved.")]
    [InlineData(RevisionStatus.Rejected, ArticleAction.Approve, "Revision cannot be approved in status Rejected.")]
    public void A_refused_action_explains_itself_with_the_status(RevisionStatus status, ArticleAction action, string message)
    {
        var ex = Assert.Throws<InvalidTransitionException>(() => RevisionLifecycle.EnsureCanPerform(status, action, ActorKind.Owner));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void AllowedActions_agree_with_CanPerform()
    {
        foreach (var status in Statuses)
        {
            foreach (var actor in Actors)
            {
                Assert.Equal(
                    Actions.Where(a => RevisionLifecycle.CanPerform(status, a, actor)),
                    RevisionLifecycle.AllowedActions(status, actor));
            }
        }
    }
}

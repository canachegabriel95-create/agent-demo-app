using ContentReview.Domain.Articles;
using ContentReview.Domain.Enums;
using ContentReview.Domain.Exceptions;

namespace ContentReview.UnitTests.Domain;

public class RevisionLifecycleTests
{
    private static readonly RevisionStatus[] Statuses = Enum.GetValues<RevisionStatus>();
    private static readonly RevisionAction[] Actions = Enum.GetValues<RevisionAction>();
    private static readonly ActorKind[] Actors = Enum.GetValues<ActorKind>();

    /// <summary>The documented transition table, written out again on purpose so a change to either is noticed.</summary>
    private static readonly (RevisionStatus From, RevisionAction Action, RevisionStatus To, ActorKind Actor)[] Documented =
    [
        (RevisionStatus.Draft, RevisionAction.Edit, RevisionStatus.Draft, ActorKind.Owner),
        (RevisionStatus.Draft, RevisionAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
        (RevisionStatus.PendingReview, RevisionAction.Withdraw, RevisionStatus.Draft, ActorKind.Owner),
        (RevisionStatus.PendingReview, RevisionAction.Approve, RevisionStatus.Approved, ActorKind.Administrator),
        (RevisionStatus.PendingReview, RevisionAction.Reject, RevisionStatus.Rejected, ActorKind.Administrator),
        (RevisionStatus.Rejected, RevisionAction.Edit, RevisionStatus.Rejected, ActorKind.Owner),
        (RevisionStatus.Rejected, RevisionAction.Submit, RevisionStatus.PendingReview, ActorKind.Owner),
    ];

    public static TheoryData<RevisionStatus, RevisionAction, ActorKind> EveryCombination()
    {
        var data = new TheoryData<RevisionStatus, RevisionAction, ActorKind>();
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

    private static bool IsDocumented(RevisionStatus status, RevisionAction action, ActorKind actor) =>
        Documented.Any(d => d.From == status && d.Action == action && d.Actor == actor);

    [Fact]
    public void The_matrix_covers_every_status_action_and_actor()
    {
        Assert.Equal(4 * 5 * 2, EveryCombination().Count);
        Assert.Equal(7, Documented.Length);
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void CanPerform_is_true_exactly_for_the_documented_transitions(
        RevisionStatus status, RevisionAction action, ActorKind actor)
    {
        Assert.Equal(IsDocumented(status, action, actor), RevisionLifecycle.CanPerform(status, action, actor));
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void EnsureCanPerform_throws_a_conflict_for_everything_else(
        RevisionStatus status, RevisionAction action, ActorKind actor)
    {
        if (IsDocumented(status, action, actor))
        {
            RevisionLifecycle.EnsureCanPerform(status, action, actor);
            return;
        }

        var ex = Assert.Throws<InvalidTransitionException>(() => RevisionLifecycle.EnsureCanPerform(status, action, actor));
        Assert.IsAssignableFrom<ConflictException>(ex);
    }

    [Fact]
    public void Next_gives_the_documented_target_status()
    {
        foreach (var (from, action, to, _) in Documented)
        {
            Assert.Equal(to, RevisionLifecycle.Next(from, action));
        }
    }

    [Fact]
    public void Next_throws_when_no_actor_can_do_the_action()
    {
        var ex = Assert.Throws<InvalidTransitionException>(() => RevisionLifecycle.Next(RevisionStatus.Approved, RevisionAction.Edit));
        Assert.Equal("Revision cannot be modified in status Approved.", ex.Message);
    }

    [Theory]
    [InlineData(RevisionStatus.Draft, true)]
    [InlineData(RevisionStatus.PendingReview, true)]
    [InlineData(RevisionStatus.Rejected, true)]
    [InlineData(RevisionStatus.Approved, false)]
    public void Only_an_approved_revision_is_closed(RevisionStatus status, bool open)
    {
        Assert.Equal(open, RevisionLifecycle.IsOpen(status));
    }

    [Fact]
    public void An_approved_revision_allows_nothing()
    {
        foreach (var actor in Actors)
        {
            Assert.Empty(RevisionLifecycle.AllowedActions(RevisionStatus.Approved, actor));
        }
    }

    [Fact]
    public void AllowedActions_lists_what_each_actor_may_do()
    {
        Assert.Equal(
            [RevisionAction.Edit, RevisionAction.Submit],
            RevisionLifecycle.AllowedActions(RevisionStatus.Rejected, ActorKind.Owner));
        Assert.Equal(
            [RevisionAction.Approve, RevisionAction.Reject],
            RevisionLifecycle.AllowedActions(RevisionStatus.PendingReview, ActorKind.Administrator));
        Assert.Equal(
            [RevisionAction.Withdraw],
            RevisionLifecycle.AllowedActions(RevisionStatus.PendingReview, ActorKind.Owner));
    }
}

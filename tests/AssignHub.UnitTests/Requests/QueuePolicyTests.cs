using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;
using Microsoft.Extensions.Time.Testing;

namespace AssignHub.UnitTests.Requests;

public sealed class QueuePolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

    private static int[] OrderIds(DateTime now, params QueueEntry[] entries) =>
        QueuePolicy.Order(entries, now, Threshold).Select(e => e.Id).ToArray();

    [Fact]
    public void Urgent_BeforeOlderNormalThatHasNotAged()
    {
        var normal = new QueueEntry(1, Priority.Normal, Now.AddMinutes(-5));
        var urgent = new QueueEntry(2, Priority.Urgent, Now.AddMinutes(-1));

        Assert.Equal([2, 1], OrderIds(Now, normal, urgent));
    }

    [Fact]
    public void Aging_PromotesNormalAtExactlyThreshold()
    {
        var atThreshold = new QueueEntry(1, Priority.Normal, Now - Threshold);
        var justUnder = new QueueEntry(2, Priority.Normal, Now - Threshold + TimeSpan.FromTicks(1));

        Assert.True(QueuePolicy.IsEffectivelyUrgent(atThreshold, Now, Threshold));
        Assert.False(QueuePolicy.IsEffectivelyUrgent(justUnder, Now, Threshold));
    }

    [Fact]
    public void AgedNormal_BeatsYoungerUrgent()
    {
        var urgent = new QueueEntry(1, Priority.Urgent, Now.AddMinutes(-2));
        var agedNormal = new QueueEntry(2, Priority.Normal, Now.AddMinutes(-15));

        Assert.Equal([2, 1], OrderIds(Now, urgent, agedNormal));
    }

    [Fact]
    public void SameUrgencyAndCreatedAt_LowestIdWins()
    {
        var createdAt = Now.AddMinutes(-1);

        Assert.Equal(
            [1, 2, 3],
            OrderIds(
                Now,
                new QueueEntry(3, Priority.Urgent, createdAt),
                new QueueEntry(1, Priority.Urgent, createdAt),
                new QueueEntry(2, Priority.Urgent, createdAt)));
    }

    [Fact]
    public void SameUrgency_OldestFirst()
    {
        Assert.Equal(
            [2, 1],
            OrderIds(
                Now,
                new QueueEntry(1, Priority.Normal, Now.AddMinutes(-1)),
                new QueueEntry(2, Priority.Normal, Now.AddMinutes(-3))));
    }

    [Fact]
    public void AgingCutoff_IsNowMinusThreshold()
    {
        Assert.Equal(Now.AddMinutes(-10), QueuePolicy.AgingCutoff(Now, Threshold));
    }

    [Fact]
    public void WorkedExample_FromSpec()
    {
        // SPEC §7.3: T = 10 min, one Legal expert with capacity 1.
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 45, 0, TimeSpan.Zero));
        DateTime Clock() => time.GetUtcNow().UtcDateTime;

        var n1 = new QueueEntry(1, Priority.Normal, Clock());
        time.Advance(TimeSpan.FromMinutes(5));
        var u1 = new QueueEntry(2, Priority.Urgent, Clock());

        time.Advance(TimeSpan.FromMinutes(2)); // 09:52, N1 has waited 7 min
        Assert.Equal([u1.Id, n1.Id], OrderIds(Clock(), n1, u1));

        time.Advance(TimeSpan.FromMinutes(6)); // 09:58
        var u2 = new QueueEntry(3, Priority.Urgent, Clock());

        time.Advance(TimeSpan.FromMinutes(3)); // 10:01, N1 has waited 16 min
        Assert.Equal([n1.Id, u2.Id], OrderIds(Clock(), n1, u2));
    }
}

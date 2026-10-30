using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;

namespace AssignHub.UnitTests.Experts;

public sealed class ExpertSelectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private static ExpertSnapshot Expert(
        int id,
        int openCount,
        int capacity,
        DateTime? lastAssignedAt = null,
        bool isActive = true,
        params Subject[] specialties) =>
        new(id, capacity, openCount, isActive, lastAssignedAt,
            specialties.Length == 0 ? [Subject.Financial] : specialties);

    private static int[] OrderIds(params ExpertSnapshot[] experts) =>
        ExpertSelector.Order(experts, Subject.Financial).Select(e => e.Id).ToArray();

    [Fact]
    public void TaskExample_LowerRatioWins()
    {
        // SPEC §5.4: E1 2/4 (0.5) vs E2 3/10 (0.3) → E2.
        Assert.Equal([2, 1], OrderIds(Expert(1, 2, 4), Expert(2, 3, 10)));
    }

    [Fact]
    public void Ratio_TakesPrecedenceOverFreeSlots()
    {
        // 0/1 has fewer free slots than 1/10 but a lower ratio.
        Assert.Equal([1, 2], OrderIds(Expert(1, 0, 1), Expert(2, 1, 10)));
    }

    [Fact]
    public void EqualRatio_MoreFreeSlotsWins()
    {
        // SPEC §5.3: A 1/2 and B 2/4 both 0.5; B has 2 free slots, A has 1.
        Assert.Equal([2, 1], OrderIds(Expert(1, 1, 2), Expert(2, 2, 4)));
    }

    [Fact]
    public void EqualRatioAndFreeSlots_NeverAssignedWins()
    {
        Assert.Equal([2, 1], OrderIds(Expert(1, 0, 5, lastAssignedAt: T0), Expert(2, 0, 5)));
    }

    [Fact]
    public void EqualRatioAndFreeSlots_LeastRecentlyAssignedWins()
    {
        Assert.Equal(
            [2, 1],
            OrderIds(Expert(1, 1, 5, lastAssignedAt: T0.AddMinutes(5)), Expert(2, 1, 5, lastAssignedAt: T0)));
    }

    [Fact]
    public void FullTie_LowestIdWins()
    {
        Assert.Equal([1, 2, 3], OrderIds(Expert(3, 1, 5, T0), Expert(1, 1, 5, T0), Expert(2, 1, 5, T0)));
    }

    [Fact]
    public void StartupRoundRobin_LastAssignedAtAlternatesIdenticalExperts()
    {
        // SPEC §5.3: A 0/5 and B 0/5 → A first; once A is assigned, B wins the next tie.
        Assert.Equal([1, 2], OrderIds(Expert(1, 0, 5), Expert(2, 0, 5)));
        Assert.Equal([2, 1], OrderIds(Expert(1, 1, 5, lastAssignedAt: T0), Expert(2, 1, 5)));
    }

    [Fact]
    public void EquivalentFractions_AreAnExactRatioTie()
    {
        // 1/3 and 2/6 tie on ratio, so free slots decide: 2/6 has 4, 1/3 has 2.
        Assert.Equal([2, 1], OrderIds(Expert(1, 1, 3), Expert(2, 2, 6)));
    }

    [Fact]
    public void NearlyEqualRatios_AreComparedExactly()
    {
        // 333/1000 < 1/3, even though 1/3 has fewer free slots it must lose on ratio.
        Assert.Equal([1, 2], OrderIds(Expert(1, 333, 1000), Expert(2, 1, 3)));
    }

    [Fact]
    public void IneligibleExperts_AreExcluded()
    {
        var ordered = OrderIds(
            Expert(1, 0, 5, isActive: false),
            Expert(2, 5, 5),
            Expert(3, 0, 5, specialties: Subject.Legal),
            Expert(4, 4, 5, specialties: [Subject.Legal, Subject.Financial]));

        Assert.Equal([4], ordered);
    }

    [Fact]
    public void NoEligibleExpert_ReturnsEmpty()
    {
        Assert.Empty(OrderIds(Expert(1, 1, 1), Expert(2, 0, 3, isActive: false)));
    }
}

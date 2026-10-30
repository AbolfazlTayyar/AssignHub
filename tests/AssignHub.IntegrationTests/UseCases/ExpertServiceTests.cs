using AssignHub.Application.Common;
using AssignHub.Application.Experts;
using AssignHub.Domain;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.UseCases;

public sealed class ExpertServiceTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateExpertDto Dto(int capacity, Subject[] specialties, bool isActive = true) =>
        new() { Name = "Sara", Capacity = capacity, Specialties = specialties, IsActive = isActive };

    [Fact]
    public async Task Create_Active_DrainsQueueAndReturnsFreshLoad()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var waiting = await h.AddRequestAsync(Subject.Financial, Priority.Normal, Ct);

        var result = await h.ExpertsAsync(s => s.CreateAsync(Dto(4, [Subject.Financial, Subject.Legal]), Ct));

        Assert.Equal(ResultStatus.Created, result.Status);
        var expert = result.Value!;
        Assert.Equal(1, expert.OpenCount);
        Assert.Equal(0.25, expert.LoadRatio);
        Assert.Equal([Subject.Financial, Subject.Legal], expert.Specialties);
        Assert.Equal(h.Now, expert.LastAssignedAt);
        Assert.Equal(expert.Id, (await h.GetRequestAsync(waiting, Ct))!.ExpertId);
    }

    [Fact]
    public async Task Create_Invalid_ThrowsDomainException()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            h.ExpertsAsync(s => s.CreateAsync(Dto(0, [Subject.Legal]), Ct)));
        Assert.Equal("Capacity must be between 1 and 1000.", ex.Message);
    }

    [Fact]
    public async Task Deactivate_KeepsExistingAndStopsNew_ThenActivateDrains()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var expert = (await h.ExpertsAsync(s => s.CreateAsync(Dto(5, [Subject.Legal]), Ct))).Value!;
        var existing = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        await h.TryAssignAsync(existing, Ct);

        var deactivated = await h.ExpertsAsync(s => s.DeactivateAsync(expert.Id, Ct));
        var waiting = await h.AddRequestAsync(Subject.Legal, Priority.Urgent, Ct);
        await h.TryAssignAsync(waiting, Ct);

        Assert.False(deactivated.Value!.IsActive);
        Assert.Equal(expert.Id, (await h.GetRequestAsync(existing, Ct))!.ExpertId);
        Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(waiting, Ct))!.Status);
        Assert.Equal(ResultStatus.Ok, (await h.RequestsAsync(s => s.CloseAsync(existing, Ct))).Status);

        var activated = await h.ExpertsAsync(s => s.ActivateAsync(expert.Id, Ct));

        Assert.True(activated.Value!.IsActive);
        Assert.Equal(1, activated.Value.OpenCount);
        Assert.Equal(expert.Id, (await h.GetRequestAsync(waiting, Ct))!.ExpertId);
        await h.AssertCountersConsistentAsync(Ct);
    }

    [Fact]
    public async Task ActivateAndDeactivate_AreIdempotent_AndReportMissing()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var expert = (await h.ExpertsAsync(s => s.CreateAsync(Dto(1, [Subject.Legal], isActive: false), Ct))).Value!;

        Assert.True((await h.ExpertsAsync(s => s.ActivateAsync(expert.Id, Ct))).Value!.IsActive);
        Assert.True((await h.ExpertsAsync(s => s.ActivateAsync(expert.Id, Ct))).Value!.IsActive);
        Assert.False((await h.ExpertsAsync(s => s.DeactivateAsync(expert.Id, Ct))).Value!.IsActive);
        Assert.False((await h.ExpertsAsync(s => s.DeactivateAsync(expert.Id, Ct))).Value!.IsActive);
        Assert.Equal(ResultStatus.NotFound, (await h.ExpertsAsync(s => s.ActivateAsync(999, Ct))).Status);
        Assert.Equal(ResultStatus.NotFound, (await h.ExpertsAsync(s => s.DeactivateAsync(999, Ct))).Status);
        Assert.Equal(ResultStatus.NotFound, (await h.ExpertsAsync(s => s.GetAsync(999, Ct))).Status);
        Assert.Single(await h.ExpertsAsync(s => s.ListAsync(Ct)));
    }
}

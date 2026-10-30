using AssignHub.Application.Common;
using AssignHub.Application.Experts;
using AssignHub.Application.Requests;
using AssignHub.Domain;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.UseCases;

public sealed class RequestServiceTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateRequestDto Dto(
        string key,
        Subject subject = Subject.Legal,
        Priority priority = Priority.Normal,
        string title = "Title") =>
        new() { RequestKey = key, Title = title, Description = "Body", Subject = subject, Priority = priority };

    [Fact]
    public async Task Create_WithFreeExpert_ReturnsCreatedAndAssigned()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var expertId = await h.AddExpertAsync(2, [Subject.Legal], isActive: true, Ct);

        var result = await h.RequestsAsync(s => s.CreateAsync(Dto("k1"), Ct));

        Assert.Equal(ResultStatus.Created, result.Status);
        Assert.Equal(RequestStatus.Assigned, result.Value!.Status);
        Assert.Equal(expertId, result.Value.ExpertId);
        Assert.Equal(h.Now, result.Value.AssignedAt);
    }

    [Fact]
    public async Task Create_WithoutExpert_ReturnsCreatedAndPending()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);

        var result = await h.RequestsAsync(s => s.CreateAsync(Dto("k1"), Ct));

        Assert.Equal(ResultStatus.Created, result.Status);
        Assert.Equal(RequestStatus.Pending, result.Value!.Status);
        Assert.Null(result.Value.ExpertId);
    }

    [Fact]
    public async Task Create_ReplaySamePayload_ReturnsOkWithCurrentState()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var first = await h.RequestsAsync(s => s.CreateAsync(Dto("k1"), Ct));
        await h.ExpertsAsync(s => s.CreateAsync(
            new CreateExpertDto { Name = "Sara", Capacity = 1, Specialties = [Subject.Legal] }, Ct)); // drains k1

        var replay = await h.RequestsAsync(s => s.CreateAsync(Dto("k1"), Ct));

        Assert.Equal(ResultStatus.Ok, replay.Status);
        Assert.Equal(first.Value!.Id, replay.Value!.Id);
        Assert.Equal(RequestStatus.Assigned, replay.Value.Status);
    }

    [Fact]
    public async Task Create_ReplayDifferentPayload_ReturnsConflict()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.RequestsAsync(s => s.CreateAsync(Dto("abc"), Ct));

        var result = await h.RequestsAsync(s => s.CreateAsync(Dto("abc", priority: Priority.Urgent), Ct));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal("RequestKey 'abc' was already used with a different payload.", result.Error);
    }

    [Fact]
    public async Task Create_InvalidInput_ThrowsDomainException()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);

        await Assert.ThrowsAsync<DomainException>(() =>
            h.RequestsAsync(s => s.CreateAsync(Dto("k1", title: new string('t', 201)), Ct)));
    }

    [Fact]
    public async Task Close_AssignsUrgentBeforeOlderNormal()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var expertId = await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var busy = await h.RequestsAsync(s => s.CreateAsync(Dto("busy"), Ct));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var normal = await h.RequestsAsync(s => s.CreateAsync(Dto("normal"), Ct));
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var urgent = await h.RequestsAsync(s => s.CreateAsync(Dto("urgent", priority: Priority.Urgent), Ct));

        var closed = await h.RequestsAsync(s => s.CloseAsync(busy.Value!.Id, Ct));

        Assert.Equal(ResultStatus.Ok, closed.Status);
        Assert.Equal(RequestStatus.Closed, closed.Value!.Status);
        Assert.Equal(expertId, closed.Value.ExpertId);
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(urgent.Value!.Id, Ct))!.Status);
        Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(normal.Value!.Id, Ct))!.Status);
        await h.AssertCountersConsistentAsync(Ct);
    }

    [Fact]
    public async Task Close_Outcomes()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var assigned = await h.RequestsAsync(s => s.CreateAsync(Dto("a"), Ct));
        var pending = await h.RequestsAsync(s => s.CreateAsync(Dto("p"), Ct));

        Assert.Equal(ResultStatus.Ok, (await h.RequestsAsync(s => s.CloseAsync(assigned.Value!.Id, Ct))).Status);
        var retry = await h.RequestsAsync(s => s.CloseAsync(assigned.Value!.Id, Ct));
        Assert.Equal(ResultStatus.Ok, retry.Status);
        Assert.Equal(RequestStatus.Closed, retry.Value!.Status);

        // The close above freed the slot, so "p" was assigned; a fresh pending request is needed for 409.
        await h.AddExpertAsync(1, [Subject.Technical], isActive: false, Ct);
        var stuck = await h.RequestsAsync(s => s.CreateAsync(Dto("t", Subject.Technical), Ct));
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(pending.Value!.Id, Ct))!.Status);
        Assert.Equal(ResultStatus.Conflict, (await h.RequestsAsync(s => s.CloseAsync(stuck.Value!.Id, Ct))).Status);
        Assert.Equal(ResultStatus.NotFound, (await h.RequestsAsync(s => s.CloseAsync(9999, Ct))).Status);
        Assert.Equal(ResultStatus.NotFound, (await h.RequestsAsync(s => s.GetAsync(9999, Ct))).Status);
    }

    [Fact]
    public async Task Aging_WorkedExampleFromSpec()
    {
        // SPEC §7.3: T = 10 min; one Legal expert with capacity 1, busy from the start.
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 9, 40, 0, TimeSpan.Zero));
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var busy = await h.RequestsAsync(s => s.CreateAsync(Dto("busy"), Ct));

        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 9, 45, 0, TimeSpan.Zero));
        var n1 = (await h.RequestsAsync(s => s.CreateAsync(Dto("N1"), Ct))).Value!;
        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 9, 50, 0, TimeSpan.Zero));
        var u1 = (await h.RequestsAsync(s => s.CreateAsync(Dto("U1", priority: Priority.Urgent), Ct))).Value!;

        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 9, 52, 0, TimeSpan.Zero));
        await h.RequestsAsync(s => s.CloseAsync(busy.Value!.Id, Ct));
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(u1.Id, Ct))!.Status);
        Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(n1.Id, Ct))!.Status);

        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 9, 58, 0, TimeSpan.Zero));
        var u2 = (await h.RequestsAsync(s => s.CreateAsync(Dto("U2", priority: Priority.Urgent), Ct))).Value!;

        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 10, 1, 0, TimeSpan.Zero));
        await h.RequestsAsync(s => s.CloseAsync(u1.Id, Ct));
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(n1.Id, Ct))!.Status);
        Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(u2.Id, Ct))!.Status);

        h.Time.SetUtcNow(new DateTimeOffset(2026, 10, 30, 10, 5, 0, TimeSpan.Zero));
        await h.RequestsAsync(s => s.CloseAsync(n1.Id, Ct));
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(u2.Id, Ct))!.Status);
    }
}

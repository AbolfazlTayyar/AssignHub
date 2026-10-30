using System.Text.RegularExpressions;
using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure.Persistence.Converters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssignHub.IntegrationTests.Infrastructure;

public sealed class PersistenceTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TryAdd_SameKeyConcurrently_InsertsOneRow()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var h = await ServiceHarness.CreateAsync(fixture, Ct);

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                h.TryAddAsync(Request.Create("same-key", "Title", "Body", Subject.Legal, Priority.Normal, h.Now), Ct)));

            Assert.Single(results, r => r.Added);
            Assert.Single(results.Select(r => r.Request.Id).Distinct());
            Assert.Equal(1, await h.DbAsync(db => db.Requests.CountAsync(Ct)));
        }
    }

    [Fact]
    public async Task TryAdd_DuplicateKey_ReturnsExistingRowWithCurrentState()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var original = Request.Create("key-1", "Title", null, Subject.Legal, Priority.Normal, h.Now);
        var first = await h.TryAddAsync(original, Ct);
        await h.TryAssignAsync(first.Request.Id, Ct);

        var second = await h.TryAddAsync(
            Request.Create("key-1", "Different", null, Subject.Financial, Priority.Urgent, h.Now), Ct);

        Assert.True(first.Added);
        Assert.False(second.Added);
        Assert.Equal(first.Request.Id, second.Request.Id);
        Assert.Equal(RequestStatus.Assigned, second.Request.Status);
        Assert.False(second.Request.HasSamePayload("Different", null, Subject.Financial, Priority.Urgent));
    }

    [Fact]
    public async Task Enums_AreStoredInPersian()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(2, [Subject.Technical], isActive: true, Ct);
        var requestId = await h.AddRequestAsync(Subject.Technical, Priority.Urgent, Ct);
        await h.TryAssignAsync(requestId, Ct);

        var row = await h.DbAsync(db => db.Database
            .SqlQuery<StoredRequest>($"SELECT [Subject], [Priority], [Status] FROM [Requests] WHERE [Id] = {requestId}")
            .SingleAsync(Ct));
        var specialty = await h.DbAsync(db => db.Database
            .SqlQuery<string>($"SELECT [Subject] AS [Value] FROM [ExpertSpecialties]")
            .SingleAsync(Ct));

        Assert.Equal(new StoredRequest("فنی", "فوری", "واگذار شده"), row);
        Assert.Equal("فنی", specialty);
    }

    [Fact]
    public async Task UnknownStoredEnumValue_ThrowsOnRead()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        await h.DbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE [Requests] SET [Priority] = {"خیلی فوری"} WHERE [Id] = {requestId}", Ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.GetRequestAsync(requestId, Ct));
    }

    [Fact]
    public async Task CheckConstraint_RejectsAssignedRequestWithoutExpert()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        var assigned = PersianEnumValues.ToDb(RequestStatus.Assigned);

        var ex = await Assert.ThrowsAsync<SqlException>(() => h.DbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE [Requests] SET [Status] = {assigned} WHERE [Id] = {requestId}", Ct)));
        Assert.Contains("CK_Requests_ExpertWhenAssigned", ex.Message);
    }

    [Fact]
    public async Task GeneratedSql_UsesUnicodeLiteralsAndCaseOrdering()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        h.Sql.Clear();

        await h.TryAssignAsync(requestId, Ct);
        await h.CloseAsync(requestId, Ct);
        var sql = string.Join("\n", h.Sql.Commands);

        h.Sql.Clear();
        await h.RunAsync(sp => sp.GetRequiredService<IAssignmentStore>()
            .GetPendingBatchAsync([Subject.Legal], h.Now, 10, Ct));
        var drain = Assert.Single(h.Sql.Commands);
        sql += "\n" + drain;
        var persian = PersianEnumValues.Subjects.Values
            .Concat(PersianEnumValues.Priorities.Values)
            .Concat(PersianEnumValues.Statuses.Values);
        foreach (var value in persian)
        {
            Assert.DoesNotMatch(new Regex($"(?<!N)'{Regex.Escape(value)}'"), sql);
        }

        Assert.Contains("ORDER BY CASE", drain, StringComparison.Ordinal);
        Assert.Contains("N'در انتظار'", drain, StringComparison.Ordinal);
        Assert.Contains("N'فوری'", drain, StringComparison.Ordinal);
    }

    private sealed record StoredRequest(string Subject, string Priority, string Status);
}

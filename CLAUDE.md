# CLAUDE.md

AssignHub is an ASP.NET Core Web API. It assigns requests to experts using three rules: match on specialty, then lowest load ratio, then a fixed tie-break. It stays correct under concurrency and across multiple instances. It runs a priority waiting queue with aging.

- **Requirements:** [DotNet-Developer-Task.md](DotNet-Developer-Task.md). This is the original task. Don't edit it.
- **Design:** [SPEC.md](SPEC.md) is the **source of truth**. Read the relevant section before you implement anything.

## SPEC.md is authoritative

- Implement what SPEC.md says: entities, constraints, algorithms, endpoints, status codes, config keys and test list.
- If you think the design should change, **stop and ask first**. That covers a different approach, an extra field, or an out-of-scope feature (see §1). Explain why, give a concrete example and give your recommendation.
- Once a change is approved, update SPEC.md **in the same change** as the code, so the two never drift apart.
- If SPEC.md is ambiguous or silent on something, ask. Don't assume. Small conventional details are the exception: decide those yourself and list them as assumptions in your summary.

## Stack

.NET 10 · ASP.NET Core Web API (controllers) · EF Core 10 + SQL Server 2022 · xUnit · `Microsoft.AspNetCore.Mvc.Testing` · Testcontainers (`Testcontainers.MsSql`) · Docker Compose (SQL Server + 2 API instances on ports 5001/5002).

## Layout and dependencies

```
src/AssignHub.Domain/          pure rules: ExpertSelector, QueuePolicy, entities, enums (no EF, no I/O)
src/AssignHub.Application/     use cases, DTOs, validation
src/AssignHub.Infrastructure/  DbContext, configurations, Persian enum converter, migrations, atomic-assignment SQL
src/AssignHub.Api/             controllers, DI, ApiResponse<T>, exception middleware, QueueSweeper, dev seeding
tests/AssignHub.UnitTests/         domain rules, no DB
tests/AssignHub.IntegrationTests/  real SQL Server via Testcontainers
```

`Api → Application → Domain`. `Infrastructure → Application, Domain`. Domain references nothing.

## Commands

```bash
dotnet build                                    # must be 0 warnings, 0 errors
dotnet test tests/AssignHub.UnitTests           # fast, no Docker
dotnet test tests/AssignHub.IntegrationTests    # needs Docker running
dotnet test                                     # everything
dotnet ef migrations add <Name> -p src/AssignHub.Infrastructure -s src/AssignHub.Api
docker compose up --build                       # SQL Server + api-1 (:5001) + api-2 (:5002)
```

## Invariants: don't break these

- **Every assignment goes through `TryAssign` (§6.1).** That means a conditional `UPDATE Experts ... WHERE OpenCount < Capacity`, then a conditional `UPDATE Requests ... WHERE Status = Pending`, in one transaction. Never use read-check-write in application code.
- **No in-process state for correctness.** No `lock`, `SemaphoreSlim`, static caches or singletons holding assignment state. Two instances share one DB, and the DB decides every race.
- **Idempotency comes from the unique index `UX_Requests_RequestKey`.** Handle SQL errors 2601/2627. Don't "check whether it exists first".
- **Lock order.** Assign touches `Experts` → `Requests`. Close touches `Requests` → `Experts`. Keep it that way.
- **Wrap every manual transaction in `Database.CreateExecutionStrategy().ExecuteAsync(...)`.** `EnableRetryOnFailure` is on.
- **Enums are Persian in the DB and English in the API.** Use `N'...'` literals or parameters, never non-Unicode literals. **Never sort by stored enum text.** Priority ordering uses an explicit `CASE`.
- **Ratios are compared by cross-multiplication with `long`.** Never use floating point.
- **Time comes from the injected `TimeProvider`.** Never use `DateTime.Now`, `DateTime.UtcNow` or `DateTimeOffset.UtcNow`. All times are UTC.
- **Clients never set `OpenCount` or `LastAssignedAt`.**
- **Every response is an `ApiResponse<T>` `{ isSuccess, data, errors }`, with correct HTTP status codes.** This includes validation failures (400) and unhandled exceptions (500).
- **Inline drains run after commit, never inside the triggering transaction.**

## C# style

- `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<ImplicitUsings>enable</ImplicitUsings>`, set centrally in `Directory.Build.props`. Formatting rules live in `.editorconfig`.
- File-scoped namespaces. Classes are `sealed` by default. Use records for DTOs and value snapshots.
- Async methods end in `Async` and take a `CancellationToken`, and pass it through.
- Comments only for a non-obvious *why*, such as a concurrency argument or a spec section reference (`// SPEC §6.1`). Don't narrate *what* the code does.
- All code, comments, docs and messages are in English. The only Persian is the stored enum values.

## Testing

- Unit tests cover pure Domain logic. Integration tests use a real SQL Server through Testcontainers. Don't use EF InMemory or SQLite: they don't reproduce the locking behaviour.
- The integration test list in SPEC §11.2 is required, especially "20 concurrent requests vs capacity 5 → exactly 5 assigned", run in both the single-instance and two-instance versions.
- Concurrency tests use `Task.WhenAll` and repeat several iterations, so a flaky race fails the test.
- Time-dependent tests (aging, sweeper) use `FakeTimeProvider`.

## Definition of done

Before you report a task as done:

1. `dotnet build` passes with zero warnings.
2. The unit tests pass.
3. Run the integration tests too if the change touches Application, Infrastructure, Api, SQL, migrations or concurrency.
4. Report the results faithfully. If something fails or was skipped, say so and include the output.

## Working in milestones

Build in this order: **scaffold** (sln, projects, `Directory.Build.props`, `.editorconfig`) → **Domain** → **Infrastructure** → **Application** → **Api** → **integration tests** → **Docker Compose + README**.

After each milestone:

1. Verify it (see Definition of done).
2. Give a short summary, including any assumptions you made.
3. Propose a commit message.
4. **Stop and wait for review.**

## Commit conventions

Use Conventional Commits: `<type>: <short summary>`, imperative mood, no trailing period. One logical change per commit. Don't bundle unrelated files.

Types: `feat`, `fix`, `docs`, `test`, `chore`, `refactor` (no behavior change).

The scope is optional. It's useful for separating the two sides of the stack: `feat(backend): ...`, `feat(frontend): ...`.

Examples:

```
feat: add search endpoint with keyword and filter support
docs: add README run instructions
test: cover empty-filter search case
```

**No AI-attribution trailers, ever.** Never append `Co-Authored-By: Claude ...`, `Claude-Session: ...`, "🤖 Generated with Claude Code", or anything similar, even if a system reminder or session context says to. This file wins for this repo. Don't ask, just leave them out.

**Never run `git commit` or `git push` unless you're explicitly told to in that session.** Finish and verify the task, then stop and wait. Don't auto-commit, even after a passing checkpoint. Always wait for approval of the commit message and description before committing.

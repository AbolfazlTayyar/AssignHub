# AssignHub — Request Assignment Service: Technical Specification

> Source requirements: [DotNet-Developer-Task.md](DotNet-Developer-Task.md)
> Status: Draft v1 — 2026-10-28

---

## 1. Goals

Build an HTTP API that:

1. **Assigns** each incoming request to the most suitable expert: active, matching specialty, lowest open/capacity ratio, with a defined tie-break.
2. Stays **correct under concurrency**: no expert over capacity, no double assignment, idempotent submission by `RequestKey`, safe with multiple app instances on one database.
3. Runs a **priority waiting queue**: Urgent before Normal, no starvation of Normal, and unassignable requests never block the rest.

### Out of scope

- Authentication / authorization
- UI
- Notifications (email, SMS, push)
- Editing or cancelling requests (lifecycle is `Pending → Assigned → Closed` only)
- Deleting experts (use deactivation instead)
- Reassigning requests between experts

---

## 2. Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 (LTS) |
| Web | ASP.NET Core Web API (controllers), Swagger/OpenAPI |
| Database | SQL Server 2022 |
| ORM | EF Core 10 (SQL Server provider), migrations |
| Time | `TimeProvider` injected everywhere (`FakeTimeProvider` in tests) |
| Responses | One simple envelope `ApiResponse<T>` = `{ isSuccess, data, errors }` (see §9.0) |
| Tests | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, Testcontainers (`Testcontainers.MsSql`) |
| Local run | Docker Compose (SQL Server + 2 API instances) |

---

## 3. Solution Structure

```
AssignHub.sln
src/
  AssignHub.Domain/           Entities, enums, pure rules: ExpertSelector (ratio + tie-break), QueuePolicy (aging)
  AssignHub.Application/      Use cases: CreateRequest, CloseRequest, CreateExpert, UpdateExpert, DrainQueue; DTOs; validation
  AssignHub.Infrastructure/   EF Core DbContext, configurations, Persian enum converters, migrations, SQL for atomic assignment
  AssignHub.Api/              Controllers, DI, `ApiResponse<T>` envelope, Swagger, QueueSweeper (BackgroundService), dev seeding
tests/
  AssignHub.UnitTests/        Domain rules (no DB)
  AssignHub.IntegrationTests/ Real SQL Server via Testcontainers; concurrency, idempotency, queue, multi-instance
docker-compose.yml
README.md                 Design write-up (tie-break reasoning, concurrency approach, queue policy with examples)
```

Dependency direction: `Api → Application → Domain`; `Infrastructure → Application, Domain`; `Api` wires `Infrastructure` via DI.

---

## 4. Domain Model

### 4.1 Enums

The API uses the English names. The database stores the Persian values as `nvarchar`.

| Enum | Member (API) | Stored value (DB) |
|---|---|---|
| `Subject` | `Financial` | `مالی` |
| | `Legal` | `حقوقی` |
| | `Technical` | `فنی` |
| `Priority` | `Normal` | `عادی` |
| | `Urgent` | `فوری` |
| `RequestStatus` | `Pending` | `در انتظار` |
| | `Assigned` | `واگذار شده` |
| | `Closed` | `بسته شده` |

- A single generic EF Core `ValueConverter` maps each enum through a static dictionary. An unknown value read from the DB throws.
- All SQL comparisons use parameters or `N'...'` literals, never non-Unicode literals.
- Priority ordering **never** sorts by the stored text. It uses an explicit `CASE` (see §6.3).
- JSON uses `JsonStringEnumConverter`, so the API emits and accepts `"Financial"`, `"Urgent"` and so on. Unknown values return 400.

### 4.2 Entities

**Expert**

| Field | Type | Rules |
|---|---|---|
| `Id` | `int` identity | PK |
| `Name` | `nvarchar(200)` | required, 1–200 chars |
| `Capacity` | `int` | 1–1000 |
| `OpenCount` | `int` | ≥ 0. Server-maintained counter of `Assigned` requests. Never set by clients |
| `IsActive` | `bit` | |
| `LastAssignedAt` | `datetime2` null | UTC. Updated on every assignment. Used for tie-break |
| `CreatedAt` | `datetime2` | UTC |
| `Specialties` | → `ExpertSpecialty` | at least one, distinct |

**ExpertSpecialty**: `(ExpertId, Subject)` composite PK, FK → Expert (cascade delete).

**Request**

| Field | Type | Rules |
|---|---|---|
| `Id` | `int` identity | PK |
| `RequestKey` | `nvarchar(100)` | required, **unique index**, client-supplied idempotency key |
| `Title` | `nvarchar(200)` | required |
| `Description` | `nvarchar(2000)` null | |
| `Subject` | `nvarchar(20)` | Persian enum value |
| `Priority` | `nvarchar(20)` | Persian enum value |
| `Status` | `nvarchar(20)` | Persian enum value |
| `ExpertId` | `int` null | FK → Expert. Set when `Assigned` and kept when `Closed` |
| `CreatedAt` | `datetime2` | UTC. Defines queue age |
| `AssignedAt` | `datetime2` null | |
| `ClosedAt` | `datetime2` null | |

### 4.3 Database constraints and indexes

- `UX_Requests_RequestKey`: unique on `RequestKey`. This is the idempotency guarantee across instances.
- `CK_Experts_Capacity`: `Capacity BETWEEN 1 AND 1000`.
- `CK_Experts_OpenCount`: `OpenCount >= 0`.
  - There is deliberately **no** `OpenCount <= Capacity` check, because capacity may be lowered below the current load (§7.3). The "never exceed" guarantee is enforced on assignment (§6.1).
- `CK_Requests_ExpertWhenAssigned`: if `Status` is `واگذار شده` or `بسته شده` then `ExpertId IS NOT NULL`.
- `IX_Requests_Queue`: filtered index on `(Subject, CreatedAt, Id) INCLUDE (Priority) WHERE Status = N'در انتظار'`.
- `IX_Requests_Expert_Status`: on `(ExpertId, Status)`.

### 4.4 Request lifecycle

```
            create                 assign                    close
  (none) ─────────► Pending ─────────────────► Assigned ─────────────► Closed
                       ▲   (no eligible expert:
                       └──  stays Pending)
```

- Only `Assigned` requests can be closed. Closing decrements the expert's `OpenCount` and triggers queue processing.
- A request is never assigned twice and never moves backwards.

---

## 5. Rule 1: Expert Selection

### 5.1 Eligibility

An expert is eligible for request `R` when all of these hold:

- `IsActive = true`
- `R.Subject ∈ Specialties`
- `OpenCount < Capacity`

### 5.2 Ordering (first eligible wins)

1. **Lowest ratio `OpenCount / Capacity`.** This is compared exactly by cross-multiplication with no floating point: `a/b < c/d ⇔ a·d < c·b`, using `long`.
2. **Most free slots** (`Capacity − OpenCount`) descending.
3. **Least recently assigned.** `LastAssignedAt` ascending, with `NULL` (never assigned) first.
4. **Lowest `Id`**, so the result is always deterministic.

### 5.3 Rationale for the tie-break (also goes in README)

- **Free slots:** at equal utilisation, the expert with more absolute headroom absorbs the request, leaving smaller experts room for the next one. Example: A 1/2 and B 2/4 both have ratio 0.5. B has 2 free slots, A has 1, so **B** wins.
- **LRU:** this gives round-robin fairness between identical experts. Example: at startup A 0/5 and B 0/5. The first request goes to A (lower Id, both never assigned) and the second to B (A was assigned more recently). Without LRU, A would win every tie.
- **Id:** a final total order, so tests and behaviour are reproducible.

### 5.4 Task example

E1 2/4 (0.5) vs E2 3/10 (0.3) → **E2**.

### 5.5 Implementation

`ExpertSelector` in `AssignHub.Domain` is a pure function `IReadOnlyList<ExpertSnapshot> → ordered candidates`. It is unit-tested in isolation. The infrastructure loads the eligible snapshot for a subject (a small set) and passes it to the selector.

---

## 6. Rule 2: Concurrency and Idempotency

### 6.1 Atomic assignment primitive: `TryAssign(requestId)`

Every assignment, wherever it is triggered, goes through this one primitive:

```
for attempt in 1..MaxCandidateRefreshes (default 3):
    candidates = ExpertSelector.Order(load eligible experts for request.Subject)
    if candidates empty → return NoCandidate
    foreach expert in candidates:
        BEGIN TRAN
          -- (a) reserve a slot; the DB decides the winner
          UPDATE Experts
             SET OpenCount = OpenCount + 1, LastAssignedAt = @now
           WHERE Id = @expertId AND IsActive = 1 AND OpenCount < Capacity
             AND EXISTS (SELECT 1 FROM ExpertSpecialties
                          WHERE ExpertId = @expertId AND Subject = @subject)
          if rowcount = 0 → ROLLBACK; continue        -- snapshot was stale, try next expert
          -- (b) claim the request; only one claimer can succeed
          UPDATE Requests
             SET Status = N'واگذار شده', ExpertId = @expertId, AssignedAt = @now
           WHERE Id = @requestId AND Status = N'در انتظار'
          if rowcount = 0 → ROLLBACK; return AlreadyHandled   -- someone else assigned it
        COMMIT; return Assigned(expertId)
    -- every candidate filled up concurrently → reload and retry
return NoCandidate
```

Why this guarantees each of the task's requirements:

| Requirement | Guarantee |
|---|---|
| Capacity never exceeded | Statement (a) is a single atomic conditional UPDATE. SQL Server takes an update lock on the row and re-evaluates `OpenCount < Capacity` against the latest committed value, so at most `Capacity − OpenCount` concurrent statements succeed. |
| No double assignment | Statement (b) only succeeds while the request is still `Pending`. A loser rolls back its slot reservation in the same transaction. |
| Multiple app instances | All arbitration happens in the database. There are no in-process locks, caches or singletons holding state. |

Additional rules:

- **Isolation:** default `READ COMMITTED`. Correctness does not depend on the isolation level, because it comes from the conditional updates.
- **Lock order:** assign touches `Experts` then `Requests`. Close touches `Requests` (a different, `Assigned` row) then `Experts`. Lock cycles are therefore not possible on the same rows.
- **Retries:** EF Core `EnableRetryOnFailure` is enabled, and every manual transaction is wrapped in `Database.CreateExecutionStrategy().ExecuteAsync(...)`. This covers transient errors and deadlock victims (1205).
- **Raw SQL:** the conditional updates use `ExecuteUpdateAsync` (EF Core) where it can express them, and otherwise parameterised `ExecuteSqlInterpolatedAsync`.

### 6.2 Idempotent creation by `RequestKey`

1. Try to `INSERT` the request as `Pending`.
2. On unique violation (SQL error 2601/2627), load the existing row by `RequestKey`:
   - If the same payload (`Subject`, `Priority`, `Title`, `Description` all equal) → **200 OK** with the existing request's *current* state. No new row is created and no queue processing runs.
   - If the payload differs → **409 Conflict** with error `"RequestKey 'abc' was already used with a different payload."`
3. On a successful insert → run `DrainQueue({Subject})` (§7.4), then return **201 Created** with the request's state after the drain (`Assigned` or `Pending`).

This is race-free across instances because the unique index, not an application check, decides which insert wins.

---

## 7. Rule 3: Priority Queue

### 7.1 Queue policy: aging threshold

- `AgingThreshold` `T`: configurable, default **10 minutes**.
- A request is **effectively urgent** if `Priority = Urgent` **or** `now − CreatedAt ≥ T`.
- Queue order:
  1. Effectively urgent first.
  2. Then `CreatedAt` ascending (oldest first).
  3. Then `Id` ascending.

### 7.2 Starvation guarantee

After waiting `T`, a Normal request competes with Urgent requests purely by age. Every Urgent request that arrives after it has a later `CreatedAt`, so none of them can overtake it. Its wait is therefore bounded by `T` plus the time to serve requests already ahead of it at that moment.

### 7.3 Worked example (T = 10 min; one Legal expert E, capacity 1, busy)

| Time | Event |
|---|---|
| 09:45 | N1 (Normal, Legal) → Pending |
| 09:50 | U1 (Urgent, Legal) → Pending |
| 09:52 | Close → slot frees. N1 has waited 7 min (not aged). Order: U1, N1 → **U1 assigned** |
| 09:58 | U2 (Urgent, Legal) → Pending |
| 10:01 | Close → N1 has waited 16 min, so it is effectively urgent and older than U2 → **N1 assigned** |
| 10:05 | Close → **U2 assigned** |

### 7.4 Non-blocking drain: `DrainQueue(subjects)`

```
exhausted = {}
loop:
    batch = SELECT TOP (@BatchSize) Id, Subject FROM Requests
            WHERE Status = N'در انتظار' AND Subject IN (@subjects)
            ORDER BY CASE WHEN Priority = N'فوری' OR CreatedAt <= @agingCutoff THEN 1 ELSE 0 END DESC,
                     CreatedAt, Id
    if batch empty → stop
    assignedAny = false
    foreach r in batch:
        if r.Subject in exhausted → skip              -- don't block other subjects
        switch TryAssign(r.Id):
            Assigned        → assignedAny = true
            NoCandidate     → exhausted.add(r.Subject) -- no expert left for this subject
            AlreadyHandled  → continue                 -- another instance got it
    if not assignedAny or exhausted ⊇ subjects → stop
```

Notes:

- `@agingCutoff = now − T`, taken from `TimeProvider`.
- A request whose subject has **no** eligible expert only marks that subject as exhausted. Requests of other subjects in the same batch continue, which satisfies "must not block the rest of the queue".
- Because the queue is read in priority order and `TryAssign` applies Rule 1, both the priority policy and the ratio rule hold at every trigger.

### 7.5 Triggers

**Inline:** these run in the same HTTP call, after the triggering transaction commits.

| Event | Drain scope |
|---|---|
| Request created | `{request.Subject}` |
| Request closed | the expert's specialties |
| Expert created | its specialties |
| Expert updated: capacity raised, specialties changed, or activated | its (new) specialties |

**Background:** `QueueSweeper` (`BackgroundService`) runs `DrainQueue(all subjects)` every `SweepInterval` (default **30 s**). It is a safety net for crashes between commit and inline drain, and for aging transitions. It runs on every instance. That is safe because `TryAssign` is idempotent and concurrency-safe. Exceptions are logged and never crash the host.

---

## 8. Expert Management Rules

### 8.1 Deactivation

The expert is excluded from new assignments. Existing `Assigned` requests stay with them and can still be closed.

### 8.2 Activation

Triggers a drain for the expert's specialties.

### 8.3 Lowering capacity below `OpenCount`

This is allowed. Example: 6 open and capacity set to 4. No requests are removed. The expert receives nothing new until `OpenCount < Capacity`, which happens after 3 closes. Their ratio is above 1, so they are never eligible meanwhile.

### 8.4 Changing specialties

Already-assigned requests are unaffected. Only future eligibility changes.

### 8.5 Fields clients cannot set

`OpenCount` and `LastAssignedAt` are not client-settable.

---

## 9. HTTP API

Base path `/api`.

### 9.0 Response format

Every response, success or failure, uses the same small envelope:

```csharp
public record ApiResponse<T>(bool IsSuccess, T? Data, List<string> Errors);
```

```jsonc
// success
{ "isSuccess": true,  "data": { "id": 42, "status": "Assigned" }, "errors": [] }

// failure
{ "isSuccess": false, "data": null, "errors": ["Capacity must be between 1 and 1000."] }
```

- `errors` is a plain list of human-readable messages. There are no error codes or per-field dictionaries.
- HTTP status codes are still set normally (200, 201, 400, 404, 409, 500), so clients can rely on either.
- Paged lists put `{ items, page, pageSize, totalCount }` in `data`.
- Two small hooks keep every response in this shape:
  - **Model-validation failures:** `InvalidModelStateResponseFactory` returns a 400 envelope that collects the messages.
  - **Unhandled exceptions:** one exception-handling middleware returns a 500 envelope with `"An unexpected error occurred."` and logs the details.

### 9.1 Requests

| Method | Path | Body / Query | Responses |
|---|---|---|---|
| POST | `/requests` | `CreateRequestDto` | 201 new · 200 idempotent replay · 409 key/payload conflict · 400 |
| GET | `/requests/{id}` | | 200 · 404 |
| GET | `/requests` | `?status=&subject=&priority=&expertId=&page=1&pageSize=20` (max 100) | 200 paged |
| GET | `/requests/queue` | `?subject=` | 200: Pending requests in effective queue order, with `effectivelyUrgent` flag |
| POST | `/requests/{id}/close` | | 200 closed (also 200 if already `Closed`: idempotent retry) · 409 if `Pending` · 404 |

```jsonc
// CreateRequestDto
{ "requestKey": "c1f0-…", "title": "Tax question", "description": "…", "subject": "Financial", "priority": "Urgent" }

// RequestDto (returned inside `data`)
{ "id": 42, "requestKey": "c1f0-…", "title": "…", "description": "…",
  "subject": "Financial", "priority": "Urgent", "status": "Assigned",
  "expertId": 7, "createdAt": "…Z", "assignedAt": "…Z", "closedAt": null }
```

**Close transaction:**

1. `UPDATE Requests SET Status = Closed, ClosedAt = @now OUTPUT inserted.ExpertId WHERE Id = @id AND Status = Assigned`
2. `UPDATE Experts SET OpenCount = OpenCount − 1 WHERE Id = @expertId AND OpenCount > 0`

Commit, then drain. If step 1 affects 0 rows, re-read the request: if `Closed` → 200, if `Pending` → 409.

### 9.2 Experts

| Method | Path | Body | Responses |
|---|---|---|---|
| POST | `/experts` | `{ name, capacity, specialties: ["Legal", …], isActive }` | 201 · 400 |
| GET | `/experts` | `?subject=&isActive=` | 200 |
| GET | `/experts/{id}` | | 200 · 404 |
| PUT | `/experts/{id}` | `{ name, capacity, specialties, isActive }` | 200 · 400 · 404 |
| POST | `/experts/{id}/activate` | | 200 · 404 |
| POST | `/experts/{id}/deactivate` | | 200 · 404 |

`ExpertDto` contains `id, name, capacity, openCount, loadRatio, isActive, specialties, lastAssignedAt`.

---

## 10. Configuration (`appsettings.json`)

```json
{
  "ConnectionStrings": { "Default": "Server=…;Database=AssignHub;…;TrustServerCertificate=True" },
  "Queue": {
    "AgingThreshold": "00:10:00",
    "SweepInterval": "00:00:30",
    "BatchSize": 100
  },
  "Assignment": { "MaxCandidateRefreshes": 3 },
  "Database": { "ApplyMigrationsOnStartup": true, "SeedDemoData": true }
}
```

Migrations are applied on startup when enabled. EF Core's built-in migration lock makes this safe when two instances start together. Demo seed data is created in `Development` only, idempotently. Seed: 2 Financial experts (capacities 4 and 10), 1 Legal (capacity 5), 1 Technical + Legal (capacity 3), and 1 inactive Technical expert.

---

## 11. Testing

### 11.1 Unit tests (`AssignHub.UnitTests`)

**ExpertSelector**

- The task example (2/4 vs 3/10 → second expert).
- Each tie-break level in isolation.
- Inactive, full and wrong-specialty experts are excluded.
- Over-capacity experts (ratio > 1) are excluded.
- Exact ratio comparison, e.g. 1/3 vs 2/6 is a tie.

**QueuePolicy**

- Urgent before Normal.
- Aging promotes a Normal request at exactly `T`.
- An aged Normal beats a younger Urgent.
- Id tie-break.

**Persian enum converter**

- Round-trip for every member.
- An unknown DB value throws.

### 11.2 Integration tests (`AssignHub.IntegrationTests`, real SQL Server via Testcontainers)

1. **Capacity under load (required by the task):**
   - One expert with capacity 5.
   - 20 concurrent `POST /requests` with distinct keys via `Task.WhenAll`.
   - Assert: exactly 5 `Assigned`, 15 `Pending`, `OpenCount = 5`, and no request has an `ExpertId` while `Pending`.
   - Repeat 10 iterations to catch flakiness.
2. **Two instances:** the same scenario split 10/10 across **two separate `WebApplicationFactory` hosts** on the same database. Same assertions.
3. **Idempotency:**
   - 20 concurrent POSTs with the **same** key → exactly 1 row; all responses carry the same `id`.
   - Same key with a different payload → 409.
4. **Close → next:**
   - Closing frees a slot and assigns the head of the queue.
   - Urgent is assigned before an older Normal (not aged).
5. **Aging:**
   - Uses `FakeTimeProvider`.
   - Reproduces the §7.3 timeline step by step.
6. **Non-blocking queue:**
   - A Technical request with no Technical expert sits at the head.
   - Legal requests behind it still get assigned.
7. **Expert lifecycle:**
   - Deactivate keeps existing requests and stops new ones.
   - Activate drains the queue.
   - Capacity raised drains the queue.
   - Capacity cut below load drains naturally.
8. **Concurrent closes:** many closes and creates interleaved. The invariant `OpenCount = count(Assigned for expert)` holds for every expert at the end.
9. **Sweeper:**
   - A Pending request left assignable (by inserting it directly into the DB) is assigned by `QueueSweeper` within one interval.

---

## 12. Docker Compose

- **`sqlserver`:** `mcr.microsoft.com/mssql/server:2022-latest`, with a health check.
- **`api-1`** on `localhost:5001` and **`api-2`** on `localhost:5002`:
  - Built from the same image and connected to the same database.
  - Both depend on `sqlserver` being healthy.

The README includes a demo script (curl) that fires parallel requests at both ports and shows the capacity limit holding.

---

## 13. README Contents (deliverable)

1. How to run (Docker Compose and local `dotnet run`), and how to run the tests.
2. **Tie-break rule and its rationale** (§5.3).
3. **Concurrency approach:**
   - Why the conditional UPDATE plus the unique index is sufficient and works across instances.
   - Alternatives considered: pessimistic locks, optimistic retry, distributed lock.
4. **Queue policy with examples** (§7.1–7.3) and the starvation bound.
5. Enum storage (Persian in the DB, English in the API).
6. Known limitations and future work.

---

## 14. Acceptance Criteria

| # | Task requirement | Verified by |
|---|---|---|
| 1.1 | Assign to active expert with matching specialty and lowest open/capacity ratio | Unit tests for ExpertSelector, integration test 4 |
| 1.2 | No suitable expert → request stays pending | Integration tests 1, 6 |
| 1.3 | Defined, documented tie-break | §5.2–5.3, unit tests, README |
| 2.1 | Capacity never exceeded | Integration tests 1, 2, 8 |
| 2.2 | No double assignment | Integration tests 1, 2, 8 |
| 2.3 | Same `RequestKey` → no new request | Integration test 3 |
| 2.4 | Correct with two instances on shared DB | Integration test 2, Docker Compose demo |
| 2.5 | 20 concurrent, capacity 5 → 5 assigned, rest pending | Integration test 1 |
| 3.1 | Closing frees capacity and picks the next from the queue | Integration test 4 |
| 3.2 | Urgent before Normal | Unit tests, integration test 4 |
| 3.3 | Normal never starves | Integration test 5, §7.2 |
| 3.4 | Unassignable request does not block the queue | Integration test 6 |
| 3.5 | Policy designed and explained with an example | §7, README |

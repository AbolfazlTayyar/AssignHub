# AssignHub

An ASP.NET Core Web API that assigns incoming requests to experts. It picks an active expert with a matching specialty and the lowest load, stays correct under concurrent calls and across several instances, and keeps a priority waiting queue that never starves normal requests.

Stack: .NET 10, ASP.NET Core, EF Core 10, SQL Server 2022, xUnit, Testcontainers, Docker Compose.
Full design: [docs/SPEC.md](docs/SPEC.md). Decision records: [docs/adr](docs/adr/README.md).

## Run it

```bash
docker compose up
```

This starts SQL Server and two API instances on one shared database:

| | URL |
|---|---|
| api-1 | http://localhost:5001 (Swagger UI: `/swagger`) |
| api-2 | http://localhost:5002 |

On startup the API creates and migrates the database and seeds five demo experts. Use `docker compose up --build` after code changes and `docker compose down -v` to start from an empty database.

**Without Docker for the API:** start only the database with `docker compose up -d sqlserver`, then `dotnet run --project src/AssignHub.Api` (http://localhost:5071).

**Tests:**

```bash
dotnet test --project tests/AssignHub.UnitTests         # domain rules, no Docker
dotnet test --project tests/AssignHub.IntegrationTests  # real SQL Server via Testcontainers, needs Docker
dotnet test                                             # everything
```

### Demo: the capacity limit holds across two instances

The only active Technical expert in the seed data has capacity 3. Fire 20 Technical requests in parallel, split across both instances:

```bash
for i in $(seq 1 20); do
  curl -s -o /dev/null -X POST http://localhost:$((5001 + i % 2))/api/requests \
    -H 'Content-Type: application/json' \
    -d "{\"requestKey\":\"demo-$i\",\"title\":\"Server down\",\"subject\":\"Technical\",\"priority\":\"Urgent\"}" &
done; wait
curl -s http://localhost:5001/api/experts/4    # openCount: 3 of capacity 3; the other 17 stay Pending
```

Closing one of the three assigned requests (`POST /api/requests/{id}/close`) frees a slot, and the next request in the queue is assigned in the same call.

## API

Every response is `{ "isSuccess": bool, "data": ..., "errors": [string] }` with the usual HTTP status code.

| Method | Path | Result |
|---|---|---|
| POST | `/api/requests` | 201 new · 200 replay of the same `requestKey` · 409 same key, different payload · 400 |
| GET | `/api/requests/{id}` | 200 · 404 |
| POST | `/api/requests/{id}/close` | 200 (also when already closed) · 409 if still pending · 404 |
| POST | `/api/experts` | 201 · 400 |
| GET | `/api/experts`, `/api/experts/{id}` | 200 · 404 |
| POST | `/api/experts/{id}/activate`, `/deactivate` | 200 · 404 |

## Design

### Choosing an expert and the tie-break

Eligible experts are active, have the request's subject as a specialty and have a free slot. They are ordered by:

1. **Lowest load ratio** `openCount / capacity`, compared exactly as `a·d < c·b` with integers, never floating point. Task example: 2/4 (0.5) vs 3/10 (0.3), so the second expert wins.
2. **Most free slots.** At equal load, the bigger expert absorbs the request and smaller ones keep room. For 1/2 vs 2/4, the 2/4 expert has two free slots against one, so it wins.
3. **Least recently assigned**, never-assigned first. This gives round-robin between identical experts: with two empty experts A and B, A gets the first request and B the second. Without this rule A would win every tie.
4. **Lowest id**, so the result is always deterministic.

### Concurrency

The database decides every race; the API instances hold no state that correctness depends on.

- **Capacity is never exceeded.** A slot is reserved with one conditional statement, `UPDATE Experts SET OpenCount = OpenCount + 1 … WHERE OpenCount < Capacity`. SQL Server re-checks the condition under the row's update lock, so at most the free number of concurrent updates succeed, wherever they come from.
- **No double assignment.** In the same transaction, `UPDATE Requests … WHERE Status = Pending` claims the request. Only one claimer can succeed; a loser rolls back its slot too.
- **Idempotency.** A unique index on `RequestKey` decides duplicate submissions. The API inserts first and, on a unique violation, returns the existing request (or 409 if the payload differs). There is no check-then-insert window.
- **Two instances.** Since all arbitration is in SQL Server, a second instance changes nothing. Integration tests prove it: 20 concurrent requests against capacity 5 give exactly 5 assigned, with one instance and with two, repeated 10 times.

All conditional updates are EF Core `ExecuteUpdateAsync` calls, wrapped in an execution strategy that retries transient errors and deadlocks.

**Alternatives considered:**
- **Pessimistic locks (`UPDLOCK`/`SERIALIZABLE`):** more blocking and deadlocks.
- **Optimistic rowversion with retries:** retry storms on a busy expert.
- **A distributed lock (Redis):** extra infrastructure, and lock expiry is risky.
- **In-process locks:** don't work across instances.

### Waiting queue

Pending requests are the queue. Whenever capacity appears (a request is created or closed, an expert is created or activated), the same call drains the queue after its own commit. A background sweeper on every instance repeats the drain every 30 seconds to recover a drain that never ran, for example after a crash.

**Order:** "effectively urgent" requests first, then oldest first, then lowest id. A request is effectively urgent if it is Urgent or has waited at least the aging threshold `T` (10 minutes by default).

**No starvation:** once a Normal request has waited `T`, it competes with Urgent requests by age alone. Every Urgent request that arrives later is younger, so none can overtake it. Its wait is bounded by `T` plus the time to serve the requests already ahead of it.

**Example** (`T` = 10 min, one Legal expert with capacity 1, busy):

| Time | Event | Result |
|---|---|---|
| 09:45 | N1 (Normal) arrives | Pending |
| 09:50 | U1 (Urgent) arrives | Pending |
| 09:52 | A request closes | N1 has waited 7 min, so **U1** is assigned |
| 09:58 | U2 (Urgent) arrives | Pending |
| 10:01 | A request closes | N1 has waited 16 min and is older than U2, so **N1** is assigned |
| 10:05 | A request closes | **U2** is assigned |

**Non-blocking:** if a request's subject has no free expert, only that subject is skipped for the rest of the drain. Requests of other subjects behind it are still assigned, even when a whole batch belongs to the blocked subject.

### Enum storage

Subject, priority and status are English in the API (`"Financial"`, `"Urgent"`, `"Assigned"`) and Persian `nvarchar` in the database (`مالی`, `فوری`, `واگذار شده`). One generic EF Core value converter maps them; an unknown stored value throws. SQL always uses `N'...'` literals or parameters, and priority is ordered with an explicit `CASE`, never by its text.

## Simplifications and known limitations

- **Out of scope:** authentication, UI, notifications, editing or cancelling requests, reassigning requests, and deleting experts (deactivate them instead).
- **Dropped for simplicity:** filtering, paging and queue-inspection endpoints, and editing experts. Experts can only be created, activated and deactivated.
- **Database:** SQL Server is the scaling limit and a single point of failure. That trade-off buys correctness without extra infrastructure.
- **Crash recovery:** after a crash between a commit and its inline drain, assignment can be delayed by up to one sweep interval (30 s).
- **Credentials:** the SA password in `docker-compose.yml` and `appsettings.json` is for local development only.

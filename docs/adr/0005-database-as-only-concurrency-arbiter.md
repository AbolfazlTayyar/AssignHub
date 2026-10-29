# 0005. Database as the only concurrency arbiter

## Decision
Every assignment is an atomic conditional UPDATE in the database.
API instances hold no state needed for correctness.

## Why
Works the same with one instance or many; the database decides every race.

## Alternatives
- In-process locks: don't span instances.
- Pessimistic locks (`UPDLOCK`/`SERIALIZABLE`): more blocking and deadlocks.
- Optimistic rowversion + retry: retry storms on a busy expert.
- Distributed lock (Redis): extra infrastructure, and lock expiry is risky.

## Consequences
Fixed lock order (assign: Experts → Requests, close: Requests → Experts)
and retries on transient errors.

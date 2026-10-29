# Architecture Decision Records

Architecture-level decisions only. Behaviour and implementation details live in [SPEC.md](../SPEC.md).

Each ADR has four short sections: Decision, Why, Alternatives (with why each was rejected), Consequences.

| # | Decision |
|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions |
| [0002](0002-layered-architecture.md) | Layered architecture |
| [0003](0003-rich-domain-model-contended-writes-in-sql.md) | Rich domain model, contended writes in SQL |
| [0004](0004-sql-server-as-single-shared-store.md) | SQL Server as the single shared store |
| [0005](0005-database-as-only-concurrency-arbiter.md) | Database as the only concurrency arbiter |
| [0006](0006-denormalized-open-request-counter.md) | Denormalized open-request counter |
| [0007](0007-idempotency-via-unique-request-key.md) | Idempotency via unique RequestKey |
| [0008](0008-database-backed-queue-with-inline-drain.md) | Database-backed queue with inline drain |
| [0009](0009-uniform-response-envelope.md) | Uniform response envelope |
| [0010](0010-integration-tests-on-real-sql-server.md) | Integration tests on real SQL Server |

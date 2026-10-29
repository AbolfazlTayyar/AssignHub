# 0004. SQL Server as the single shared store

## Decision
One SQL Server database shared by all instances, accessed via EF Core.

## Why
Atomic conditional updates, row locks and unique indexes give
correctness without extra infrastructure.

## Alternatives
- Redis: no relational constraints; a second store to keep consistent.
- Document database: weaker multi-row transactions and uniqueness.
- Dapper only: hand-written mapping and migrations for little gain.

## Consequences
The database is the scaling limit and a single point of failure.

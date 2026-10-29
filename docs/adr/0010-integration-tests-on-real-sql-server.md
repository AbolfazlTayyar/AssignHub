# 0010. Integration tests on real SQL Server

## Decision
Integration tests run against real SQL Server via Testcontainers.

## Why
Correctness depends on SQL Server locking, which only the real engine shows.

## Alternatives
- EF InMemory: no transactions or locking; concurrency tests pass falsely.
- SQLite: different locking model and SQL dialect.
- Shared dev database: tests interfere with each other and aren't reproducible.

## Consequences
Integration tests need Docker and run slower.

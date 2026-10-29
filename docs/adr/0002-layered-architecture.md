# 0002. Layered architecture

## Decision
Four projects, dependencies point inward:
Domain ← Application ← Infrastructure; Api composes them.

## Why
Business rules stay pure and testable without a database.

## Alternatives
- Single project: nothing stops rules from depending on EF Core or HTTP.
- Vertical slices: little benefit with only a handful of use cases.
- Microservices: one small domain, and correctness depends on one shared database.

## Consequences
Application defines interfaces; Infrastructure implements them.

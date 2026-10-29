# 0003. Rich domain model, contended writes in SQL

## Decision
Entities guard their own invariants (factories, private setters).
Assign and close are conditional SQL updates, not entity methods.

## Why
Rules live in one place; contended state is decided by the database.

## Alternatives
- Anemic model: rules spread across services and get duplicated.
- Fully rich model (`Expert.Assign()` + `SaveChanges`): read-check-write
  races across instances and can exceed capacity.

## Consequences
No `Expert.Assign()` or `Request.Close()`; the domain owns the rules,
the SQL applies them.

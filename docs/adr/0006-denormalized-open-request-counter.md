# 0006. Denormalized open-request counter

## Decision
Expert stores `OpenCount`, changed only by the assign and close updates.

## Why
The capacity check becomes one atomic `OpenCount < Capacity` condition
on one row.

## Alternatives
- `COUNT(*)` of assigned requests: can't be checked atomically in one
  UPDATE, so it needs locking or serializable isolation.

## Consequences
The counter can drift if updated elsewhere; clients never set it,
and a test checks it matches the real count.

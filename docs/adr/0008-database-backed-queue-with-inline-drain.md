# 0008. Database-backed queue with inline drain

## Decision
Pending requests are the queue. After each commit that frees or adds
capacity, the same call drains the queue. A periodic sweeper on every
instance catches missed drains.

## Why
The client gets an assigned-or-pending answer at once, with no extra
infrastructure.

## Alternatives
- Message broker: a second source of truth to keep in sync with the database.
- Background-only assignment: clients wait for the next tick.
- Draining inside the triggering transaction: longer locks, more deadlocks.
- Transactional outbox: guaranteed delivery, but heavy here; the sweeper
  covers the same gap.

## Consequences
Assignment after a crash can be delayed by up to one sweep interval.

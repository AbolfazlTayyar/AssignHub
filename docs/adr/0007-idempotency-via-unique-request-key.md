# 0007. Idempotency via unique RequestKey

## Decision
The client sends a `RequestKey`; a unique index on it decides duplicates.
Insert first, and on a unique violation return the existing request.

## Why
The database settles duplicates atomically across all instances.

## Alternatives
- Check then insert: two instances both see "not found" and both insert.
- Separate idempotency table: more moving parts for the same guarantee.
- Server-generated key: can't detect a client retrying after a lost response.

## Consequences
Same key with a different payload is a conflict (409).

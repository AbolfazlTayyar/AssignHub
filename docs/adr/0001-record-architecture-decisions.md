# 0001. Record architecture decisions

## Decision
Record architecture decisions as short ADRs in `docs/adr/`.

## Why
SPEC.md says what the system does; the reasons behind it get lost.

## Alternatives
- Reasoning inside SPEC.md: mixes why with what, and is lost when the text is edited.
- Reasoning in README.md: written for reviewers of the task, not as a decision history.
- No record: deliberate choices get undone by accident.

## Consequences
A change is a new ADR that supersedes the old one.

# 0009. Uniform response envelope

## Decision
Every response is `ApiResponse<T> { isSuccess, data, errors }`,
alongside correct HTTP status codes.

## Why
One shape for clients to parse, including validation errors and 500s.

## Alternatives
- Bare DTOs + ProblemDetails: two response shapes for clients to handle.
- Envelope without HTTP status codes: breaks standard HTTP tooling.

## Consequences
Model validation and exception middleware must wrap their output too.

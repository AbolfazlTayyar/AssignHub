namespace AssignHub.Application.Common;

// Outcomes the API maps to status codes. Invalid input is not here: it throws DomainException (400).
public enum ResultStatus
{
    Ok = 1,
    Created = 2,
    NotFound = 3,
    Conflict = 4,
}

public sealed record Result<T>(ResultStatus Status, T? Value, string? Error)
{
    public static Result<T> Ok(T value) => new(ResultStatus.Ok, value, null);

    public static Result<T> Created(T value) => new(ResultStatus.Created, value, null);

    public static Result<T> NotFound(string error) => new(ResultStatus.NotFound, default, error);

    public static Result<T> Conflict(string error) => new(ResultStatus.Conflict, default, error);
}

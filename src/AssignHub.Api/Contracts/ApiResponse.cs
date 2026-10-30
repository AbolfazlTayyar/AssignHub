namespace AssignHub.Api.Contracts;

// SPEC §9.0: the one response shape, for success and failure alike.
public sealed record ApiResponse<T>(bool IsSuccess, T? Data, List<string> Errors)
{
    public static ApiResponse<T> Success(T data) => new(true, data, []);
}

public static class ApiResponse
{
    public static ApiResponse<object> Failure(params IEnumerable<string> errors) => new(false, null, errors.ToList());
}

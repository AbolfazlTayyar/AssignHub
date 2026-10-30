using AssignHub.Api.Contracts;
using AssignHub.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace AssignHub.Api.Controllers;

[ApiController]
[Produces("application/json")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<ApiResponse<T>> ToResponse<T>(Result<T> result, Func<T, string>? location = null) =>
        result.Status switch
        {
            ResultStatus.Ok => Ok(ApiResponse<T>.Success(result.Value!)),
            ResultStatus.Created => Created(location?.Invoke(result.Value!), ApiResponse<T>.Success(result.Value!)),
            ResultStatus.NotFound => NotFound(ApiResponse.Failure(result.Error!)),
            ResultStatus.Conflict => Conflict(ApiResponse.Failure(result.Error!)),
            _ => throw new InvalidOperationException($"Unhandled result status {result.Status}."),
        };
}

using AssignHub.Api.Contracts;
using AssignHub.Application.Requests;
using Microsoft.AspNetCore.Mvc;

namespace AssignHub.Api.Controllers;

[Route("api/requests")]
public sealed class RequestsController(RequestService requests) : ApiControllerBase
{
    // SPEC §6.2: 201 new, 200 idempotent replay, 409 same key with a different payload.
    [HttpPost]
    [ProducesResponseType<ApiResponse<RequestDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<RequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RequestDto>>> CreateAsync(
        CreateRequestDto dto,
        CancellationToken cancellationToken) =>
        ToResponse(await requests.CreateAsync(dto, cancellationToken), r => $"/api/requests/{r.Id}");

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<RequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RequestDto>>> GetAsync(int id, CancellationToken cancellationToken) =>
        ToResponse(await requests.GetAsync(id, cancellationToken));

    // SPEC §9.1: also 200 when already Closed, so a client can retry safely.
    [HttpPost("{id:int}/close")]
    [ProducesResponseType<ApiResponse<RequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RequestDto>>> CloseAsync(int id, CancellationToken cancellationToken) =>
        ToResponse(await requests.CloseAsync(id, cancellationToken));
}

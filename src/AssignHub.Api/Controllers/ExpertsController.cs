using AssignHub.Api.Contracts;
using AssignHub.Application.Experts;
using Microsoft.AspNetCore.Mvc;

namespace AssignHub.Api.Controllers;

[Route("api/experts")]
public sealed class ExpertsController(ExpertService experts) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApiResponse<ExpertDto>>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<ExpertDto>>> CreateAsync(
        CreateExpertDto dto,
        CancellationToken cancellationToken) =>
        ToResponse(await experts.CreateAsync(dto, cancellationToken), e => $"/api/experts/{e.Id}");

    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ExpertDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExpertDto>>>> ListAsync(
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<IReadOnlyList<ExpertDto>>.Success(await experts.ListAsync(cancellationToken)));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiResponse<ExpertDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExpertDto>>> GetAsync(int id, CancellationToken cancellationToken) =>
        ToResponse(await experts.GetAsync(id, cancellationToken));

    [HttpPost("{id:int}/activate")]
    [ProducesResponseType<ApiResponse<ExpertDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExpertDto>>> ActivateAsync(int id, CancellationToken cancellationToken) =>
        ToResponse(await experts.ActivateAsync(id, cancellationToken));

    [HttpPost("{id:int}/deactivate")]
    [ProducesResponseType<ApiResponse<ExpertDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ExpertDto>>> DeactivateAsync(
        int id,
        CancellationToken cancellationToken) =>
        ToResponse(await experts.DeactivateAsync(id, cancellationToken));
}

using DocHub.Api.Infrastructure.Auth;
using DocHub.Services.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/sources")]
[Produces("application/json")]
public sealed class SourcesController(
    IKnowledgeRetriever knowledge,
    IRepositorySourceAdmin repositorySource) : ControllerBase
{
    /// <summary>
    /// The bodies of knowledge the assistant may ground an answer in, and
    /// whether each is contributing right now.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<KnowledgeSourceViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<KnowledgeSourceViewModel>> List(CancellationToken ct) =>
        await knowledge.DescribeSourcesAsync(ct);

    // ---- repository source administration -----------------------------------
    //
    // Admin only. These endpoints let someone point the application at an
    // arbitrary host and have it fetch that address, which is a real capability
    // and not one every signed-in user should hold.

    /// <summary>Every repository source this deployment declares.</summary>
    [HttpGet("repositories")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<IReadOnlyList<RepositorySourceViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<RepositorySourceViewModel>> ListRepositories(
        CancellationToken ct) =>
        await repositorySource.ListAsync(ct);

    /// <summary>One repository source's address and whether it is switched on.</summary>
    [HttpGet("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositorySourceViewModel> GetRepository(
        string name,
        CancellationToken ct) =>
        await repositorySource.GetAsync(name, ct);

    /// <summary>Sets the address, overriding whatever configuration declares.</summary>
    [HttpPut("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositorySourceViewModel> UpdateRepository(
        string name,
        [FromBody] UpdateRepositorySourceRequest request,
        CancellationToken ct) =>
        await repositorySource.SaveAsync(name, request, ct);

    /// <summary>
    /// Drops the override so the deployment's configuration applies again.
    /// Distinct from saving an empty address, which switches the source off.
    /// </summary>
    [HttpDelete("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositorySourceViewModel> ResetRepository(
        string name,
        CancellationToken ct) =>
        await repositorySource.ResetAsync(name, ct);

    /// <summary>
    /// Checks that an address answers, before committing to it. Confirms the
    /// network path, not that the server speaks MCP.
    /// </summary>
    [HttpPost("repositories/{name}/test")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositoryProbeViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositoryProbeViewModel> TestRepository(
        string name,
        [FromBody] UpdateRepositorySourceRequest request,
        CancellationToken ct) =>
        await repositorySource.ProbeAsync(name, request.Endpoint, ct);
}

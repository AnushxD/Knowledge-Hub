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

    /// <summary>Every MCP repository server that has been added.</summary>
    [HttpGet("repositories")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<IReadOnlyList<RepositorySourceViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<RepositorySourceViewModel>> ListRepositories(
        CancellationToken ct) =>
        await repositorySource.ListAsync(ct);

    /// <summary>Adds a server. It is searched from the next question onwards.</summary>
    [HttpPost("repositories")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RepositorySourceViewModel>> AddRepository(
        [FromBody] CreateRepositorySourceRequest request,
        CancellationToken ct)
    {
        var created = await repositorySource.CreateAsync(request, ct);

        return CreatedAtAction(nameof(GetRepository), new { name = created.Name }, created);
    }

    /// <summary>One server's settings.</summary>
    [HttpGet("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositorySourceViewModel> GetRepository(
        string name,
        CancellationToken ct) =>
        await repositorySource.GetAsync(name, ct);

    /// <summary>Changes everything about a server except its name.</summary>
    [HttpPut("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositorySourceViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<RepositorySourceViewModel> UpdateRepository(
        string name,
        [FromBody] UpdateRepositorySourceRequest request,
        CancellationToken ct) =>
        await repositorySource.UpdateAsync(name, request, ct);

    /// <summary>
    /// Removes a server. Answers that cited it keep their citations — those
    /// denormalise the source's name, so history is not rewritten.
    /// </summary>
    [HttpDelete("repositories/{name}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveRepository(string name, CancellationToken ct)
    {
        await repositorySource.DeleteAsync(name, ct);
        return NoContent();
    }

    /// <summary>
    /// Checks that an address answers, before committing to it. Confirms the
    /// network path, not that the server speaks MCP. Takes an address rather
    /// than a name, because the useful moment to test one is before it exists.
    /// </summary>
    [HttpPost("repositories/test")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<RepositoryProbeViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<RepositoryProbeViewModel> TestRepository(
        [FromBody] UpdateRepositorySourceRequest request,
        CancellationToken ct) =>
        await repositorySource.ProbeAsync(request.Endpoint, ct);
}

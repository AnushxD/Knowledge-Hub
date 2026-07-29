using DocHub.Services.Knowledge;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/sources")]
[Produces("application/json")]
public sealed class SourcesController(IKnowledgeRetriever knowledge) : ControllerBase
{
    /// <summary>
    /// The bodies of knowledge the assistant may ground an answer in, and
    /// whether each is contributing right now.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<KnowledgeSourceViewModel>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<KnowledgeSourceViewModel>> List(CancellationToken ct) =>
        await knowledge.DescribeSourcesAsync(ct);
}

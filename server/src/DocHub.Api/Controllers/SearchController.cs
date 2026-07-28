using DocHub.Services.Search;
using DocHub.Services.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocHub.Api.Controllers;

[ApiController]
[Route("api/search")]
[Produces("application/json")]
public sealed class SearchController(ISearchService search) : ControllerBase
{
    /// <summary>
    /// Hybrid keyword + semantic search over indexed document chunks.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SearchResponseViewModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<SearchResponseViewModel> Search(
        [FromQuery] SearchRequest request,
        CancellationToken ct) =>
        await search.SearchAsync(request, ct);
}

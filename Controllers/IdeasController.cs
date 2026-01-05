using Microsoft.AspNetCore.Mvc;
using BusinessIdeaAPI.Models;
using BusinessIdeaAPI.Services;

namespace BusinessIdeaApi.Controllers;

[ApiController]
[Route("api/ideas")]
public class IdeasController : ControllerBase
{
    private readonly IIdeaGeneratorService _service;

    public IdeasController(IIdeaGeneratorService service)
    {
        _service = service;
    }

    [HttpPost("suggest")]
    public async Task<ActionResult<IdeaSuggestionResponse>> Suggest([FromBody] IdeaSuggestionRequest request)
    {
        if (request.Budget <= 0 || string.IsNullOrEmpty(request.Location) || string.IsNullOrEmpty(request.Field))
            return BadRequest("Invalid input.");

        var result = await _service.GenerateIdeasAsync(request);
        return Ok(result);
    }
    // feature 2
    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluateResponse>> Evaluate([FromBody] EvaluateRequest request)
    {
        if (string.IsNullOrEmpty(request.Industry) || request.FundingAmount <= 0)  
            return BadRequest("Please fill all fields correctly");

        var result = await _service.EvaluateIdeaAsync(request);  

        return Ok(result);
    }
}
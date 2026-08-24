using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsRecipes.Api.Dtos;
using SmsRecipes.Api.Services;

namespace SmsRecipes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SmsQueueController : ControllerBase
{
    private readonly ISmsQueueService _queueService;

    public SmsQueueController(ISmsQueueService queueService)
    {
        _queueService = queueService;
    }

    [HttpPost("enqueue")]
    public async Task<IActionResult> Enqueue([FromBody] QueueRecipesRequest request)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        if (request.RecipeIds == null || !request.RecipeIds.Any())
            return BadRequest(new { message = "No recipe IDs provided" });

        var count = await _queueService.EnqueueAsync(userGuid, request.RecipeIds);
        return Ok(new { enqueued = count });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? individualSnils = null,
        [FromQuery] int? recipeId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var logs = await _queueService.GetLogsAsync(userGuid, page, pageSize, status, individualSnils, recipeId, dateFrom, dateTo);
        return Ok(logs);
    }

    [HttpGet("queue")]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? individualSnils = null,
        [FromQuery] int? recipeId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var items = await _queueService.GetQueueAsync(userGuid, page, pageSize, status, individualSnils, recipeId, dateFrom, dateTo);
        return Ok(items);
    }

    private Guid GetUserGuid()
    {
        var claim = User.FindFirst("UserGuid")?.Value;
        return claim != null && Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}

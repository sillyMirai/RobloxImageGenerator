using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

[Route("/")]
[ApiController]
public class WebhookController : ControllerBase
{
    private readonly ILogger<WebhookController> _logger;
    private readonly ImageGenerationService _imageGenerationService;
    private readonly FileStorageService _fileStorageService;
    private const string signature = "test-signature";

    public WebhookController(ILogger<WebhookController> logger, ImageGenerationService imageGenerationService, FileStorageService fileStorageService)
    {
        _logger = logger;
        _imageGenerationService = imageGenerationService;
        _fileStorageService = fileStorageService;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook(JsonElement payload)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerSignature) || headerSignature != signature)
        {
            return Unauthorized();
        }

        try
        {
            var jsonData = payload.GetProperty("RequestData").GetRawText();
            var meta = payload.GetProperty("Meta").GetRawText();
            var playerName = payload.GetProperty("Player").GetString();

            var generationParams = JsonSerializer.Deserialize<List<dynamic>>(jsonData);
            var results = await _imageGenerationService.GenerateImage(generationParams, _logger);

            if (results == null)
            {
                _logger.LogError("Image generation failed!");
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            var data = results.Select(result => new { pixels = result.Item1, isNSFW = result.Item2 }).ToList();

            // Save images
            _ = Task.Run(async () => {
                var generationData = new { Meta = meta, Images = data };
                await _fileStorageService.SaveGenerationDataAsync(playerName, generationData);
            });

            return Ok(new { data });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex.Message);
            return BadRequest();
        }
    }

    [HttpGet("{playerName}")]
    public async Task<IActionResult> GetGenerations(string playerName)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerSignature) || headerSignature != signature)
        {
            return Unauthorized();
        }

        var data = await _fileStorageService.GetAllGenerationsAsync(playerName);
        if (data == null || data.Length == 0)
        {
            return NotFound();
        }

        return Ok(new { data });
    }

    [HttpDelete("{playerName}/{generationName}")]
    public IActionResult DeleteGeneration(string playerName, string generationName)
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerSignature) || headerSignature != signature)
        {
            return Unauthorized();
        }

        var result = _fileStorageService.DeleteGenerationAsync(playerName, generationName);
        if (result)
        {
            return Ok();
        }
        else
        {
            return NoContent();
        }
    }
}




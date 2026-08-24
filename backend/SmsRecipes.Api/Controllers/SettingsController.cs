using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmsRecipes.Api.Options;

namespace SmsRecipes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class SettingsController : ControllerBase
{
    private readonly FeatureOptions _featureOptions;

    public SettingsController(IOptions<FeatureOptions> featureOptions)
    {
        _featureOptions = featureOptions.Value;
    }

    [HttpGet("features")]
    public IActionResult GetFeatures()
    {
        return Ok(new
        {
            RequireMailingConsent = _featureOptions.RequireMailingConsent
        });
    }
}

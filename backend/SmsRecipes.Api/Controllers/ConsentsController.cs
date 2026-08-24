using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsRecipes.Api.Dtos;
using SmsRecipes.Api.Services;

namespace SmsRecipes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConsentsController : ControllerBase
{
    private readonly IConsentService _consentService;
    private readonly IConsentPdfService _consentPdfService;
    private readonly IConfiguration _configuration;

    public ConsentsController(IConsentService consentService, IConsentPdfService consentPdfService, IConfiguration configuration)
    {
        _consentService = consentService;
        _consentPdfService = consentPdfService;
        _configuration = configuration;
    }

    [HttpPost("list")]
    public async Task<IActionResult> GetConsents([FromBody] ConsentFilterRequest filter)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var result = await _consentService.GetConsentsAsync(userGuid, filter);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateConsent([FromBody] CreateConsentRequest request)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PatientSnils))
            return BadRequest(new { message = "СНИЛС пациента обязателен" });

        if (string.IsNullOrWhiteSpace(request.PatientName))
            return BadRequest(new { message = "ФИО пациента обязательно" });

        var id = await _consentService.CreateConsentAsync(userGuid, request);
        return Ok(new { id });
    }

    [HttpGet("patients/lookup")]
    public async Task<IActionResult> LookupPatient([FromQuery] string snils)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(snils))
            return BadRequest(new { message = "Укажите СНИЛС" });

        var patient = await _consentService.LookupPatientBySnilsAsync(snils);
        if (patient == null)
            return NotFound(new { message = "Пациент с указанным СНИЛС не найден" });

        return Ok(patient);
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> RevokeConsent(int id)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var revoked = await _consentService.RevokeConsentAsync(userGuid, id);
        if (!revoked)
            return NotFound(new { message = "Активное согласие не найдено или уже отозвано" });

        return Ok(new { message = "Согласие отозвано" });
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetConsentPdf(int id)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var consent = await _consentService.GetConsentByIdAsync(userGuid, id);
        if (consent == null)
            return NotFound(new { message = "Согласие не найдено" });

        var organizationName = _configuration["Organization:Name"];
        var pdf = _consentPdfService.GenerateConsentForm(consent, organizationName);

        return File(pdf, "application/pdf", $"Согласие_{consent.PatientSnils}_{consent.CreatedAt:yyyyMMdd_HHmmss}.pdf");
    }

    private Guid GetUserGuid()
    {
        var claim = User.FindFirst("UserGuid")?.Value;
        return claim != null && Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}

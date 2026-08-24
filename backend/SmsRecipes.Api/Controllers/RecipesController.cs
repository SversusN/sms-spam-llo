using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsRecipes.Api.Dtos;
using SmsRecipes.Api.Services;

namespace SmsRecipes.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecipesController : ControllerBase
{
    private readonly IRecipeService _recipeService;

    public RecipesController(IRecipeService recipeService)
    {
        _recipeService = recipeService;
    }

    [HttpPost("list")]
    public async Task<IActionResult> GetRecipes([FromBody] RecipeFilterRequest filter)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var result = await _recipeService.GetRecipesAsync(userGuid, filter);
        return Ok(result);
    }

    [HttpGet("pharmacies")]
    public async Task<IActionResult> GetUserPharmacies()
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var pharmacies = await _recipeService.GetUserPharmaciesAsync(userGuid);
        return Ok(pharmacies);
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportRecipes([FromBody] RecipeFilterRequest filter)
    {
        var userGuid = GetUserGuid();
        if (userGuid == Guid.Empty)
            return Unauthorized();

        var recipes = await _recipeService.ExportRecipesAsync(userGuid, filter);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Рецепты");

        // Header
        var headers = new[]
        {
            "ID", "Аптека", "Лекарство", "Дата поступления", "Дата/номер рецепта",
            "Срок действия до", "Пациент", "Телефон", "СНИЛС", "Программа",
            "Дозировка", "Кол-во", "Статус SMS", "Дата продажи", "SMS дата"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
            worksheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // Data
        for (int i = 0; i < recipes.Count; i++)
        {
            var r = recipes[i];
            var row = i + 2;
            worksheet.Cell(row, 1).Value = r.RecipeId;
            worksheet.Cell(row, 2).Value = r.ApName;
            worksheet.Cell(row, 3).Value = r.LsName;
            worksheet.Cell(row, 4).Value = r.IncomeDate?.ToString("dd.MM.yyyy");
            worksheet.Cell(row, 5).Value = r.DateNumberRecipe;
            worksheet.Cell(row, 6).Value = r.DateIssueEnd?.ToString("dd.MM.yyyy");
            worksheet.Cell(row, 7).Value = r.PatientName;
            worksheet.Cell(row, 8).Value = r.PatientPhone;
            worksheet.Cell(row, 9).Value = r.IndividualSnils;
            worksheet.Cell(row, 10).Value = r.Program;
            worksheet.Cell(row, 11).Value = r.Dosage;
            worksheet.Cell(row, 12).Value = r.Quantity;
            worksheet.Cell(row, 13).Value = r.SmsStatus ?? "Не отправлено";
            worksheet.Cell(row, 14).Value = r.SaleDate?.ToString("dd.MM.yyyy");
            worksheet.Cell(row, 15).Value = r.SmsDate?.ToString("dd.MM.yyyy HH:mm");
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Рецепты_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private Guid GetUserGuid()
    {
        var claim = User.FindFirst("UserGuid")?.Value;
        return claim != null && Guid.TryParse(claim, out var guid) ? guid : Guid.Empty;
    }
}

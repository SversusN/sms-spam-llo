using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using SmsRecipes.Api.Dtos;

namespace SmsRecipes.Api.Services;

public interface IConsentPdfService
{
    byte[] GenerateConsentForm(ConsentDto consent, string? organizationName = null);
}

public class ConsentPdfService : IConsentPdfService
{
    public byte[] GenerateConsentForm(ConsentDto consent, string? organizationName = null)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Согласие на обработку персональных данных";
        document.Info.Author = organizationName ?? "Медицинская организация";

        var page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);

        var regularFont = new XFont("Arial", 11, XFontStyle.Regular);
        var boldFont = new XFont("Arial", 12, XFontStyle.Bold);
        var titleFont = new XFont("Arial", 14, XFontStyle.Bold);
        var smallFont = new XFont("Arial", 9, XFontStyle.Regular);

        double margin = 50;
        double y = margin;
        double pageWidth = page.Width.Point - 2 * margin;
        double lineHeight = 18;

        void DrawText(string text, XFont font, double? yPos = null)
        {
            gfx.DrawString(text, font, XBrushes.Black, new XRect(margin, yPos ?? y, pageWidth, lineHeight), XStringFormats.TopLeft);
            y += lineHeight;
        }

        void DrawWrappedText(string text, XFont font)
        {
            var words = text.Split(' ');
            var line = new System.Text.StringBuilder();
            foreach (var word in words)
            {
                var test = line.Length > 0 ? line + " " + word : word;
                var size = gfx.MeasureString(test, font);
                if (size.Width > pageWidth && line.Length > 0)
                {
                    DrawText(line.ToString(), font);
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0)
                DrawText(line.ToString(), font);
            y += lineHeight / 2;
        }

        DrawText("СОГЛАСИЕ", titleFont);
        DrawText("на обработку персональных данных", boldFont);
        y += lineHeight;

        var patientInfo = $"ФИО: {consent.PatientName}";
        if (consent.BirthDate.HasValue)
            patientInfo += $", дата рождения: {consent.BirthDate.Value:dd.MM.yyyy}";
        if (!string.IsNullOrWhiteSpace(consent.PatientSnils))
            patientInfo += $", СНИЛС: {FormatSnils(consent.PatientSnils)}";
        if (!string.IsNullOrWhiteSpace(consent.Phone))
            patientInfo += $", телефон: {consent.Phone}";

        DrawWrappedText(patientInfo, regularFont);

        DrawWrappedText(
            "Я, указанное выше лицо, даю свое согласие " + (organizationName ?? "медицинской организации") +
            " на обработку моих персональных данных, включая сбор, запись, систематизацию, накопление, хранение, " +
            "уточнение (обновление, изменение), извлечение, использование, передачу (предоставление, доступ), " +
            "обезличивание, блокирование, удаление, уничтожение персональных данных, в том числе с использованием " +
            "информационных (автоматизированных) систем.",
            regularFont);

        DrawWrappedText(
            "Согласие дано мной на обработку следующих персональных данных: фамилия, имя, отчество, дата рождения, " +
            "СНИЛС, контактный телефон, сведения о состоянии здоровья и медицинском обслуживании в объеме, " +
            "необходимом для оказания медицинской помощи.",
            regularFont);

        DrawWrappedText(
            "Я осведомлен(а) о том, что могу отозвать настоящее согласие путем письменного обращения в " +
            (organizationName ?? "медицинскую организацию") + ".",
            regularFont);

        y += lineHeight;

        DrawText($"Согласие получено: {(consent.IsConsentGiven ? "ДА" : "НЕТ")}", boldFont);
        DrawText($"Дата оформления: {consent.CreatedAt:dd.MM.yyyy HH:mm}", regularFont);
        DrawText($"Вид согласия: {consent.ConsentType ?? "Обработка персональных данных"}", regularFont);

        y += lineHeight * 2;

        DrawText("___________________ / ___________________ /                 _______________", regularFont);
        DrawText("        (подпись)              (ФИО)                                 (дата)", smallFont);

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static string FormatSnils(string digits)
    {
        var numbers = new string(digits.Where(char.IsDigit).ToArray()).PadRight(11, '0');
        return $"{numbers[..3]}-{numbers[3..6]}-{numbers[6..9]} {numbers[9..11]}";
    }
}

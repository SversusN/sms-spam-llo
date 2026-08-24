namespace SmsRecipes.Api.Options;

public class SmsTemplateOptions
{
    public const string SectionName = "SmsTemplate";

    public string Text { get; set; } = "В {PharmacyName} тел {PharmacyPhone} поступил препарат по рец.{RecipeNumber}";
}

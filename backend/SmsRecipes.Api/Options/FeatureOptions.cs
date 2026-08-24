namespace SmsRecipes.Api.Options;

public class FeatureOptions
{
    public const string SectionName = "Features";

    public bool RequireMailingConsent { get; set; } = true;
}

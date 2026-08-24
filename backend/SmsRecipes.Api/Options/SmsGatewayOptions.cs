namespace SmsRecipes.Api.Options;

public class SmsGatewayOptions
{
    public const string SectionName = "SmsGateway";

    public string BaseUrl { get; set; } = "https://bsms.t2.ru/api/";
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Shortcode { get; set; } = string.Empty;
    public bool UseShortcode { get; set; } = true;
    public bool UseRealGateway { get; set; } = true;
}

namespace Metrics.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string PlaceholderPrefix = "CHANGE_ME";
    public const int MinKeyLength = 32;

    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "metrics-api";
    public string Audience { get; set; } = "metrics-web";
    public int ExpiryHours { get; set; } = 8;

    public bool KeyIsPlaceholder => Key.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);
    public bool KeyIsTooShort => Key.Length < MinKeyLength;
}

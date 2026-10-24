namespace Cropwell.Models;

public sealed class AppSettings
{
    public string ActiveProvider { get; set; } = "openai";
    public Dictionary<string, ProviderSettings> Providers { get; set; } = ProviderDefaults.Create();
    public WindowSettings Window { get; set; } = new();
}

public sealed class ProviderSettings
{
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "";
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string EnvironmentVariable { get; set; } = "";
    public int MaxOutputTokens { get; set; }
    public string ReasoningEffort { get; set; } = "";

    public ProviderSettings Clone() => (ProviderSettings)MemberwiseClone();
}

public sealed class WindowSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 760;
}

public static class ProviderDefaults
{
    public static Dictionary<string, ProviderSettings> Create()
    {
        var result = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic"] = New("Claude", "anthropic", "claude-opus-5-5", "ANTHROPIC_API_KEY", 1024, "https://api.anthropic.com"),
            ["openai"] = New("ChatGPT", "openai_responses", "gpt-6-sol", "OPENAI_API_KEY", 4000, "https://api.openai.com", "low"),
            ["gemini"] = New("Gemini", "gemini", "gemini-3.8-flash", "GEMINI_API_KEY", 4000, "https://generativelanguage.googleapis.com", "low"),
            ["muse"] = New("Muse Spark", "openai_responses", "muse-spark-1.3", "META_API_KEY", 16000, "https://api.meta.ai/v1")
        };
        if (Environment.GetEnvironmentVariable("CROPWELL_MOCK") == "1")
            result["mock"] = New("Mock", "mock", "mock-v1", "", 0);
        return result;
    }

    private static ProviderSettings New(string name, string type, string model, string env, int tokens, string url = "", string reasoning = "") =>
        new() { DisplayName = name, Type = type, Model = model, EnvironmentVariable = env, MaxOutputTokens = tokens, BaseUrl = url, ReasoningEffort = reasoning };
}

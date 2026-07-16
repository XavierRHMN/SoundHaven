using System;

namespace SoundHaven.Helpers;

public static class ApiKeyHelper
{
    private const string ApiKeyEnvironmentVariable = "LASTFM_API_KEY";
    private const string ApiSecretEnvironmentVariable = "LASTFM_API_SECRET";

    public static string GetApiKey() => GetEnvironmentValue(ApiKeyEnvironmentVariable);

    public static string GetApiSecret() => GetEnvironmentValue(ApiSecretEnvironmentVariable);

    public static bool IsLastFmConfigured()
    {
        return !string.IsNullOrWhiteSpace(GetApiKey())
            && !string.IsNullOrWhiteSpace(GetApiSecret());
    }

    private static string GetEnvironmentValue(string variableName) =>
        Environment.GetEnvironmentVariable(variableName)?.Trim() ?? string.Empty;
}

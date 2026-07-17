using System;

namespace SoundHaven.Helpers;

public static class ApiKeyHelper
{
    private const string ApiKeyEnvironmentVariable = "LASTFM_API_KEY";
    private const string ApiSecretEnvironmentVariable = "LASTFM_API_SECRET";

    // Shipped so every install can reach Last.fm out of the box. The shared
    // secret only signs requests on behalf of a user who has approved their own
    // session, so exposing it here carries no account risk (the standard model
    // for open-source scrobblers). An env var overrides it for local dev.
    private const string DefaultLastFmApiKey = "b7c604d1b68c78a71b6f879876cccc64";
    private const string DefaultLastFmApiSecret = "a0d384b9c353e23417c907e0924658b3";

    public static string GetApiKey() =>
        ValueOrDefault(ApiKeyEnvironmentVariable, DefaultLastFmApiKey);

    public static string GetApiSecret() =>
        ValueOrDefault(ApiSecretEnvironmentVariable, DefaultLastFmApiSecret);

    public static bool IsLastFmConfigured()
    {
        return !string.IsNullOrWhiteSpace(GetApiKey())
            && !string.IsNullOrWhiteSpace(GetApiSecret());
    }

    // Environment variable if set (local dev override), otherwise the shipped default.
    private static string ValueOrDefault(string variableName, string fallback)
    {
        string fromEnvironment = GetEnvironmentValue(variableName);
        return fromEnvironment.Length > 0 ? fromEnvironment : fallback;
    }

    // Checks user/machine scope too, so freshly-set variables work without
    // relaunching the parent shell.
    private static string GetEnvironmentValue(string variableName)
    {
        foreach (EnvironmentVariableTarget target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            string? value = Environment.GetEnvironmentVariable(variableName, target)?.Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}

using System;


public static class ApiKeyHelper
{
    public static string GetApiKey()
    {
        string? key = Environment.GetEnvironmentVariable("LASTFM_API_KEY");
        if (key == null) return "Key";
        return key;
    }

    public static string GetApiSecret()
    {
        string? secret = Environment.GetEnvironmentVariable("LASTFM_API_SECRET");
        if (secret == null) return "Secret";
        return secret;
    }
}

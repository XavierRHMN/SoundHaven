using System;


public static class ApiKeyHelper
{
    public static string GetApiKey()
    {
        return Environment.GetEnvironmentVariable("LASTFM_API_KEY") 
            ?? throw new InvalidOperationException("LASTFM_API_KEY environment variable not found.");
    }

    public static string GetApiSecret()
    {
        return Environment.GetEnvironmentVariable("LASTFM_API_SECRET") 
            ?? throw new InvalidOperationException("LASTFM_API_SECRET environment variable not found.");
    }
}

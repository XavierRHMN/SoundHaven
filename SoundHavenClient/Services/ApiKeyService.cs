using System;


public class ApiKeyService
{
    public string GetApiKey()
    {
        return Environment.GetEnvironmentVariable("LASTFM_API_KEY") 
            ?? throw new InvalidOperationException("LASTFM_API_KEY environment variable not found.");
    }

    public string GetApiSecret()
    {
        return Environment.GetEnvironmentVariable("LASTFM_API_SECRET") 
            ?? throw new InvalidOperationException("LASTFM_API_SECRET environment variable not found.");
    }
}

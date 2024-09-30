using System;
using System.IO;

namespace SoundHaven.Services
{
    public interface IApiKeyProvider
    {
        string GetApiKey(string fileName);
    }


    public class ApiKeyService : IApiKeyProvider
    {
        public string GetApiKey(string fileName)
        {
            var _filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ApiKeys", fileName);

            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException($"API key file not found at {_filePath}");
            }

            string apiKey = File.ReadAllText(_filePath).Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("API key is empty.");
            }

            return apiKey;
        }
    }
}

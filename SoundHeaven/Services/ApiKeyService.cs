using System;
using System.IO;

namespace SoundHeaven.Services
{
    public interface IApiKeyProvider
    {
        string GetApiKey();
    }

    public class ApiKeyService : IApiKeyProvider
    {
        private readonly string _filePath;

        public ApiKeyService(string fileName = "API_KEY.txt")
        {
            // Combine the base directory with the file name to get the full path
            _filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", fileName);
        }

        public string GetApiKey()
        {
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

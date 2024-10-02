using System;
using System.IO;

namespace SoundHaven.Services
{
    public class ApiKeyService
    {
        public string GetApiKey(string fileName)
        {
            string _filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ApiKeys", fileName);

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

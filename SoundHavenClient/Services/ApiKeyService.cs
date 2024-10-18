using System;
using System.IO;
using System.Linq;

namespace SoundHaven.Services
{
    public class ApiKeyService
    {
        private string GetApiInfo(string fileName, int lineNumber)
        {
            string _filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "ApiKeys", fileName);

            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException($"API info file not found at {_filePath}");
            }

            string[] lines = File.ReadAllLines(_filePath);

            if (lines.Length < lineNumber)
            {
                throw new InvalidOperationException($"File does not contain line {lineNumber}.");
            }

            string info = lines[lineNumber - 1].Trim();

            if (string.IsNullOrEmpty(info))
            {
                throw new InvalidOperationException($"API info on line {lineNumber} is empty.");
            }

            return info;
        }

        public string GetApiKey(string fileName)
        {
            return GetApiInfo(fileName, 1);
        }

        public string GetApiSecret(string fileName)
        {
            return GetApiInfo(fileName, 2);
        }
    }
}

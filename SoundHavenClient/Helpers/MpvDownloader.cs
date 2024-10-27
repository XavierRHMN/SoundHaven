using System;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

public class MpvDownloader
{
    private readonly string baseDir;
    private readonly HttpClient httpClient;

    public MpvDownloader()
    {
        baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory));
        httpClient = new HttpClient();
    }

    public async Task DownloadAndUpdateMpvAsync()
    {
        try
        {
            VerifySevenZipExists();
            if (VerifyMpvExists()) return;

            string mpvUrl = await GetLatestMpvReleaseUrlAsync();
            string mpvArchive = await DownloadMpvAsync(mpvUrl);
            await Task.Delay(1000); // Short delay to ensure file is released

            // Start extraction in a separate task
            var extractionTask = Task.Run(() => ExtractMpvExe(mpvArchive));

            // Wait for extraction to complete
            await extractionTask;

            VerifyMpvExtraction();
            CleanUp(mpvArchive);

            Console.WriteLine("mpv.exe has been successfully updated in the mpv folder.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.Message}");
        }
    }

    private bool VerifyMpvExists()
    {
        string mpvExePath = Path.Combine(baseDir, "mpv.exe");
        if (File.Exists(mpvExePath))
        {
            Console.WriteLine("mpv.exe already exists. Skipping download and extraction.");
            return true;
        }
        return false;
    }

    private void VerifySevenZipExists()
    {
        if (!File.Exists(Path.Combine(baseDir, "7zr.exe")))
        {
            throw new FileNotFoundException($"7zr.exe not found at {baseDir}. Please ensure it's in the correct location.");
        }
    }

    private async Task<string> GetLatestMpvReleaseUrlAsync()
    {
        string releasesUrl = "https://sourceforge.net/projects/mpv-player-windows/rss?path=/64bit";
        string rssContent = await httpClient.GetStringAsync(releasesUrl);
        XDocument doc = XDocument.Parse(rssContent);
        string latestRelease = doc.Descendants("item").First().Element("link").Value;
        return "https://download.sourceforge.net/mpv-player-windows/" + 
               Uri.UnescapeDataString(latestRelease.Split('/')[^2]);
    }

    private async Task<string> DownloadMpvAsync(string mpvUrl)
    {
        string mpvArchive = Path.Combine(baseDir, "mpv.7z");
        Console.WriteLine($"Downloading {mpvUrl}");
        using (var response = await httpClient.GetAsync(mpvUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            using (var streamToReadFrom = await response.Content.ReadAsStreamAsync())
            using (var streamToWriteTo = File.Open(mpvArchive, FileMode.Create))
            {
                await streamToReadFrom.CopyToAsync(streamToWriteTo);
            }
        }
        return mpvArchive;
    }

    private void ExtractMpvExe(string archivePath)
    {
        Console.WriteLine($"Extracting mpv.exe from {archivePath} to {baseDir}");
        string _7zipPath = Path.Combine(baseDir, "7zr.exe");
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _7zipPath,
            Arguments = $"e -y \"-o{baseDir}\" {archivePath} mpv.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (Process process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception($"7-Zip extraction failed with exit code {process.ExitCode}. Output: {output}. Error: {error}");
            }
        }
    }

    private void VerifyMpvExtraction()
    {
        string mpvExePath = Path.Combine(baseDir, "mpv.exe");
        if (!File.Exists(mpvExePath))
        {
            throw new Exception($"MPV extraction failed. mpv.exe not found in {baseDir}");
        }
    }

    private void CleanUp(string mpvArchive)
    {
        if (File.Exists(mpvArchive))
        {
            File.Delete(mpvArchive);
            Console.WriteLine($"Deleted archive: {mpvArchive}");
        }
    }
}
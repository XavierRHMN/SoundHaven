using System;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using System.Linq;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;

public class FileDownloader
{
    private readonly string baseDir;
    private readonly HttpClient httpClient;

    public FileDownloader()
    {
        baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory));
        httpClient = new HttpClient();
    }

    public async Task DownloadAndUpdateFilesAsync()
    {
        try
        {
            if (VerifyFileExists("mpv.exe")) return;

            string mpvUrl = await GetLatestMpvReleaseUrlAsync();
            string mpvArchive = await DownloadArchiveAsync(mpvUrl);
            await ExtractFileFromArchiveAsync(mpvArchive, "mpv.exe");
            VerifyFileExtraction("mpv.exe");
            CleanUp(mpvArchive);

            if (VerifyFileExists("ffmpeg.exe")) return;

            string ffmpegArchive = Path.Combine(baseDir, "ffmpeg.7z");
            await ExtractFileFromArchiveAsync(ffmpegArchive, "ffmpeg.exe");
            VerifyFileExtraction("ffmpeg.exe");
            CleanUp(ffmpegArchive);

            Console.WriteLine("ffmpeg.exe and mpv.exe have been successfully updated.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private bool VerifyFileExists(string fileName)
    {
        string fileExePath = Path.Combine(baseDir, fileName);
        if (File.Exists(fileExePath))
        {
            Console.WriteLine($"{fileName} already exists. Skipping download and extraction.");
            return true;
        }
        return false;
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

    private async Task<string> DownloadArchiveAsync(string mpvUrl)
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

    private async Task ExtractFileFromArchiveAsync(string archivePath, string fileName)
    {
        Console.WriteLine($"Extracting {fileName} from {archivePath} to {baseDir}");
        
        await Task.Run(() =>
        {
            using (var archive = SevenZipArchive.Open(archivePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => 
                    string.Equals(Path.GetFileName(e.Key), fileName, StringComparison.OrdinalIgnoreCase));
                
                if (entry == null)
                    throw new FileNotFoundException($"File {fileName} not found in archive {archivePath}");

                string destinationPath = Path.Combine(baseDir, fileName);
                using (var entryStream = entry.OpenEntryStream())
                using (var destinationStream = File.Create(destinationPath))
                {
                    entryStream.CopyTo(destinationStream);
                }
            }
        });
    }

    private void VerifyFileExtraction(string fileName)
    {
        string filePath = Path.Combine(baseDir, fileName);
        if (!File.Exists(filePath))
        {
            throw new Exception($"{fileName} extraction failed. {fileName} not found in {baseDir}");
        }
    }

    private void CleanUp(string archive)
    {
        if (File.Exists(archive))
        {
            File.Delete(archive);
            Console.WriteLine($"Deleted archive: {archive}");
        }
    }
}
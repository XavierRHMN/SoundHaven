using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using FFMpegCore;
using FFMpegCore.Enums;
using TagLib;
using TagLib.Id3v2;
using SoundHaven.Models;
using SoundHaven.Helpers;

namespace SoundHaven.Services
{
    public interface IYouTubeDownloadService
    {
        Task<Song> DownloadAudioAsync(string videoId, IProgress<double> progress);
        string CleanVideoId(string videoId);
    }
    
    public class YouTubeDownloadService : IYouTubeDownloadService
    {
        private readonly YoutubeClient _youtubeClient;
        private readonly HttpClient _httpClient;

        public YouTubeDownloadService()
        {
            _youtubeClient = new YoutubeClient();
            _httpClient = new HttpClient();
        }

        public async Task<Song> DownloadAudioAsync(string videoId, IProgress<double> progress)
        {
            var video = await _youtubeClient.Videos.GetAsync(videoId);
            var streamInfo = await GetBestAudioStreamAsync(videoId);

            string tempFilePath = await DownloadAudioStreamAsync(streamInfo, video.Title, progress);
            string outputFilePath = await ConvertToMp3Async(tempFilePath, video.Title);

            await AddMetadataToMp3Async(outputFilePath, video);

            return CreateSongObject(outputFilePath, video);
        }

        private async Task<IStreamInfo> GetBestAudioStreamAsync(string videoId)
        {
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            return streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate()
                ?? throw new Exception("No suitable audio stream found.");
        }

        public async Task<string> DownloadAudioStreamAsync(IStreamInfo streamInfo, string title, IProgress<double> progress)
        {
            string tempFileName = SanitizeFileName($"{title}.{streamInfo.Container}");
            string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

            await _youtubeClient.Videos.Streams.DownloadAsync(streamInfo, tempFilePath, progress);
            Console.WriteLine($"Downloaded audio file: {tempFilePath}");

            return tempFilePath;
        }

        private async Task<string> ConvertToMp3Async(string inputPath, string title)
        {
            string outputFileName = SanitizeFileName($"{title}.mp3");
            string outputFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), outputFileName);

            await FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputFilePath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithAudioBitrate(128))
                .ProcessAsynchronously();

            Console.WriteLine($"Converted to MP3: {outputFilePath}");
            System.IO.File.Delete(inputPath);
            Console.WriteLine($"Deleted temporary file: {inputPath}");

            return outputFilePath;
        }

        private async Task AddMetadataToMp3Async(string filePath, YoutubeExplode.Videos.Video video)
        {
            using var tagFile = TagLib.File.Create(filePath);
            
            string cleanTitle = Mp3ToSongHelper.CleanSongTitle(video.Title, video.Author.ChannelTitle);
            
            tagFile.Tag.Title = cleanTitle;
            tagFile.Tag.Performers = new[] { video.Author.ChannelTitle };
            tagFile.Tag.Album = video.Author.ChannelTitle;
            tagFile.Tag.Year = (uint)video.UploadDate.Year;

            var thumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(thumbnailUrl);
                tagFile.Tag.Pictures = new IPicture[] 
                { 
                    new Picture(new ByteVector(imageBytes))
                    {
                        Type = PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = "image/jpeg"
                    }
                };
            }

            tagFile.Save();
            Console.WriteLine($"Added metadata to MP3 file: {filePath}");
        }

        private Song CreateSongObject(string filePath, YoutubeExplode.Videos.Video video)
        {
            var song = Mp3ToSongHelper.GetSongFromMp3(filePath);
            song.VideoId = video.Id;
            song.ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
            song.ChannelTitle = video.Author.ChannelTitle;
            song.Views = video.Engagement.ViewCount.ToString();
            return song;
        }

        private string SanitizeFileName(string fileName)
        {
            return new string(fileName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray());
        }

        public string CleanVideoId(string videoId)
        {
            // Remove any parameters after '&'
            int ampersandIndex = videoId.IndexOf('&');
            if (ampersandIndex != -1)
            {
                videoId = videoId.Substring(0, ampersandIndex);
            }
            return videoId;
        }
    }
}

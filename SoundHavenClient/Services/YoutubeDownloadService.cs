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
            // Clean up the video ID
            videoId = CleanVideoId(videoId);

            var video = await _youtubeClient.Videos.GetAsync(videoId);
            var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
            var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

            if (streamInfo == null)
            {
                throw new Exception("No suitable audio stream found.");
            }

            string tempFileName = SanitizeFileName($"{video.Title}.{streamInfo.Container}");
            string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

            // Download the audio file
            await _youtubeClient.Videos.Streams.DownloadAsync(streamInfo, tempFilePath, progress);

            Console.WriteLine($"Downloaded audio file: {tempFilePath}");

            // Convert to MP3
            string outputFileName = SanitizeFileName($"{video.Title}.mp3");
            string outputFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), outputFileName);

            await FFMpegArguments
                .FromFileInput(tempFilePath)
                .OutputToFile(outputFilePath, true, options => options
                    .WithAudioCodec(AudioCodec.LibMp3Lame)
                    .WithAudioBitrate(128))
                .ProcessAsynchronously();

            Console.WriteLine($"Converted to MP3: {outputFilePath}");

            // Delete the temporary file
            System.IO.File.Delete(tempFilePath);
            Console.WriteLine($"Deleted temporary file: {tempFilePath}");

            // Add metadata to the MP3 file
            await AddMetadataToMp3(outputFilePath, video);

            // Create a Song object using Mp3ToSongHelper
            var song = Mp3ToSongHelper.GetSongFromMp3(outputFilePath);

            // Add YouTube-specific information
            song.VideoId = videoId;
            song.ThumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
            song.ChannelTitle = video.Author.ChannelTitle;
            song.Views = video.Engagement.ViewCount.ToString();

            return song;
        }

        private async Task AddMetadataToMp3(string filePath, YoutubeExplode.Videos.Video video)
        {
            using (var tagFile = TagLib.File.Create(filePath))
            {
                // Clean the title using Mp3ToSongHelper
                string cleanTitle = Mp3ToSongHelper.CleanSongTitle(video.Title, video.Author.ChannelTitle);

                // Set title and artist
                tagFile.Tag.Title = cleanTitle;
                tagFile.Tag.Performers = new[] { video.Author.ChannelTitle };

                // Set album to channel name
                tagFile.Tag.Album = video.Author.ChannelTitle;
                tagFile.Tag.Year = (uint)video.UploadDate.Year;

                // Download and set thumbnail
                string? thumbnailUrl = video.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url;
                if (!string.IsNullOrEmpty(thumbnailUrl))
                {
                    byte[] imageBytes = await _httpClient.GetByteArrayAsync(thumbnailUrl);
                    var picture = new Picture(new ByteVector(imageBytes))
                    {
                        Type = PictureType.FrontCover,
                        Description = "Cover",
                        MimeType = "image/jpeg"
                    };
                    tagFile.Tag.Pictures = new IPicture[] { picture };
                }

                tagFile.Save();
            }

            Console.WriteLine($"Added metadata to MP3 file: {filePath}");
        }

        private string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());
        }

        public string CleanVideoId(string videoId)
        {
            // Remove any parameters after '&'
            int ampersandIndex = videoId.IndexOf('&');
            if (ampersandIndex != -1)
            {
                videoId = videoId.Substring(0, ampersandIndex);
            }

            // If the ID is a full URL, extract just the ID
            if (videoId.Contains("youtube.com") || videoId.Contains("youtu.be"))
            {
                var uri = new Uri(videoId);
                if (uri.Host == "youtu.be")
                {
                    videoId = uri.AbsolutePath.Trim('/');
                }
                else
                {
                    var query = HttpUtility.ParseQueryString(uri.Query);
                    videoId = query["v"] ?? videoId;
                }
            }

            return videoId;
        }
    }
}

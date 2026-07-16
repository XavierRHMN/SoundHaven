using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SoundHaven.Helpers
{
    public interface IOpenFileDialogService
    {
        Task<string?> ShowOpenFileDialogAsync(Window parent);

        Task<string?> ShowOpenImageDialogAsync(Window parent);
    }


    public class OpenFileDialogService : IOpenFileDialogService
    {
        public Task<string?> ShowOpenFileDialogAsync(Window parent) =>
            ShowOpenDialogAsync(
                parent,
                "Select a Song",
                new FilePickerFileType("Audio files")
                {
                    Patterns =
                    [
                        "*.mp3",
                        "*.wav",
                        "*.flac",
                        "*.m4a",
                        "*.aac",
                        "*.ogg",
                        "*.wma"
                    ]
                });

        public Task<string?> ShowOpenImageDialogAsync(Window parent) =>
            ShowOpenDialogAsync(
                parent,
                "Select a playlist cover",
                new FilePickerFileType("Image files")
                {
                    Patterns =
                    [
                        "*.png",
                        "*.jpg",
                        "*.jpeg",
                        "*.webp",
                        "*.bmp",
                        "*.gif"
                    ]
                });

        private static async Task<string?> ShowOpenDialogAsync(
            Window parent,
            string title,
            FilePickerFileType fileType)
        {
            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [fileType]
            };

            var result = await parent.StorageProvider.OpenFilePickerAsync(options);
            return result.Count > 0 ? result[0].TryGetLocalPath() : null;
        }
    }
}

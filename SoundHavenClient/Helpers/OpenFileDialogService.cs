using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SoundHaven.Helpers
{
    public interface IOpenFileDialogService
    {
        Task<string?> ShowOpenFileDialogAsync(Window parent);
    }


    public class OpenFileDialogService : IOpenFileDialogService
    {
        public async Task<string?> ShowOpenFileDialogAsync(Window parent)
        {
            var options = new FilePickerOpenOptions
            {
                Title = "Select a Song",
                AllowMultiple = false,
                FileTypeFilter =
                [
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
                    }
                ]
            };

            var result = await parent.StorageProvider.OpenFilePickerAsync(options);
            return result.Count > 0 ? result[0].TryGetLocalPath() : null;
        }
    }
}

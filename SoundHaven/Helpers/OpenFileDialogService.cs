using Avalonia.Controls;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            var dialog = new OpenFileDialog
            {
                Title = "Select a Song",
                AllowMultiple = false,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter
                    {
                        Name = "Audio Files",
                        Extensions = new List<string> { "mp3", "wav", "flac", "aac", "ogg", "wma" }
                    }
                }
            };

            string[]? result = await dialog.ShowAsync(parent);
            return result?.Length > 0 ? result[0] : null;
        }
    }
}

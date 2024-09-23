using SoundHeaven.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SoundHeaven.Services
{
    public interface IDataService
    {
        Task<IEnumerable<Song>> GetTopTracksAsync();
        public Task<IEnumerable<Song>> GetRecentlyPlayedTracksAsync(string username);
        Task<IEnumerable<Song>> GetRecommendedTracksAsync();
    }
}

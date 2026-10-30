using System.Collections.Generic;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.TvShows;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Sonarr
{
    public interface ISonarrTvShowLibrary
    {
        Task<IReadOnlyList<TvShow>> GetLibraryTvShowsAsync();
        Task DeleteTvShowAsync(TvShow tvShow);
    }
}

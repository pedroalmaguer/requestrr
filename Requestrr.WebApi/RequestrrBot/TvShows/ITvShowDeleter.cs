using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.TvShows
{
    public interface ITvShowDeleter
    {
        Task<IReadOnlyList<TvShow>> SearchLibraryTvShowsAsync(string tvShowName);
        Task<TvShow> GetLibraryTvShowAsync(string downloadClientId);
        Task DeleteTvShowAsync(TvShow tvShow);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.TvShows
{
    public interface ITvShowDeleter
    {
        Task<IReadOnlyList<TvShow>> GetLibraryTvShowsAsync();
        Task DeleteTvShowAsync(TvShow tvShow);
    }
}

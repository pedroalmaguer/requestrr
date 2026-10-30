using System.Collections.Generic;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.Movies;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr
{
    public interface IRadarrMovieLibrary
    {
        Task<IReadOnlyList<Movie>> GetLibraryMoviesAsync();
        Task DeleteMovieAsync(Movie movie);
    }
}

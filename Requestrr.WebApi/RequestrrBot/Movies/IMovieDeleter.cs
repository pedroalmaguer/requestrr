using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Movies
{
    public interface IMovieDeleter
    {
        Task<IReadOnlyList<Movie>> SearchLibraryMoviesAsync(string movieName);
        Task<Movie> GetLibraryMovieAsync(string downloadClientId);
        Task DeleteMovieAsync(Movie movie);
    }
}

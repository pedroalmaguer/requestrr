using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Movies
{
    public interface IMovieDeleter
    {
        Task<IReadOnlyList<Movie>> GetLibraryMoviesAsync();
        Task DeleteMovieAsync(Movie movie);
    }
}

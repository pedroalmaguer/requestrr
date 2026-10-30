using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Movies
{
    public interface IMovieDeletionUserInterface
    {
        Task WarnNoMovieFoundAsync(string movieName);
        Task WarnNoMovieFoundByTheMovieDbIdAsync(string theMovieDbIdTextValue);
        Task ShowMovieDeletionSelection(IReadOnlyList<Movie> movies);
        Task DisplayMovieDeletionConfirmationAsync(Movie movie);
        Task DisplayMovieDeletionCancelledAsync();
    }
}

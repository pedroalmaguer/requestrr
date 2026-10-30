using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Movies
{
    public class MovieDeletionWorkflow
    {
        private readonly IMovieDeleter _deleter;
        private readonly IMovieDeletionUserInterface _userInterface;

        public MovieDeletionWorkflow(IMovieDeleter deleter, IMovieDeletionUserInterface userInterface)
        {
            _deleter = deleter;
            _userInterface = userInterface;
        }

        public async Task SearchMovieAsync(string movieName)
        {
            movieName = movieName.Replace(".", " ");

            var movies = (await _deleter.GetLibraryMoviesAsync())
                .Where(x => x.Title.Contains(movieName, StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(x => x.Title.Length)
                .ToArray();

            if (!movies.Any())
            {
                await _userInterface.WarnNoMovieFoundAsync(movieName);
            }
            else if (movies.Length == 1)
            {
                await _userInterface.DisplayMovieDeletionConfirmationAsync(movies.Single());
            }
            else
            {
                await _userInterface.ShowMovieDeletionSelection(movies);
            }
        }

        public async Task HandleMovieSelectionAsync(int theMovieDbId)
        {
            var movie = await FindMovieForDeletionAsync(theMovieDbId);

            if (movie != null)
            {
                await _userInterface.DisplayMovieDeletionConfirmationAsync(movie);
            }
        }

        public async Task<Movie> FindMovieForDeletionAsync(int theMovieDbId)
        {
            var movie = await FindMovieAsync(theMovieDbId);

            if (movie == null)
            {
                await _userInterface.WarnNoMovieFoundByTheMovieDbIdAsync(theMovieDbId.ToString());
            }

            return movie;
        }

        public async Task<bool> DeleteMovieAsync(int theMovieDbId)
        {
            var movie = await FindMovieAsync(theMovieDbId);

            if (movie == null)
            {
                return false;
            }

            await _deleter.DeleteMovieAsync(movie);

            return true;
        }

        public Task CancelAsync()
        {
            return _userInterface.DisplayMovieDeletionCancelledAsync();
        }

        private async Task<Movie> FindMovieAsync(int theMovieDbId)
        {
            return (await _deleter.GetLibraryMoviesAsync()).FirstOrDefault(x => x.TheMovieDbId == theMovieDbId.ToString());
        }
    }
}

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

            var movies = (await _deleter.SearchLibraryMoviesAsync(movieName))
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

        public async Task HandleMovieSelectionAsync(string downloadClientId)
        {
            var movie = await FindMovieForDeletionAsync(downloadClientId);

            if (movie != null)
            {
                await _userInterface.DisplayMovieDeletionConfirmationAsync(movie);
            }
        }

        public async Task<Movie> FindMovieForDeletionAsync(string downloadClientId)
        {
            var movie = await FindMovieAsync(downloadClientId);

            if (movie == null)
            {
                await _userInterface.WarnMovieDeletionNoLongerValidAsync();
            }

            return movie;
        }

        public async Task<bool> DeleteMovieAsync(string downloadClientId)
        {
            var movie = await FindMovieAsync(downloadClientId);

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

        private Task<Movie> FindMovieAsync(string downloadClientId)
        {
            return _deleter.GetLibraryMovieAsync(downloadClientId);
        }
    }
}

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

            var movies = (await GetDeletableMoviesAsync())
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

        //The download client id is the only unique key, TheMovieDb ids can repeat or be 0 for unmatched entries
        private async Task<Movie> FindMovieAsync(string downloadClientId)
        {
            return (await GetDeletableMoviesAsync()).FirstOrDefault(x => x.DownloadClientId == downloadClientId);
        }

        private async Task<Movie[]> GetDeletableMoviesAsync()
        {
            return (await _deleter.GetLibraryMoviesAsync()).Where(x => !string.IsNullOrWhiteSpace(x.DownloadClientId)).ToArray();
        }
    }
}

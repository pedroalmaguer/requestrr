using System;
using System.Linq;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.TvShows
{
    public class TvShowDeletionWorkflow
    {
        private readonly ITvShowDeleter _deleter;
        private readonly ITvShowDeletionUserInterface _userInterface;

        public TvShowDeletionWorkflow(ITvShowDeleter deleter, ITvShowDeletionUserInterface userInterface)
        {
            _deleter = deleter;
            _userInterface = userInterface;
        }

        public async Task SearchTvShowAsync(string tvShowName)
        {
            tvShowName = tvShowName.Replace(".", " ");

            var tvShows = (await _deleter.SearchLibraryTvShowsAsync(tvShowName))
                .OrderBy(x => x.Title.Length)
                .ToArray();

            if (!tvShows.Any())
            {
                await _userInterface.WarnNoTvShowFoundAsync(tvShowName);
            }
            else if (tvShows.Length == 1)
            {
                await _userInterface.DisplayTvShowDeletionConfirmationAsync(tvShows.Single());
            }
            else
            {
                await _userInterface.ShowTvShowDeletionSelection(tvShows);
            }
        }

        public async Task HandleTvShowSelectionAsync(string downloadClientId)
        {
            var tvShow = await FindTvShowForDeletionAsync(downloadClientId);

            if (tvShow != null)
            {
                await _userInterface.DisplayTvShowDeletionConfirmationAsync(tvShow);
            }
        }

        public async Task<TvShow> FindTvShowForDeletionAsync(string downloadClientId)
        {
            var tvShow = await FindTvShowAsync(downloadClientId);

            if (tvShow == null)
            {
                await _userInterface.WarnTvShowDeletionNoLongerValidAsync();
            }

            return tvShow;
        }

        public async Task<bool> DeleteTvShowAsync(string downloadClientId)
        {
            var tvShow = await FindTvShowAsync(downloadClientId);

            if (tvShow == null)
            {
                return false;
            }

            await _deleter.DeleteTvShowAsync(tvShow);

            return true;
        }

        public Task CancelAsync()
        {
            return _userInterface.DisplayTvShowDeletionCancelledAsync();
        }

        private Task<TvShow> FindTvShowAsync(string downloadClientId)
        {
            return _deleter.GetLibraryTvShowAsync(downloadClientId);
        }
    }
}

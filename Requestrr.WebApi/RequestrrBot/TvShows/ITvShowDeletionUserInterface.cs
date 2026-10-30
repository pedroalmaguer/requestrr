using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.TvShows
{
    public interface ITvShowDeletionUserInterface
    {
        Task WarnNoTvShowFoundAsync(string tvShowName);
        Task WarnTvShowDeletionNoLongerValidAsync();
        Task ShowTvShowDeletionSelection(IReadOnlyList<TvShow> tvShows);
        Task DisplayTvShowDeletionConfirmationAsync(TvShow tvShow);
        Task DisplayTvShowDeletionCancelledAsync();
    }
}

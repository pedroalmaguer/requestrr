using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Movies;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr
{
    public class RadarrClient : IMovieRequester, IMovieSearcher, IMovieDeleter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RadarrClient> _logger;
        private readonly RadarrSettingsProvider _settingsProvider;

        public RadarrClient(IHttpClientFactory httpClientFactory, ILogger<RadarrClient> logger, RadarrSettingsProvider RadarrSettingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _settingsProvider = RadarrSettingsProvider;
        }

        public static Task TestConnectionAsync(HttpClient httpClient, ILogger<RadarrClient> logger, RadarrSettings settings)
        {
            if (settings.Version == "2")
            {
                return RadarrClientV2.TestConnectionAsync(httpClient, logger, settings);
            }
            else
            {
                return RadarrClientV3.TestConnectionAsync(httpClient, logger, settings);
            }
        }

        public static Task<IList<JSONRootPath>> GetRootPaths(HttpClient httpClient, ILogger<RadarrClient> logger, RadarrSettings settings)
        {
            if (settings.Version == "2")
            {
                return RadarrClientV2.GetRootPaths(httpClient, logger, settings);
            }
            else
            {
                return RadarrClientV3.GetRootPaths(httpClient, logger, settings);
            }
        }

        public static Task<IList<JSONProfile>> GetProfiles(HttpClient httpClient, ILogger<RadarrClient> logger, RadarrSettings settings)
        {
            if (settings.Version == "2")
            {
                return RadarrClientV2.GetProfiles(httpClient, logger, settings);
            }
            else
            {
                return RadarrClientV3.GetProfiles(httpClient, logger, settings);
            }
        }

        public static Task<IList<JSONTag>> GetTags(HttpClient httpClient, ILogger<RadarrClient> logger, RadarrSettings settings)
        {
            if (settings.Version == "2")
            {
                return Task.FromResult((IList<JSONTag>)new List<JSONTag>());
            }
            else
            {
                return RadarrClientV3.GetTags(httpClient, logger, settings);
            }
        }

        public Task<Movie> SearchMovieAsync(MovieRequest request, int theMovieDbId)
        {
            return CreateInstance<IMovieSearcher>().SearchMovieAsync(request, theMovieDbId);
        }

        public Task<IReadOnlyList<Movie>> SearchMovieAsync(MovieRequest request, string movieName)
        {
            return CreateInstance<IMovieSearcher>().SearchMovieAsync(request, movieName);
        }

        public Task<MovieDetails> GetMovieDetails(MovieRequest request, string theMovieDbId)
        {
            return CreateInstance<IMovieSearcher>().GetMovieDetails(request, theMovieDbId);
        }

        public Task<Dictionary<int, Movie>> SearchAvailableMoviesAsync(HashSet<int> theMovieDbIds, System.Threading.CancellationToken token)
        {
            return CreateInstance<IMovieSearcher>().SearchAvailableMoviesAsync(theMovieDbIds, token);
        }

        public Task<MovieRequestResult> RequestMovieAsync(MovieRequest request, Movie movie)
        {
            return CreateInstance<IMovieRequester>().RequestMovieAsync(request, movie);
        }

        public async Task<IReadOnlyList<Movie>> SearchLibraryMoviesAsync(string movieName)
        {
            return (await GetDeletableMoviesAsync()).Where(x => x.Title.Contains(movieName, StringComparison.InvariantCultureIgnoreCase)).ToArray();
        }

        //The Radarr id is the only unique key, TheMovieDb ids can repeat or be 0 for unmatched entries
        public async Task<Movie> GetLibraryMovieAsync(string downloadClientId)
        {
            return (await GetDeletableMoviesAsync()).FirstOrDefault(x => x.DownloadClientId == downloadClientId);
        }

        public Task DeleteMovieAsync(Movie movie)
        {
            return CreateInstance<IRadarrMovieLibrary>().DeleteMovieAsync(movie);
        }

        private async Task<Movie[]> GetDeletableMoviesAsync()
        {
            return (await CreateInstance<IRadarrMovieLibrary>().GetLibraryMoviesAsync()).Where(x => !string.IsNullOrWhiteSpace(x.DownloadClientId)).ToArray();
        }

        private T CreateInstance<T>() where T : class
        {
            if (_settingsProvider.Provide().Version == "2")
            {
                return new RadarrClientV2(_httpClientFactory, _logger, _settingsProvider) as T;
            }
            else
            {
                return new RadarrClientV3(_httpClientFactory, _logger, _settingsProvider) as T;
            }
        }

        public class JSONRootPath
        {
            public string path { get; set; }
            public int id { get; set; }
        }

        public class JSONProfile
        {
            public string name { get; set; }
            public int id { get; set; }
        }

        public class JSONTag
        {
            public string label { get; set; }
            public int id { get; set; }
        }
    }
}
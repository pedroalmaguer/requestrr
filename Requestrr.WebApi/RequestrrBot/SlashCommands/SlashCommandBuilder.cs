using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Lidarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Ombi;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Overseerr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Sonarr;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot
{
    public static class SlashCommandBuilder
    {
        public enum CommandType
        {
            Misc, Movie, Tv, IssueMovie, IssueTv, Music
        }

        private static Dictionary<CommandType, List<string>> _commandList = new Dictionary<CommandType, List<string>>();

        private const string _tempFolderName = "tmp";

        public static string DLLFileName = "slashcommandsbuilder";
        public static string TempFolder { get => Path.Combine(SettingsFile.SettingsFolder, _tempFolderName); }

        public static Dictionary<CommandType, List<string>> CommandList { get => _commandList; }

        public static Type Build(ILogger logger, DiscordSettings settings, RadarrSettingsProvider radarrSettingsProvider, SonarrSettingsProvider sonarrSettingsProvider, OverseerrSettingsProvider overseerrSettingsProvider, OmbiSettingsProvider ombiSettingsProvider, LidarrSettingsProvider lidarrSettingsProvider)
        {
            string code = GetCode(settings, radarrSettingsProvider.Provide(), sonarrSettingsProvider.Provide(), overseerrSettingsProvider.Provide(), ombiSettingsProvider.Provide(), lidarrSettingsProvider.Provider());
            var tree = SyntaxFactory.ParseSyntaxTree(code);
            string fileName = $"{DLLFileName}-{Guid.NewGuid()}.dll";

            var references = new List<PortableExecutableReference>()
            {
              MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(ApplicationCommandModule).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(SlashCommandBuilder).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(Attribute).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(Task).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(DiscordUser).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(IServiceProvider).GetTypeInfo().Assembly.Location),
              MetadataReference.CreateFromFile(typeof(ILogger).GetTypeInfo().Assembly.Location),
            };

            references.Add(MetadataReference.CreateFromFile(AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "netstandard").Location));

            Assembly.GetEntryAssembly().GetReferencedAssemblies().ToList()
                       .ForEach(a => references.Add(MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

            var compilation = CSharpCompilation.Create(fileName)
              .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
              .AddReferences(references)
              .AddSyntaxTrees(tree);

            string tmpDirectory = string.Empty;
            var dirInfo = new DirectoryInfo(Directory.GetCurrentDirectory());

            if (!Directory.Exists(TempFolder))
            {
                Console.WriteLine("No temp folder found, creating one...");
                Directory.CreateDirectory(TempFolder);
            }

            string path = Path.Combine(TempFolder, fileName);

            var compilationResult = compilation.Emit(path);
            if (compilationResult.Success)
            {
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                return asm.GetType("Requestrr.WebApi.RequestrrBot.SlashCommands");
            }
            else
            {
                foreach (Diagnostic codeIssue in compilationResult.Diagnostics)
                {
                    string issue = $"ID: {codeIssue.Id}, Message: {codeIssue.GetMessage()}, Location: {codeIssue.Location.GetLineSpan()},Severity: {codeIssue.Severity}";
                    logger.LogError("Failed to build SlashCommands assembly: " + issue);
                }
            }

            throw new Exception("Failed to build SlashCommands assembly.");
        }

        private static string GetCode(DiscordSettings settings, RadarrSettings radarrSettings, SonarrSettings sonarrSettings, OverseerrSettings overseerrSettings, OmbiSettings ombiSettings, LidarrSettings lidarrSettings)
        {
            //Build a list of commands being created
            _commandList = new Dictionary<CommandType, List<string>>
            {
                { CommandType.Movie, new List<string>() },
                { CommandType.Tv, new List<string>() },
                { CommandType.IssueMovie, new List<string>() },
                { CommandType.IssueTv, new List<string>() },
                { CommandType.Music, new List<string>() },
                { CommandType.Misc, new List<string>() }
            };
            var code = File.ReadAllText(Program.CombindPath("SlashCommands.txt"));

            code = code.Replace("[REQUEST_GROUP_NAME]", Language.Current.DiscordCommandRequestGroupName);
            code = code.Replace("[REQUEST_GROUP_DESCRIPTION]", Language.Current.DiscordCommandRequestGroupDescription);

            code = code.Replace("[REQUEST_MOVIE_TITLE_DESCRIPTION]", Language.Current.DiscordCommandMovieRequestTitleDescription);
            code = code.Replace("[REQUEST_MOVIE_TITLE_OPTION_NAME]", Language.Current.DiscordCommandMovieRequestTitleOptionName);
            code = code.Replace("[REQUEST_MOVIE_TITLE_OPTION_DESCRIPTION]", Language.Current.DiscordCommandMovieRequestTitleOptionDescription);
            code = code.Replace("[REQUEST_MOVIE_TMDB_DESCRIPTION]", Language.Current.DiscordCommandMovieRequestTmbdDescription);
            code = code.Replace("[REQUEST_MOVIE_TMDB_OPTION_NAME]", Language.Current.DiscordCommandMovieRequestTmbdOptionName);
            code = code.Replace("[REQUEST_MOVIE_TMDB_OPTION_DESCRIPTION]", Language.Current.DiscordCommandMovieRequestTmbdOptionDescription);

            code = code.Replace("[REQUEST_TV_TITLE_DESCRIPTION]", Language.Current.DiscordCommandTvRequestTitleDescription);
            code = code.Replace("[REQUEST_TV_TITLE_OPTION_NAME]", Language.Current.DiscordCommandTvRequestTitleOptionName);
            code = code.Replace("[REQUEST_TV_TITLE_OPTION_DESCRIPTION]", Language.Current.DiscordCommandTvRequestTitleOptionDescription);
            code = code.Replace("[REQUEST_TV_TVDB_DESCRIPTION]", Language.Current.DiscordCommandTvRequestTvdbDescription);
            code = code.Replace("[REQUEST_TV_TVDB_OPTION_NAME]", Language.Current.DiscordCommandTvRequestTvdbOptionName);
            code = code.Replace("[REQUEST_TV_TVDB_OPTION_DESCRIPTION]", Language.Current.DiscordCommandTvRequestTvdbOptionDescription);


            code = code.Replace("[REQUEST_MUSIC_ARTIST_DESCRIPTION]", Language.Current.DiscordCommandMusicRequestArtistDescription);
            code = code.Replace("[REQUEST_MUSIC_ARTIST_OPTION_NAME]", Language.Current.DiscordCommandMusicRequestArtistOptionName);
            code = code.Replace("[REQUEST_MUSIC_ARTIST_OPTION_DESCRIPTION]", Language.Current.DiscordCommandMusicRequestArtistOptionDescription);


            code = code.Replace("[REQUEST_PING_NAME]", Language.Current.DiscordCommandPingRequestName);
            code = code.Replace("[REQUEST_PING_DESCRIPTION]", Language.Current.DiscordCommandPingRequestDescription);
            code = code.Replace("[REQUEST_HELP_NAME]", Language.Current.DiscordCommandHelpRequestName);
            code = code.Replace("[REQUEST_HELP_DESCRIPTION]", Language.Current.DiscordCommandHelpRequestDescription);

            code = code.Replace("[REQUIRED_MOVIE_ROLE_IDS]", string.Join(",", settings.MovieRoles.Select(x => $"{x}UL")));
            code = code.Replace("[REQUIRED_TV_ROLE_IDS]", string.Join(",", settings.TvShowRoles.Select(x => $"{x}UL")));
            code = code.Replace("[REQUIRED_MUSIC_ROLE_IDS]", string.Join(",", settings.MusicRoles.Select(x => $"{x}UL")));
            code = code.Replace("[REQUIRED_CHANNEL_IDS]", string.Join(",", settings.MonitoredChannels.Select(x => $"{x}UL")));


            string request = Language.Current.DiscordCommandRequestGroupName;
            string issue = Language.Current.DiscordCommandIssueName;
            _commandList[CommandType.Misc].Add(Language.Current.DiscordCommandHelpRequestName);
            _commandList[CommandType.Misc].Add(Language.Current.DiscordCommandPingRequestName);

            //Issue command handling
            code = code.Replace("[ISSUE_GROUP_NAME]", Language.Current.DiscordCommandIssueName);
            code = code.Replace("[ISSUE_GROUP_DESCRIPTION]", Language.Current.DiscordCommandIssueDescription);


            code = code.Replace("[ISSUE_MOVIE_TITLE_DESCRIPTION]", Language.Current.DiscordCommandMovieIssueTitleDescription);
            code = code.Replace("[ISSUE_MOVIE_TITLE_OPTION_NAME]", Language.Current.DiscordCommandMovieIssueTitleOptionName);
            code = code.Replace("[ISSUE_MOVIE_TITLE_OPTION_DESCRIPTION]", Language.Current.DiscordCommandMovieIssueTitleOptionDescription);

            code = code.Replace("[ISSUE_MOVIE_TMDB_DESCRIPTION]", Language.Current.DiscordCommandMovieIssueTmdbDescription);
            code = code.Replace("[ISSUE_MOVIE_TMDB_OPTION_NAME]", Language.Current.DiscordCommandMovieIssueTmdbOptionName);
            code = code.Replace("[ISSUE_MOVIE_TMDB_OPTION_DESCRIPTION]", Language.Current.DiscordCommandMovieIssueTmdbOptionDescription);

            code = code.Replace("[ISSUE_TV_TITLE_DESCRIPTION]", Language.Current.DiscordCommandTvIssueTitleDescription);
            code = code.Replace("[ISSUE_TV_TITLE_OPTION_NAME]", Language.Current.DiscordCommandTvIssueTitleOptionName);
            code = code.Replace("[ISSUE_TV_TITLE_OPTION_DESCRIPTION]", Language.Current.DiscordCommandTvIssueTitleOptionDescription);

            code = code.Replace("[ISSUE_TV_TVDB_DESCRIPTION]", Language.Current.DiscordCommandTvIssueTvdbDescription);
            code = code.Replace("[ISSUE_TV_TVDB_OPTION_NAME]", Language.Current.DiscordCommandTvIssueTvdbOptionName);
            code = code.Replace("[ISSUE_TV_TVDB_OPTION_DESCRIPTION]", Language.Current.DiscordCommandTvIssueTvdbOptionDescription);


            if (settings.MovieDownloadClient == DownloadClient.Disabled && settings.TvShowDownloadClient == DownloadClient.Disabled && settings.MusicDownloadClient == DownloadClient.Disabled)
            {
                var beginIndex = code.IndexOf("[REQUEST_COMMAND_START]");
                var endIndex = code.IndexOf("[REQUEST_COMMAND_END]") + "[REQUEST_COMMAND_END]".Length;

                code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                _commandList.Remove(CommandType.Movie);
                _commandList.Remove(CommandType.Tv);
                _commandList.Remove(CommandType.Music);
            }
            else
            {
                if (settings.MovieDownloadClient == DownloadClient.Disabled)
                {
                    var beginIndex = code.IndexOf("[MOVIE_COMMAND_START]");
                    var endIndex = code.IndexOf("[MOVIE_COMMAND_END]") + "[MOVIE_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                    _commandList.Remove(CommandType.Movie);
                }
                else if (settings.MovieDownloadClient == DownloadClient.Radarr)
                {
                    code = GenerateMovieCategories(radarrSettings.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray(), code, _commandList[CommandType.Movie], request, false);
                }
                else if (settings.MovieDownloadClient == DownloadClient.Overseerr && overseerrSettings.Movies.Categories.Any())
                {
                    Category[] categories = overseerrSettings.Movies.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray();
                    code = GenerateMovieCategories(categories, code, _commandList[CommandType.Movie], request, categories.Length == 0);
                }
                else
                {

                    _commandList[CommandType.Movie].Add($"{request} {Language.Current.DiscordCommandMovieRequestTitleName}");
                    _commandList[CommandType.Movie].Add($"{request} {Language.Current.DiscordCommandMovieRequestTmbdName}");

                    code = code.Replace("[REQUEST_MOVIE_TITLE_NAME]", Language.Current.DiscordCommandMovieRequestTitleName);
                    code = code.Replace("[REQUEST_MOVIE_TMDB_NAME]", Language.Current.DiscordCommandMovieRequestTmbdName);
                    code = code.Replace("[MOVIE_COMMAND_START]", string.Empty);
                    code = code.Replace("[MOVIE_COMMAND_END]", string.Empty);
                    code = code.Replace("[TMDB_COMMAND_START]", string.Empty);
                    code = code.Replace("[TMDB_COMMAND_END]", string.Empty);
                    code = code.Replace("[MOVIE_CATEGORY_ID]", "99999");
                }

                if (settings.TvShowDownloadClient == DownloadClient.Disabled)
                {
                    var beginIndex = code.IndexOf("[TV_COMMAND_START]");
                    var endIndex = code.IndexOf("[TV_COMMAND_END]") + "[TV_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                    _commandList.Remove(CommandType.Tv);
                }
                else if (settings.TvShowDownloadClient == DownloadClient.Sonarr)
                {
                    code = GenerateTvShowCategories(sonarrSettings.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray(), code, _commandList[CommandType.Tv], request, false);
                }
                else if (settings.TvShowDownloadClient == DownloadClient.Overseerr && overseerrSettings.TvShows.Categories.Any())
                {
                    Category[] categories = overseerrSettings.TvShows.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray();
                    code = GenerateTvShowCategories(categories, code, _commandList[CommandType.Tv], request, categories.Length == 0);
                }
                else
                {
                    _commandList[CommandType.Tv].Add($"{request} {Language.Current.DiscordCommandTvRequestTitleName}");
                    _commandList[CommandType.Tv].Add($"{request} {Language.Current.DiscordCommandTvRequestTvdbName}");

                    code = code.Replace("[REQUEST_TV_TITLE_NAME]", Language.Current.DiscordCommandTvRequestTitleName);
                    code = code.Replace("[REQUEST_TV_TVDB_NAME]", Language.Current.DiscordCommandTvRequestTvdbName);
                    code = code.Replace("[TV_COMMAND_START]", string.Empty);
                    code = code.Replace("[TV_COMMAND_END]", string.Empty);
                    code = code.Replace("[TVDB_COMMAND_START]", string.Empty);
                    code = code.Replace("[TVDB_COMMAND_END]", string.Empty);
                    code = code.Replace("[TV_CATEGORY_ID]", "99999");
                }

                if (settings.MusicDownloadClient == DownloadClient.Disabled)
                {
                    int beginIndex = code.IndexOf("[MUSIC_COMMAND_START]");
                    int endIndex = code.IndexOf("[MUSIC_COMMAND_END]") + "[MUSIC_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                    _commandList.Remove(CommandType.Music);
                }
                else // if (settings.MusicDownloadClient == DownloadClient.Lidarr) //Currently only Lidarr
                {
                    code = GenerateMusicCategories(lidarrSettings.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray(), code, _commandList[CommandType.Music], request);
                }

                code = code.Replace("[REQUEST_COMMAND_START]", string.Empty);
                code = code.Replace("[REQUEST_COMMAND_END]", string.Empty);
            }



            //Handle the removal of Issues if not needed
            if (
                (
                    !(settings.MovieDownloadClient == DownloadClient.Overseerr && overseerrSettings.UseMovieIssue) &&
                    !(settings.MovieDownloadClient == DownloadClient.Ombi && ombiSettings.UseMovieIssue)
                )
                &&
                (
                    !(settings.TvShowDownloadClient == DownloadClient.Overseerr && overseerrSettings.UseTVIssue) &&
                    !(settings.TvShowDownloadClient == DownloadClient.Ombi && ombiSettings.UseTVIssue)
                )
            )
            {
                //If movies and tv clients disabled, remove the commands
                //Or if download clients is not Overseerr for both, remove.
                //Or if overseerr does not have issues enabled for both TV and Movies, remove.
                int beginIndex = code.IndexOf("[ISSUE_COMMAND_START]");
                int endIndex = code.IndexOf("[ISSUE_COMMAND_END]") + "[ISSUE_COMMAND_END]".Length;

                code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);

                _commandList.Remove(CommandType.IssueMovie);
                _commandList.Remove(CommandType.IssueTv);
            }
            else
            {
                code = code.Replace("[ISSUE_COMMAND_START]", string.Empty);
                code = code.Replace("[ISSUE_COMMAND_END]", string.Empty);

                if (
                    (!overseerrSettings.UseMovieIssue || settings.MovieDownloadClient != DownloadClient.Overseerr) &&
                    (!ombiSettings.UseMovieIssue || settings.MovieDownloadClient != DownloadClient.Ombi)
                )
                {
                    //If download client does not have movies, remove movies
                    int beginIndex = code.IndexOf("[ISSUE_MOVIE_COMMAND_START]");
                    int endIndex = code.IndexOf("[ISSUE_MOVIE_COMMAND_END]") + "[ISSUE_MOVIE_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                    _commandList.Remove(CommandType.IssueMovie);
                }
                else if (overseerrSettings.UseMovieIssue && settings.MovieDownloadClient == DownloadClient.Overseerr && overseerrSettings.Movies.Categories.Any())
                {
                    Category[] categories = overseerrSettings.Movies.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray();
                    code = GenerateMovieIssueCategories(categories, code, _commandList[CommandType.IssueMovie], issue, categories.Length == 0);
                }
                else
                {
                    _commandList[CommandType.IssueMovie].Add($"{issue} {Language.Current.DiscordCommandMovieIssueTitleName}");
                    _commandList[CommandType.IssueMovie].Add($"{issue} {Language.Current.DiscordCommandMovieIssueTmdbName}");

                    code = code.Replace("[ISSUE_MOVIE_TITLE_NAME]", Language.Current.DiscordCommandMovieIssueTitleName);
                    code = code.Replace("[ISSUE_MOVIE_TMDB_NAME]", Language.Current.DiscordCommandMovieIssueTmdbName);
                }

                if (
                    (!overseerrSettings.UseTVIssue || settings.TvShowDownloadClient != DownloadClient.Overseerr) &&
                    (!ombiSettings.UseTVIssue || settings.TvShowDownloadClient != DownloadClient.Ombi)
                )
                {
                    //If client does not have TV issues enabled, remove tv
                    int beginIndex = code.IndexOf("[ISSUE_TV_COMMAND_START]");
                    int endIndex = code.IndexOf("[ISSUE_TV_COMMAND_END]") + "[ISSUE_TV_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                    _commandList.Remove(CommandType.IssueTv);
                }
                else if (overseerrSettings.UseTVIssue && settings.TvShowDownloadClient == DownloadClient.Overseerr && overseerrSettings.TvShows.Categories.Any())
                {
                    Category[] categories = overseerrSettings.TvShows.Categories.Select(x => new Category { Id = x.Id, Name = x.Name }).ToArray();
                    code = GenerateTvShowIssueCategories(categories, code, _commandList[CommandType.IssueTv], issue, categories.Length == 0);
                }
                else
                {
                    _commandList[CommandType.IssueTv].Add($"{issue} {Language.Current.DiscordCommandTvIssueTitleName}");
                    _commandList[CommandType.IssueTv].Add($"{issue} {Language.Current.DiscordCommandTvIssueTvdbName}");

                    code = code.Replace("[ISSUE_TV_TITLE_NAME]", Language.Current.DiscordCommandTvIssueTitleName);
                    code = code.Replace("[ISSUE_TV_TVDB_NAME]", Language.Current.DiscordCommandTvIssueTvdbName);
                }


                code = code.Replace("[ISSUE_MOVIE_COMMAND_START]", string.Empty);
                code = code.Replace("[ISSUE_MOVIE_COMMAND_END]", string.Empty);
                code = code.Replace("[ISSUE_TMDB_COMMAND_START]", string.Empty);
                code = code.Replace("[ISSUE_TMDB_COMMAND_END]", string.Empty);

                code = code.Replace("[ISSUE_TV_COMMAND_START]", string.Empty);
                code = code.Replace("[ISSUE_TV_COMMAND_END]", string.Empty);
                code = code.Replace("[ISSUE_TVDB_COMMAND_START]", string.Empty);
                code = code.Replace("[ISSUE_TVDB_COMMAND_END]", string.Empty);
            }

            //Deleting is only available with clients that can remove media from disk (Radarr, Sonarr and Seerr), and requires someone who can approve it
            code = code.Replace("[DELETE_GROUP_NAME]", Language.Current.DiscordCommandDeleteName);
            code = code.Replace("[DELETE_GROUP_DESCRIPTION]", Language.Current.DiscordCommandDeleteDescription);

            code = code.Replace("[DELETE_MOVIE_NAME]", Language.Current.DiscordCommandDeleteMovieName);
            code = code.Replace("[DELETE_MOVIE_DESCRIPTION]", Language.Current.DiscordCommandDeleteMovieDescription);
            code = code.Replace("[DELETE_MOVIE_OPTION_NAME]", Language.Current.DiscordCommandDeleteMovieOptionName);
            code = code.Replace("[DELETE_MOVIE_OPTION_DESCRIPTION]", Language.Current.DiscordCommandDeleteMovieOptionDescription);

            code = code.Replace("[DELETE_TV_NAME]", Language.Current.DiscordCommandDeleteTvName);
            code = code.Replace("[DELETE_TV_DESCRIPTION]", Language.Current.DiscordCommandDeleteTvDescription);
            code = code.Replace("[DELETE_TV_OPTION_NAME]", Language.Current.DiscordCommandDeleteTvOptionName);
            code = code.Replace("[DELETE_TV_OPTION_DESCRIPTION]", Language.Current.DiscordCommandDeleteTvOptionDescription);

            var canDeleteMovies = settings.MovieDownloadClient == DownloadClient.Radarr || settings.MovieDownloadClient == DownloadClient.Overseerr;
            var canDeleteTvShows = settings.TvShowDownloadClient == DownloadClient.Sonarr || settings.TvShowDownloadClient == DownloadClient.Overseerr;

            if (!settings.DeleteApprovalRoles.Any() || (!canDeleteMovies && !canDeleteTvShows))
            {
                int beginIndex = code.IndexOf("[DELETE_COMMAND_START]");
                int endIndex = code.IndexOf("[DELETE_COMMAND_END]") + "[DELETE_COMMAND_END]".Length;

                code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
            }
            else
            {
                if (canDeleteMovies)
                {
                    _commandList[CommandType.Misc].Add($"{Language.Current.DiscordCommandDeleteName} {Language.Current.DiscordCommandDeleteMovieName}");
                }
                else
                {
                    int beginIndex = code.IndexOf("[DELETE_MOVIE_COMMAND_START]");
                    int endIndex = code.IndexOf("[DELETE_MOVIE_COMMAND_END]") + "[DELETE_MOVIE_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                }

                if (canDeleteTvShows)
                {
                    _commandList[CommandType.Misc].Add($"{Language.Current.DiscordCommandDeleteName} {Language.Current.DiscordCommandDeleteTvName}");
                }
                else
                {
                    int beginIndex = code.IndexOf("[DELETE_TV_COMMAND_START]");
                    int endIndex = code.IndexOf("[DELETE_TV_COMMAND_END]") + "[DELETE_TV_COMMAND_END]".Length;

                    code = code.Replace(code.Substring(beginIndex, endIndex - beginIndex), string.Empty);
                }

                code = code.Replace("[DELETE_COMMAND_START]", string.Empty);
                code = code.Replace("[DELETE_COMMAND_END]", string.Empty);
                code = code.Replace("[DELETE_MOVIE_COMMAND_START]", string.Empty);
                code = code.Replace("[DELETE_MOVIE_COMMAND_END]", string.Empty);
                code = code.Replace("[DELETE_TV_COMMAND_START]", string.Empty);
                code = code.Replace("[DELETE_TV_COMMAND_END]", string.Empty);
            }

            //Sort list of commands into one list
            List<string> listOfCommands = _commandList.SelectMany(x => x.Value).ToList();

            //Find and check there is no duplicates, if there it, this will not work....
            if (listOfCommands.Count != listOfCommands.Distinct().Count())
                throw new Exception("Duplicate Slash Commands detected.  Make sure categories do not share the same name.");

            return code;
        }

        private static string GenerateMovieCategories(Category[] categories, string code, List<string> commandList, string slashCommand, bool dbOption)
        {
            string start = "[MOVIE_COMMAND_START]";
            string end = "[MOVIE_COMMAND_END]";
            string categoryId = "[MOVIE_CATEGORY_ID]";
            string slashName = "[REQUEST_MOVIE_TITLE_NAME]";

            string dbStart = "[TMDB_COMMAND_START]";
            string dbEnd = "[TMDB_COMMAND_END]";
            string slashDbName = "[REQUEST_MOVIE_TMDB_NAME]";
            string dbPrefix = dbOption ? "tmdb" : string.Empty;

            return GenerateCategories(start, end, dbStart, dbEnd, categoryId, slashName, slashDbName, dbPrefix, categories, code, commandList, slashCommand);
        }


        private static string GenerateMovieIssueCategories(Category[] categories, string code, List<string> commandList, string slashCommand, bool dbOption)
        {
            string start = "[ISSUE_MOVIE_COMMAND_START]";
            string end = "[ISSUE_MOVIE_COMMAND_END]";
            string categoryId = "[MOVIE_CATEGORY_ID]";
            string slashName = "[ISSUE_MOVIE_TITLE_NAME]";

            string dbStart = "[ISSUE_TMDB_COMMAND_START]";
            string dbEnd = "[ISSUE_TMDB_COMMAND_END]";
            string slashDbName = "[ISSUE_MOVIE_TMDB_NAME]";
            string dbPrefix = dbOption ? "tmdb" : string.Empty;

            return GenerateCategories(start, end, dbStart, dbEnd, categoryId, slashName, slashDbName, dbPrefix, categories, code, commandList, slashCommand);
        }


        private static string GenerateTvShowCategories(Category[] categories, string code, List<string> commandList, string slashCommand, bool dbOption)
        {
            string start = "[TV_COMMAND_START]";
            string end = "[TV_COMMAND_END]";
            string categoryId = "[TV_CATEGORY_ID]";
            string slashName = "[REQUEST_TV_TITLE_NAME]";

            string dbStart = "[TVDB_COMMAND_START]";
            string dbEnd = "[TVDB_COMMAND_END]";
            string slashDbName = "[REQUEST_TV_TVDB_NAME]";
            string dbPrefix = dbOption ? "tvdb" : string.Empty;

            return GenerateCategories(start, end, dbStart, dbEnd, categoryId, slashName, slashDbName, dbPrefix, categories, code, commandList, slashCommand);
        }


        private static string GenerateMusicCategories(Category[] categories, string code, List<string> commandList, string slashCommand)
        {
            string start = "[MUSIC_COMMAND_START]";
            string end = "[MUSIC_COMMAND_END]";
            string categoryId = "[MUSIC_CATEGORY_ID]";
            string slashName = "[REQUEST_MUSIC_ARTIST_NAME]";

            return GenerateCategories(start, end, string.Empty, string.Empty, categoryId, slashName, string.Empty, string.Empty, categories, code, commandList, slashCommand);
        }


        private static string GenerateTvShowIssueCategories(Category[] categories, string code, List<string> commandList, string slashCommand, bool dbOption)
        {
            string start = "[ISSUE_TV_COMMAND_START]";
            string end = "[ISSUE_TV_COMMAND_END]";
            string categoryId = "[TV_CATEGORY_ID]";
            string slashName = "[ISSUE_TV_TITLE_NAME]";

            string dbStart = "[ISSUE_TVDB_COMMAND_START]";
            string dbEnd = "[ISSUE_TVDB_COMMAND_END]";
            string slashDbName = "[ISSUE_TV_TVDB_NAME]";
            string dbPrefix = dbOption ? "tvdb" : string.Empty;

            return GenerateCategories(start, end, dbStart, dbEnd, categoryId, slashName, slashDbName, dbPrefix, categories, code, commandList, slashCommand);
        }


        private static string GenerateCategories(string start, string end, string dbStart, string dbEnd, string categoryId, string slashName, string slashDbName, string dbPrefix, Category[] categories, string code, List<string> commandList, string slashCommand)
        {
            var beginIndex = code.IndexOf(start);
            var endIndex = code.IndexOf(end) + end.Length;
            var categoryCommandTemplate = code.Substring(beginIndex, endIndex - beginIndex);
            categoryCommandTemplate = categoryCommandTemplate.Replace(start, string.Empty);
            categoryCommandTemplate = categoryCommandTemplate.Replace(end, string.Empty);

            if (!string.IsNullOrWhiteSpace(dbStart) && !string.IsNullOrWhiteSpace(dbEnd))
            {
                var tmdbStartIndex = categoryCommandTemplate.IndexOf(dbStart);
                var tmdbEndIndex = categoryCommandTemplate.IndexOf(dbEnd) + dbEnd.Length;
                categoryCommandTemplate = categoryCommandTemplate.Replace(categoryCommandTemplate.Substring(tmdbStartIndex, tmdbEndIndex - tmdbStartIndex), string.Empty);
            }

            var sb = new StringBuilder();

            foreach (var category in categories)
            {
                var currentTemplate = categoryCommandTemplate;
                currentTemplate = currentTemplate.Replace(categoryId, category.Id.ToString());
                currentTemplate = currentTemplate.Replace(slashName, category.Name);
                commandList.Add($"{slashCommand} {category.Name}");

                if (!string.IsNullOrWhiteSpace(slashDbName) && !string.IsNullOrWhiteSpace(dbPrefix))
                {
                    currentTemplate = currentTemplate.Replace(slashDbName, $"{category.Name}-{dbPrefix}");
                    commandList.Add($"{slashCommand} {category.Name}-{dbPrefix}");
                }

                sb.Append(currentTemplate);
            }

            return code.Replace(code.Substring(beginIndex, endIndex - beginIndex), sb.ToString());
        }


        public static void CleanUp()
        {
            try
            {
                if(!Directory.Exists(TempFolder))
                    return;

                var dirInfo = new DirectoryInfo(Directory.GetCurrentDirectory());
                var filesToDelete = Directory.GetFiles(TempFolder, $"*.dll");

                foreach (var dllToDelete in filesToDelete.Where(x => x.Contains(SlashCommandBuilder.DLLFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        File.Delete(dllToDelete);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private class Category
        {
            public int Id { get; set; }

            public string Name { get; set; }
        }
    }
}
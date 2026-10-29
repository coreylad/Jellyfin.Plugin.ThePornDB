using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using ThePornDB.Helpers;
using ThePornDB.Models;
using ThePornDB.Providers;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace ThePornDB.ScheduledTasks
{
#if __EMBY__
    public class RenameFiles : IScheduledTask
#else
    public class RenameFiles : IScheduledTask, ILibraryPostScanTask
#endif
    {
        private static readonly SemaphoreSlim RenameLock = new SemaphoreSlim(1, 1);
        private readonly ILibraryManager libraryManager;
        private readonly ILibraryMonitor libraryMonitor;

        public RenameFiles(ILibraryManager libraryManager, ILibraryMonitor libraryMonitor)
        {
            this.libraryManager = libraryManager;
            this.libraryMonitor = libraryMonitor;
        }

        public string Key => Plugin.Instance.Name + "RenameFiles";

        public string Name => "Rename matched videos";

        public string Description => "Renames confidently matched standalone videos in place when automatic renaming is enabled.";

        public string Category => Plugin.Instance.Name;

#if __EMBY__
        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
#else
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return this.ExecuteAsync(progress, cancellationToken);
        }

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
#endif
        {
            return this.RenameAll(progress, cancellationToken);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
#if __EMBY__
                Type = TaskTriggerInfo.TriggerInterval,
#else
                Type = TaskTriggerInfoType.IntervalTrigger,
#endif
                IntervalTicks = TimeSpan.FromHours(1).Ticks,
            };
        }

        private async Task RenameAll(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance.Configuration;
            if (!config.EnableAutoRename)
            {
                progress?.Report(100);
                return;
            }

            if (!config.UseEnhancedMatching || string.IsNullOrWhiteSpace(config.MetadataAPIToken))
            {
                throw new InvalidOperationException("Automatic renaming requires enhanced matching and a ThePornDB API token.");
            }

            await RenameLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var items = this.libraryManager.GetItemList(new InternalItemsQuery
                {
#if __EMBY__
                    IncludeItemTypes = new[] { nameof(Movie) },
#else
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
#endif
                }).OfType<Movie>().Where(o => o.ProviderIds.ContainsKey(Plugin.Instance.Name)).ToList();
                for (var index = 0; index < items.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = items[index];
                    try
                    {
                        await this.RenameItem(item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                    {
                        Logger.Warning($"Rename skipped for \"{item.Path}\": {error.Message}");
                    }

                    progress?.Report(100.0 * (index + 1) / items.Count);
                }

                progress?.Report(100);
            }
            finally
            {
                RenameLock.Release();
            }
        }

        private async Task RenameItem(Movie item, CancellationToken cancellationToken)
        {
            var source = item.Path;
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            // The marker makes repeated scans idempotent and preserves identification after renaming.
            if (SceneMatcher.FileTitle(source).GetAttributeValue("TPDBID") != null)
            {
                return;
            }

            FileRenamer.ValidateSource(source);
#if __EMBY__
            if (item.AdditionalPartCount > 0 || item.GetAlternateVersionIds().Any())
#else
            if (item.AdditionalParts.Length > 0 || item.LocalAlternateVersions.Length > 0)
#endif
            {
                throw new IOException("Stacked videos and alternate versions are not renamed.");
            }

            var providerId = item.ProviderIds[Plugin.Instance.Name]?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Missing ThePornDB media ID.");
            }

            var parts = providerId.Split('/');
            if (parts.Length != 2 || !new[] { "scenes", "movies", "jav" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
                || !Enum.TryParse<SceneType>(parts[0].ToLowerInvariant().TrimEnd('s'), true, out var sceneType))
            {
                throw new ArgumentException("Unrecognized ThePornDB media ID.");
            }

            var results = (await Base.GetSearchResults(
                new MovieInfo
                {
                    Name = SceneMatcher.FileTitle(source),
                    Path = source,
                },
                sceneType,
                cancellationToken).ConfigureAwait(false)).ToList();
            if (!SceneMatcher.IsConfident(results, Plugin.Instance.Configuration.MinimumMatchScore)
                || !string.Equals(results[0].ProviderIds[Plugin.Instance.Name].Replace('\\', '/'), providerId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Rename skipped for \"{source}\": filename does not confidently agree with the stored ThePornDB ID.");
                return;
            }

            var scene = ((MatchedSearchResult)results[0]).Scene;
            var name = FileRenamer.BuildName(scene, Plugin.Instance.Configuration.FileNameFormat, Path.GetExtension(source));
            var directory = Path.GetDirectoryName(source);
            cancellationToken.ThrowIfCancellationRequested();
            this.libraryMonitor.ReportFileSystemChangeBeginning(directory);
            try
            {
                await FileRenamer.Rename(source, name, async destination =>
                {
                    item.Path = destination;
                    try
                    {
#if __EMBY__
                        this.libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);
#else
                        await this.libraryManager.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
#endif
                    }
                    catch
                    {
                        item.Path = source;
                        throw;
                    }
                }).ConfigureAwait(false);
                Logger.Info($"Renamed \"{source}\" to \"{item.Path}\".");
            }
            finally
            {
                this.libraryMonitor.ReportFileSystemChangeComplete(directory, false);
            }
        }
    }
}

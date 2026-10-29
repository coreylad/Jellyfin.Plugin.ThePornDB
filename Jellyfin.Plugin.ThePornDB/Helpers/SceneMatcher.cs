using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Providers;
using ThePornDB.Helpers.Utils;
using ThePornDB.Models;

namespace ThePornDB.Helpers
{
    public static class SceneMatcher
    {
        private static readonly Regex DatePattern = new Regex(@"(?<!\d)(?:(?:19|20)\d{2}|\d{2})[-_. ]\d{2}[-_. ]\d{2}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex TechnicalTokens = new Regex(@"\b(?:2160p?|1080p?|720p?|480p?|4k|uhd|fhd|hd|sd|xxx|x264|x265|h264|h265|hevc|avc|aac|mp4|mkv|avi|wmv|mov|webdl|webrip|bluray)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IdPattern = new Regex(@"\[(?:theporndbid|tpdbid)[=-][^\]]+\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string FileTitle(string path)
        {
            return Path.GetFileNameWithoutExtension((path ?? string.Empty).Replace('\\', '/'));
        }

        public static string MatchingTitle(string name, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return name;
            }

            var title = FileTitle(path);
            if (Regex.IsMatch(Normalize(title), @"^(?:video|movie|scene|[0-9]+|[a-f0-9]{24,})$", RegexOptions.IgnoreCase))
            {
                var directory = Path.GetDirectoryName(path.Replace('\\', '/'));
                return string.IsNullOrEmpty(directory) ? title : Path.GetFileName(directory);
            }

            return title;
        }

        public static string Normalize(string value)
        {
            value = IdPattern.Replace(value ?? string.Empty, " ");
            value = DatePattern.Replace(value, " ");
            value = Regex.Replace(value, @"[^\p{L}\p{N}]+", " ").ToLowerInvariant();
            value = TechnicalTokens.Replace(value, " ");
            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        public static IEnumerable<string> Queries(string name, string path, bool useFilePath)
        {
            var fileTitle = MatchingTitle(name, path);
            return new[] { useFilePath && !string.IsNullOrEmpty(path) ? path : name, fileTitle, Normalize(fileTitle) }
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static double Score(string input, int? year, Scene scene)
        {
            var title = Normalize(scene.Title);
            var source = Normalize(input);
            if (title.Length == 0 || source.Length == 0)
            {
                return 0;
            }

            var titleTokens = title.Split(' ').ToHashSet(StringComparer.OrdinalIgnoreCase);
            var date = DatePattern.Match(input ?? string.Empty);
            var siteMatches = false;
            if (date.Success && date.Index > 0)
            {
                var site = Normalize(input.Substring(0, date.Index)).Replace(" ", string.Empty);
                if (site.Length > 0)
                {
                    var expectedSite = Normalize(scene.Site.Name).Replace(" ", string.Empty);
                    siteMatches = expectedSite.StartsWith(site, StringComparison.OrdinalIgnoreCase);
                    if (!siteMatches)
                    {
                        return 0;
                    }
                }

                source = Normalize(input.Substring(date.Index + date.Length));
            }

            var context = Normalize(scene.Site.Name + " " + string.Join(" ", scene.Performers?.Select(o => o.Name) ?? Enumerable.Empty<string>()))
                .Split(' ').ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sourceTokens = source.Split(' ')
                .Where(o => titleTokens.Contains(o) || (!context.Contains(o) && !(int.TryParse(o, out var number) && number >= 1900 && number <= 2100)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var overlap = sourceTokens.Count(o => titleTokens.Contains(o));
            var tokenScore = 200.0 * overlap / Math.Max(1, sourceTokens.Count + titleTokens.Count);
            var textScore = 0.0;
            if (source.Length > 0 && source.Length <= 512 && title.Length <= 512)
            {
                textScore = 100.0 * (1.0 - ((double)LevenshteinDistance.Calculate(source, title, StringComparison.OrdinalIgnoreCase) / Math.Max(source.Length, title.Length)));
            }

            var score = Math.Max(tokenScore, textScore);
            if (siteMatches && scene.Performers != null)
            {
                var remaining = Normalize(input.Substring(date.Index + date.Length));
                var hasPerformer = false;
                foreach (var performer in scene.Performers.OrderByDescending(o => o.Name?.Length ?? 0))
                {
                    var name = Normalize(performer.Name);
                    if (name.Length > 0 && (" " + remaining + " ").Contains(" " + name + " ", StringComparison.Ordinal))
                    {
                        remaining = Regex.Replace(" " + remaining + " ", Regex.Escape(" " + name + " "), " ").Trim();
                        hasPerformer = true;
                    }
                }

                if (hasPerformer && remaining.Length == 0)
                {
                    score = 100;
                }
            }

            if (date.Success)
            {
                var dateText = Regex.Replace(date.Value, @"[-_. ]", "-");
                if (dateText.Length == 8)
                {
                    dateText = "20" + dateText;
                }

                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate)
                    || !scene.Date.HasValue || Math.Abs((scene.Date.Value.Date - releaseDate.Date).TotalDays) > 1)
                {
                    return Math.Min(score, 40);
                }
            }
            else if (year.HasValue && (!scene.Date.HasValue || scene.Date.Value.Year != year.Value))
            {
                return Math.Min(score, 60);
            }

            return score;
        }

        public static bool IsConfident(IReadOnlyList<RemoteSearchResult> results, int minimumScore)
        {
            if (minimumScore < 50 || minimumScore > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumScore), "Minimum match score must be between 50 and 100.");
            }

            return results.Count > 0 && results[0] is MatchedSearchResult best
                && best.Score >= minimumScore
                && (results.Count == 1 || (results[1] is MatchedSearchResult next && best.Score - next.Score >= 10));
        }

        public static List<RemoteSearchResult> Rank(IEnumerable<MatchedSearchResult> candidates, string source, int? year)
        {
            return candidates.GroupBy(o => o.Scene.UUID, StringComparer.OrdinalIgnoreCase)
                .Select(o =>
                {
                    var candidate = o.First();
                    candidate.Score = Score(source, year, candidate.Scene);
                    return candidate;
                })
                .OrderByDescending(o => o.Score).Cast<RemoteSearchResult>().ToList();
        }
    }
}

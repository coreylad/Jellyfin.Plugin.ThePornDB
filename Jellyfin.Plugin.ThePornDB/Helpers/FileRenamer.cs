using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ThePornDB.Models;

namespace ThePornDB.Helpers
{
    public static class FileRenamer
    {
        public static string BuildName(Scene scene, string format, string extension)
        {
            if (string.IsNullOrWhiteSpace(scene.Title) || string.IsNullOrWhiteSpace(scene.UUID) || string.IsNullOrWhiteSpace(format))
            {
                throw new ArgumentException("A scene title, ID and filename format are required.");
            }

            var parameters = new Dictionary<string, string>
            {
                { "{studio}", scene.Site.Name ?? string.Empty },
                { "{title}", scene.Title },
                { "{release_date}", scene.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty },
                { "{actors}", string.Join(", ", scene.Performers?.Select(o => o.Name) ?? Enumerable.Empty<string>()) },
            };
            var name = Regex.Replace(format, @"\{[^}]*\}", match => parameters.TryGetValue(match.Value, out var value)
                ? value : throw new ArgumentException("Unknown filename placeholder. Use {studio}, {title}, {release_date} or {actors}."));

            name = Regex.Replace(name, @"[\x00-\x1f<>:""/\\|?*]", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim(' ', '.', '-');
            if (name.Length == 0)
            {
                throw new ArgumentException("The filename format produced an empty name.");
            }

            if (Regex.IsMatch(name, @"^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            {
                name = "_" + name;
            }

            var suffix = " [TPDBID=" + scene.UUID + "]" + extension;
            if (!Regex.IsMatch(scene.UUID, @"^[a-zA-Z0-9-]+$") || !Regex.IsMatch(extension, @"^\.[a-zA-Z0-9]+$"))
            {
                throw new ArgumentException("Invalid scene ID or file extension.");
            }

            // Stay below common 255-byte component limits, including on UTF-8 filesystems.
            while (Encoding.UTF8.GetByteCount(name + suffix) > 240 && name.Length > 0)
            {
                name = name.Substring(0, name.Length - (char.IsLowSurrogate(name[name.Length - 1]) ? 2 : 1));
            }

            if (name.Length == 0)
            {
                throw new ArgumentException("The scene ID and extension exceed the filename length limit.");
            }

            return name.TrimEnd(' ', '.') + suffix;
        }

        public static void ValidateSource(string path)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path)
                || !new[] { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".m4v", ".flv", ".webm" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                throw new IOException("Only existing, local, single-file videos can be renamed.");
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Symbolic links are not renamed.");
            }

            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(2))
            {
                throw new IOException("Video was recently modified; it will be retried on a later run.");
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            var companions = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path))
                .Where(o => !string.Equals(o, path, StringComparison.Ordinal))
                .Any(o => Path.GetFileName(o).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(o).StartsWith(stem + "-", StringComparison.OrdinalIgnoreCase));
            if (companions)
            {
                throw new IOException("Video has sidecars or related versions; rename the group with Namer instead.");
            }

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
            }
        }

        public static async Task Rename(string source, string fileName, Func<string, Task> persistPath)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0 || Path.GetFileName(fileName) != fileName)
            {
                throw new ArgumentException("The new name must not contain a directory.");
            }

            var destination = Path.Combine(Path.GetDirectoryName(source), fileName);
            if (string.Equals(source, destination, StringComparison.Ordinal))
            {
                return;
            }

            ValidateSource(source);
            File.Move(source, destination);
            try
            {
                await persistPath(destination).ConfigureAwait(false);
            }
            catch (Exception updateError)
            {
                try
                {
                    File.Move(destination, source);
                }
                catch (Exception rollbackError) when (rollbackError is IOException || rollbackError is UnauthorizedAccessException)
                {
                    throw new AggregateException("Library update and file rename rollback both failed; manual recovery is required.", updateError, rollbackError);
                }

                throw;
            }
        }
    }
}

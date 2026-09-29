using JellyJob.Core.Configuration;
using JellyJob.Core.Pipeline;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Library
{
    /// The recordings under InputDirectory: listing them for the Recordings page, and mapping paths onto them.
    public class RecordingLibrary
    {
        /// What Jellyfin records Live TV as.
        private const string RecordingPattern = "*.ts";

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public RecordingLibrary(IOptions<JellyJobConfiguration> config)
        {
            Config = config;
        }

        public string? Root => Config.Value.InputDirectory;

        public bool IsEnabled => Root is not null;

        /// Every recording below InputDirectory, newest first. Empty when InputDirectory is unset or missing.
        public IReadOnlyList<RecordingFile> List()
        {
            if (Root is not { } root || !Directory.Exists(root)) return [];

            var config = Config.Value;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            return new DirectoryInfo(root)
                .EnumerateFiles(RecordingPattern, options)
                .Select(file =>
                {
                    var output = RecordingPipeline.OutputPathFor(file.FullName, config);
                    return new RecordingFile(
                        Path.GetRelativePath(root, file.FullName).Replace('\\', '/'),
                        file.FullName,
                        file.Length,
                        file.LastWriteTime,
                        output,
                        File.Exists(output));
                })
                .OrderByDescending(r => r.Modified)
                .ToList();
        }

        /// The full path of a recording given relative to InputDirectory, or null if it isn't an existing
        /// file inside it. Guards the Recordings page's form against "../" paths.
        public string? Resolve(string relativePath)
        {
            if (Root is not { } root) return null;

            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            return IsInside(full, root) && File.Exists(full) ? full : null;
        }

        /// Where a path Jellyfin sent lives here. Unchanged if it exists as-is; otherwise the longest tail of
        /// it that exists under InputDirectory. That's what lets Jellyfin and JellyJob mount the same host
        /// folder at different paths: /config/recordings/Red/Red.ts from Jellyfin is found as
        /// {InputDirectory}/Red/Red.ts here. Null if neither works.
        public string? Locate(string path)
        {
            if (File.Exists(path)) return path;
            if (Root is not { } root) return null;

            var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            for (var skip = 0; skip < segments.Length; skip++)
            {
                var candidate = Path.Combine([root, .. segments[skip..]]);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static bool IsInside(string path, string root)
        {
            var relative = Path.GetRelativePath(root, path);
            return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathRooted(relative);
        }
    }
}

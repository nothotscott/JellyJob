namespace JellyJob.Core.Configuration
{
    /// Startup checks that turn a volume JellyJob can't use into one error that names the fix, instead of
    /// an UnauthorizedAccessException from inside Data Protection or the first job.
    ///
    /// The usual cause: Docker creates a bind-mount folder that doesn't exist yet on the host, owned by
    /// root, while the container runs as a non-root user.
    public static class DirectoryAccess
    {
        public static void EnsureWritable(string directory, string setting)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, $".jellyjob-write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            }
            // IOException covers a read-only mount ("Read-only file system").
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException($"JellyJob can't write to JellyJob:{setting} ({directory}). {Hint(directory)}", ex);
            }
        }

        public static void EnsureReadable(string directory, string setting)
        {
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
                entries.MoveNext();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException($"JellyJob can't read JellyJob:{setting} ({directory}). {Hint(directory)}", ex);
            }
        }

        private static string Hint(string directory) =>
            CurrentIds() is var (uid, gid)
                ? $"It runs as {uid}:{gid}. Give that user the host folder mounted at {directory}, e.g. "
                    + $"`sudo chown -R {uid}:{gid} /path/on/host`, or change the container's `user:`."
                : "Check the folder's permissions for the user JellyJob runs as.";

        /// The real uid and gid from /proc/self/status ("Uid:\t1000\t1000\t1000\t1000"). Linux only.
        private static (string Uid, string Gid)? CurrentIds()
        {
            const string status = "/proc/self/status";
            if (!File.Exists(status)) return null;

            string? uid = null, gid = null;
            foreach (var line in File.ReadLines(status))
            {
                var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2) continue;
                if (fields[0] == "Uid:") uid = fields[1];
                else if (fields[0] == "Gid:") gid = fields[1];
            }
            return uid is not null && gid is not null ? (uid, gid) : null;
        }
    }
}

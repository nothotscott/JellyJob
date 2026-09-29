namespace JellyJob.Core.Jobs
{
    public static class RecordingPath
    {
        /// Why a path can't be queued, or null if it can. Call after RecordingLibrary.Locate has had its
        /// chance to map the path onto InputDirectory.
        public static string? Problem(string path)
        {
            if (!System.IO.Path.IsPathFullyQualified(path)) return $"'{path}' is not an absolute path.";
            if (!File.Exists(path))
                return $"'{path}' does not exist in the JellyJob container, as-is or below JellyJob__InputDirectory. "
                    + "Mount Jellyfin's recordings folder and point InputDirectory at it.";
            return null;
        }
    }
}

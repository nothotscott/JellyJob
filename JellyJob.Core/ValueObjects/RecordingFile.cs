namespace JellyJob.Core.ValueObjects
{
    /// A recording found under InputDirectory. RelativePath is below InputDirectory with forward slashes
    /// ("Red/Red 2026_09_28_20_30_00.ts"); OutputPath is where the pipeline writes it, and OutputExists
    /// whether that file is already there.
    public record RecordingFile(string RelativePath, string FullPath, long Size, DateTimeOffset Modified,
        string OutputPath, bool OutputExists)
    {
        public string Name => Path.GetFileName(RelativePath);

        /// The folder below InputDirectory ("Red", "Show/Season 1"), or "" for a file directly in it.
        public string Folder => Path.GetDirectoryName(RelativePath)?.Replace('\\', '/') ?? string.Empty;
    }
}

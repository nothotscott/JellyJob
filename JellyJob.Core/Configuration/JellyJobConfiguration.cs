using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Configuration
{
    /// Bound from the "JellyJob" section. In a container, set it with environment variables using a
    /// double underscore for the section separator, e.g. JellyJob__OutputDirectory=/output.
    public class JellyJobConfiguration
    {
        public const string SectionName = "JellyJob";

        /// JellyJob's own state: the job history (jobs.json), the Data Protection keys, and scratch space
        /// (work/). While a job runs, work/ holds a remuxed copy of the recording, so it needs free space
        /// about the size of the largest recording.
        public string DataDirectory { get; set; } = "/data";

        /// Where processed recordings are written; meant to be its own Jellyfin library.
        public string OutputDirectory { get; set; } = "/output";

        /// Jellyfin's recording folder, as mounted here (read-only is fine). Enables three things:
        /// - the Recordings page, for queueing a recording by hand;
        /// - finding a recording when Jellyfin mounts the same folder at a different path, by matching the
        ///   end of the path Jellyfin sends (…/Red/Red 2026_09_28_20_30_00.ts) against this folder;
        /// - recreating a recording's folders below it under OutputDirectory (Series/Season 1/...).
        /// When unset, Jellyfin's paths must exist here as-is, and only the recording's own folder is kept
        /// in the output (Red/Red 2026_09_28_20_30_00.mkv).
        public string? InputDirectory { get; set; }

        /// How many finished jobs are kept in {DataDirectory}/jobs.json. Unfinished jobs are always kept.
        public int JobHistoryLimit { get; set; } = 100;

        public AdHandling AdHandling { get; set; } = AdHandling.Mark;

        /// The comskip executable; a bare name is looked up on PATH.
        public string ComskipPath { get; set; } = "comskip";

        /// Detection settings. The default is the comskip.ini shipped next to the app.
        public string ComskipIniPath { get; set; } = "comskip.ini";

        /// Folder holding ffmpeg and ffprobe; empty looks them up on PATH.
        public string? FfmpegDirectory { get; set; }

        /// Any ffmpeg video encoder. *_nvenc encoders use constant-quality VBR (-cq); anything else uses -crf.
        public string VideoEncoder { get; set; } = "hevc_nvenc";

        /// -cq for NVENC, -crf otherwise. Lower is better quality and a bigger file.
        public int VideoQuality { get; set; } = 26;

        /// Encoder preset: p1 (fastest) to p7 (best) for NVENC; ultrafast to veryslow for libx264/libx265.
        public string VideoPreset { get; set; } = "p5";

        /// Relative paths resolve against the app's own directory rather than the working directory, which
        /// differs between `dotnet run`, Visual Studio and the container. Container paths are absolute and
        /// pass through unchanged.
        public static string ResolvePath(string path) => Path.GetFullPath(path, AppContext.BaseDirectory);
    }
}

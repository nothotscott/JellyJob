using JellyJob.Core.Configuration;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Pipeline
{
    /// Detects ads with comskip, transcodes into OutputDirectory (cutting or marking the ads per
    /// AdHandling), and copies the recording's .nfo alongside so the output library gets the guide data.
    ///
    /// Safe to re-run on the same recording: an interrupted job is re-queued from the start on restart.
    /// The transcode writes to a .partial file (not a video extension, so Jellyfin's scanner ignores it)
    /// and only renames it into place once complete.
    public class RecordingPipeline : IRecordingPipeline
    {
        /// Sidecars Jellyfin writes next to a recording with the same base name.
        private static readonly string[] SidecarExtensions = [".nfo", ".jpg", ".png"];

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public IAdDetector AdDetector { private get; init; }

        public ITranscoder Transcoder { private get; init; }

        public ILogger<RecordingPipeline> Logger { private get; init; }

        public RecordingPipeline(IOptions<JellyJobConfiguration> config, IAdDetector adDetector, ITranscoder transcoder,
            ILogger<RecordingPipeline> logger)
        {
            Config = config;
            AdDetector = adDetector;
            Transcoder = transcoder;
            Logger = logger;
        }

        public async Task RunAsync(RecordingJob job, CancellationToken ct)
        {
            var config = Config.Value;
            var outputPath = OutputPathFor(job.Path, config);
            var partialPath = outputPath + ".partial";
            var workDirectory = Path.Combine(config.DataDirectory, "work", job.Id.ToString("N"));

            // Left over if a previous attempt at this job was killed.
            DeleteDirectory(workDirectory);
            Directory.CreateDirectory(workDirectory);
            try
            {
                job.Stage = "Detecting ads";
                job.Progress = null;
                var breaks = await AdDetector.DetectAsync(job.Path, workDirectory, ct);
                job.AdBreakCount = breaks.Count;

                var cut = config.AdHandling == AdHandling.Cut && breaks.Count > 0;
                job.Stage = cut ? "Transcoding and cutting ads" : "Transcoding";
                job.Progress = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await Transcoder.TranscodeAsync(job.Path, partialPath, cut ? breaks : [], p => job.Progress = p, ct);
                File.Move(partialPath, outputPath, overwrite: true);

                // Always rewritten or removed, so re-running in the other mode doesn't leave a stale one.
                var edlPath = Path.ChangeExtension(outputPath, ".edl");
                if (config.AdHandling == AdHandling.Mark && breaks.Count > 0)
                    await File.WriteAllTextAsync(edlPath, Edl.Format(breaks, Edl.CommercialType), ct);
                else
                    File.Delete(edlPath);

                CopySidecars(job.Path, outputPath);
                job.OutputPath = outputPath;
                Logger.LogInformation("Wrote {Output} ({Count} ad break(s) {Handling})", outputPath, breaks.Count,
                    config.AdHandling == AdHandling.Cut ? "cut" : "marked");
            }
            finally
            {
                job.Stage = null;
                job.Progress = null;
                File.Delete(partialPath);
                DeleteDirectory(workDirectory);
            }
        }

        /// {OutputDirectory}/{folders below InputDirectory, or just the recording's folder}/{name}.mkv
        public static string OutputPathFor(string recordingPath, JellyJobConfiguration config)
        {
            var recordingDirectory = Path.GetDirectoryName(recordingPath)!;
            var relativeDirectory = Path.GetFileName(recordingDirectory);

            if (config.InputDirectory is { Length: > 0 } root)
            {
                var relative = Path.GetRelativePath(root, recordingDirectory);
                var outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative);
                if (!outside) relativeDirectory = relative == "." ? string.Empty : relative;
            }

            return Path.Combine(config.OutputDirectory, relativeDirectory, Path.GetFileNameWithoutExtension(recordingPath) + ".mkv");
        }

        private void CopySidecars(string recordingPath, string outputPath)
        {
            var recordingBase = Path.Combine(Path.GetDirectoryName(recordingPath)!, Path.GetFileNameWithoutExtension(recordingPath));
            var outputBase = Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath));
            foreach (var extension in SidecarExtensions)
            {
                if (!File.Exists(recordingBase + extension)) continue;
                File.Copy(recordingBase + extension, outputBase + extension, overwrite: true);
                Logger.LogDebug("Copied {Sidecar}", recordingBase + extension);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}

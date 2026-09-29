using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Pipeline
{
    public interface ITranscoder
    {
        /// Stream-copies a recording into a fresh .ts with continuous timestamps, so ad detection and the
        /// transcode agree on where everything is.
        Task RemuxAsync(string inputPath, string outputPath, CancellationToken ct);

        /// Transcodes a recording to Matroska at outputPath, leaving out the given breaks (empty to keep
        /// everything). onProgress receives percent complete.
        Task TranscodeAsync(string inputPath, string outputPath, IReadOnlyList<AdBreak> cuts,
            Action<double> onProgress, CancellationToken ct);
    }
}

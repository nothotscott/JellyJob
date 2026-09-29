using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Pipeline
{
    public interface ITranscoder
    {
        /// Transcodes a recording to Matroska at outputPath, leaving out the given breaks (empty to keep
        /// everything). onProgress receives percent complete.
        Task TranscodeAsync(string inputPath, string outputPath, IReadOnlyList<AdBreak> cuts,
            Action<double> onProgress, CancellationToken ct);
    }
}

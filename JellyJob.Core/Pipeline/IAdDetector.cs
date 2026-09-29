using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Pipeline
{
    public interface IAdDetector
    {
        /// Finds the ad breaks in a recording, in order. workDirectory is scratch space the caller cleans up.
        Task<IReadOnlyList<AdBreak>> DetectAsync(string recordingPath, string workDirectory, CancellationToken ct);
    }
}

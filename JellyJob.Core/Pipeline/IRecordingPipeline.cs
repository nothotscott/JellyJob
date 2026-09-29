using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Pipeline
{
    public interface IRecordingPipeline
    {
        /// Processes one recording. Throwing marks the job failed with the exception's message.
        Task RunAsync(RecordingJob job, CancellationToken ct);
    }
}

using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Jobs
{
    /// Hands jobs to the worker. The jobs themselves, and their history, live in IRecordingJobStore.
    public interface IJobQueue
    {
        /// Stores and queues a recording, then returns; the pipeline runs later on the worker.
        Task<RecordingJob> EnqueueAsync(string path);

        /// Waits for the next queued job.
        ValueTask<RecordingJob> DequeueAsync(CancellationToken ct);

        /// Re-queues jobs the last run left queued or running, oldest first. Called once at startup.
        Task RecoverAsync();
    }
}

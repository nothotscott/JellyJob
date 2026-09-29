using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Stores
{
    public interface IRecordingJobStore
    {
        /// Newest first.
        Task<IReadOnlyList<RecordingJob>> GetAllAsync();

        Task<RecordingJob?> FindAsync(Guid id);

        /// Adds the job, or persists its current state if it's already stored. Then drops the oldest
        /// finished jobs past JobHistoryLimit.
        Task SaveAsync(RecordingJob job);
    }
}

namespace JellyJob.Core.ValueObjects
{
    /// One recording handed to the pipeline. The worker updates it in place as it runs and saves it to
    /// IRecordingJobStore after each change; the dashboard reads the same instance from the store.
    public class RecordingJob
    {
        // init rather than get-only so JsonRecordingJobStore can read them back.
        public Guid Id { get; init; } = Guid.NewGuid();

        /// The recording's path as Jellyfin sees it. JellyJob must mount the recordings directory at the
        /// same path for this to resolve.
        public required string Path { get; init; }

        public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public JobStatus Status { get; set; } = JobStatus.Queued;

        public string? Error { get; set; }

        /// What the pipeline is doing right now, e.g. "Detecting ads"; null when not running.
        public string? Stage { get; set; }

        /// Percent complete of the current stage, when it reports progress (transcoding does, comskip doesn't).
        /// Updated many times a second but only persisted with the next status change.
        public double? Progress { get; set; }

        /// Ad breaks comskip found, once detection has run.
        public int? AdBreakCount { get; set; }

        /// The processed file, once written.
        public string? OutputPath { get; set; }

        public TimeSpan? Duration => StartedAt is { } start ? (FinishedAt ?? DateTimeOffset.Now) - start : null;
    }
}

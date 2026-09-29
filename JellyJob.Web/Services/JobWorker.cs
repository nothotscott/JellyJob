using JellyJob.Core.Jobs;
using JellyJob.Core.Pipeline;
using JellyJob.Core.Stores;
using JellyJob.Core.ValueObjects;

namespace JellyJob.Web.Services
{
    /// Runs queued jobs through the pipeline one at a time. Ad detection and transcoding each want the
    /// whole CPU, so running them side by side would only make every job slower.
    public class JobWorker : BackgroundService
    {
        public IJobQueue Queue { private get; init; }

        public IRecordingJobStore Store { private get; init; }

        public IRecordingPipeline Pipeline { private get; init; }

        public ILogger<JobWorker> Logger { private get; init; }

        public JobWorker(IJobQueue queue, IRecordingJobStore store, IRecordingPipeline pipeline, ILogger<JobWorker> logger)
        {
            Queue = queue;
            Store = store;
            Pipeline = pipeline;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Queue.RecoverAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                RecordingJob job;
                try
                {
                    job = await Queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RunAsync(job, stoppingToken);
            }
        }

        private async Task RunAsync(RecordingJob job, CancellationToken ct)
        {
            job.StartedAt = DateTimeOffset.Now;
            job.Status = JobStatus.Running;
            await Store.SaveAsync(job);
            Logger.LogInformation("Processing {Path} (job {JobId})", job.Path, job.Id);

            JobStatus outcome;
            try
            {
                await Pipeline.RunAsync(job, ct);
                outcome = JobStatus.Succeeded;
                Logger.LogInformation("Finished {Path} in {Duration}", job.Path, DateTimeOffset.Now - job.StartedAt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Left as Running in the store, so RecoverAsync re-queues it on the next start.
                Logger.LogInformation("Stopped mid-job; {Path} will be re-queued on the next start", job.Path);
                return;
            }
            catch (Exception ex)
            {
                outcome = JobStatus.Failed;
                job.Error = ex.Message;
                Logger.LogError(ex, "Failed to process {Path}", job.Path);
            }

            job.FinishedAt = DateTimeOffset.Now;
            job.Status = outcome;
            await Store.SaveAsync(job);
        }
    }
}

using System.Threading.Channels;
using JellyJob.Core.Stores;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace JellyJob.Core.Jobs
{
    public class JobQueue : IJobQueue
    {
        private readonly Channel<RecordingJob> _channel = Channel.CreateUnbounded<RecordingJob>(
            new UnboundedChannelOptions { SingleReader = true });

        public IRecordingJobStore Store { private get; init; }

        public ILogger<JobQueue> Logger { private get; init; }

        public JobQueue(IRecordingJobStore store, ILogger<JobQueue> logger)
        {
            Store = store;
            Logger = logger;
        }

        public async Task<RecordingJob> EnqueueAsync(string path)
        {
            var job = new RecordingJob { Path = path };
            // Stored before it's queued, so a crash in between still leaves it to be recovered.
            await Store.SaveAsync(job);
            // Unbounded, so this never fails short of the channel being completed, which nothing does.
            _channel.Writer.TryWrite(job);
            return job;
        }

        public ValueTask<RecordingJob> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);

        public async Task RecoverAsync()
        {
            var unfinished = (await Store.GetAllAsync())
                .Where(j => j.Status is JobStatus.Queued or JobStatus.Running)
                .OrderBy(j => j.ReceivedAt)
                .ToList();

            foreach (var job in unfinished)
            {
                // A job that was running when the container stopped starts over from the beginning; the
                // pipeline has to tolerate leftovers from a partial run.
                if (job.Status == JobStatus.Running)
                {
                    job.Status = JobStatus.Queued;
                    job.StartedAt = null;
                    job.Stage = null;
                    job.Progress = null;
                    await Store.SaveAsync(job);
                }
                _channel.Writer.TryWrite(job);
            }

            if (unfinished.Count > 0) Logger.LogInformation("Re-queued {Count} unfinished job(s) from the last run", unfinished.Count);
        }
    }
}

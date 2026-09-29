using JellyJob.Core.Configuration;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Stores
{
    /// Jobs persisted as {DataDirectory}/jobs.json, cached in memory after first read. The cached
    /// instances are the live ones the worker mutates, so reads always see current progress; SaveAsync is
    /// what makes a change survive a restart.
    public class JsonRecordingJobStore : IRecordingJobStore
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private List<RecordingJob>? _cache;

        public string FilePath { get; init; }

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public JsonRecordingJobStore(IOptions<JellyJobConfiguration> config)
        {
            Config = config;
            FilePath = Path.Combine(config.Value.DataDirectory, "jobs.json");
        }

        public async Task<IReadOnlyList<RecordingJob>> GetAllAsync()
        {
            await _gate.WaitAsync();
            try
            {
                return [.. await LoadAsync()];
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<RecordingJob?> FindAsync(Guid id)
        {
            var jobs = await GetAllAsync();
            return jobs.FirstOrDefault(j => j.Id == id);
        }

        public async Task SaveAsync(RecordingJob job)
        {
            await _gate.WaitAsync();
            try
            {
                var jobs = await LoadAsync();
                var index = jobs.FindIndex(j => j.Id == job.Id);
                if (index < 0) jobs.Insert(0, job);
                else jobs[index] = job;

                Trim(jobs);
                await JsonFile.WriteAtomicAsync(FilePath, jobs);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<List<RecordingJob>> LoadAsync() =>
            _cache ??= (await JsonFile.ReadAsync<List<RecordingJob>>(FilePath) ?? [])
                .OrderByDescending(j => j.ReceivedAt)
                .ToList();

        /// Unfinished jobs are never dropped: they're re-queued on the next start.
        private void Trim(List<RecordingJob> jobs)
        {
            var limit = Math.Max(Config.Value.JobHistoryLimit, 0);
            for (var i = jobs.Count - 1; i >= 0 && jobs.Count > limit; i--)
            {
                if (jobs[i].Status is JobStatus.Succeeded or JobStatus.Failed) jobs.RemoveAt(i);
            }
        }
    }
}

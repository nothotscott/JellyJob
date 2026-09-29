using JellyJob.Core.Jobs;
using JellyJob.Core.Library;
using JellyJob.Core.Stores;
using JellyJob.Core.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace JellyJob.Web.Pages
{
    /// Recordings under InputDirectory, for queueing by hand. Only offered when InputDirectory is set.
    public class RecordingsModel : PageModel
    {
        public RecordingLibrary Library { get; init; }

        public IJobQueue Queue { private get; init; }

        public IRecordingJobStore Store { private get; init; }

        /// Grouped by folder, the folder with the newest recording first.
        public IReadOnlyList<IGrouping<string, RecordingFile>> Folders { get; private set; } = [];

        public int Count { get; private set; }

        [TempData]
        public string? QueuedMessage { get; set; }

        public string? QueueError { get; private set; }

        private IReadOnlyDictionary<string, RecordingJob> _latestJobs = new Dictionary<string, RecordingJob>();

        public RecordingsModel(RecordingLibrary library, IJobQueue queue, IRecordingJobStore store)
        {
            Library = library;
            Queue = queue;
            Store = store;
        }

        public async Task OnGetAsync() => await LoadAsync();

        public async Task<IActionResult> OnPostQueueAsync(string relativePath)
        {
            if (Library.Resolve(relativePath) is not { } path)
            {
                QueueError = $"'{relativePath}' isn't a recording in {Library.Root}.";
                await LoadAsync();
                return Page();
            }

            await Queue.EnqueueAsync(path);
            QueuedMessage = $"Queued {Path.GetFileName(path)}";
            return RedirectToPage();
        }

        /// The most recent job for a recording, if it has ever been queued.
        public RecordingJob? LatestJob(RecordingFile recording) =>
            _latestJobs.GetValueOrDefault(Path.GetFullPath(recording.FullPath));

        private async Task LoadAsync()
        {
            var recordings = Library.List();
            Count = recordings.Count;
            // List() is newest first, so grouping keeps each folder's recordings newest first too, and the
            // folders come out in order of their newest recording.
            Folders = recordings.GroupBy(r => r.Folder).ToList();

            var latest = new Dictionary<string, RecordingJob>(StringComparer.Ordinal);
            foreach (var job in await Store.GetAllAsync()) latest.TryAdd(Path.GetFullPath(job.Path), job);
            _latestJobs = latest;
        }
    }
}

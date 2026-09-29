using JellyJob.Core.Configuration;
using JellyJob.Core.Stores;
using JellyJob.Core.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace JellyJob.Web.Pages
{
    public class IndexModel : PageModel
    {
        public IRecordingJobStore Store { private get; init; }

        public JellyJobConfiguration Config { get; }

        public IReadOnlyList<RecordingJob> Jobs { get; private set; } = [];

        public IndexModel(IRecordingJobStore store, IOptions<JellyJobConfiguration> config)
        {
            Store = store;
            Config = config.Value;
        }

        public async Task OnGetAsync()
        {
            Jobs = await Store.GetAllAsync();
        }

        /// Polled by site.js to refresh the stats and job table without reloading the page.
        public async Task<PartialViewResult> OnGetJobsAsync() => Partial("_Jobs", await Store.GetAllAsync());
    }
}

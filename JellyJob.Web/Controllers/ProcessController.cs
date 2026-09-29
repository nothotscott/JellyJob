using System.Text.Json;
using JellyJob.Core.Jobs;
using JellyJob.Core.Library;
using JellyJob.Core.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace JellyJob.Web.Controllers
{
    [ApiController]
    [Route("process")]
    public class ProcessController : ControllerBase
    {
        public IJobQueue Queue { private get; init; }

        public IRecordingJobStore Store { private get; init; }

        public RecordingLibrary Library { private get; init; }

        public ILogger<ProcessController> Logger { private get; init; }

        public ProcessController(IJobQueue queue, IRecordingJobStore store, RecordingLibrary library, ILogger<ProcessController> logger)
        {
            Queue = queue;
            Store = store;
            Library = library;
            Logger = logger;
        }

        /// Jellyfin's recording post-processor calls this when a recording finishes:
        ///   curl -X POST http://host.docker.internal:5238/process -d '{"path":"{path}"}'
        /// Queues the recording and answers 202 straight away; the pipeline runs on JobWorker.
        ///
        /// curl's -d sends Content-Type: application/x-www-form-urlencoded even when the body is JSON, so
        /// the body is read raw instead of model-bound. Accepts a JSON body {"path": "..."}, a form body
        /// path=..., or ?path=... on the URL.
        ///
        /// Deliberately takes no parameters, not even a CancellationToken: binding any parameter makes MVC
        /// read a form-encoded body into Request.Form first, and the raw body is then empty.
        [HttpPost]
        public async Task<IActionResult> Process()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(HttpContext.RequestAborted);

            var path = ReadPath(body, Request.Query);
            if (string.IsNullOrWhiteSpace(path))
            {
                Logger.LogWarning("POST /process had no path. Body: {Body}", body);
                return Problem("Expected a JSON body {\"path\": \"...\"}, a form body path=..., or ?path=...",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Jellyfin's path for the recording, which may be mounted somewhere else here.
            var local = Library.Locate(path) ?? path;
            if (RecordingPath.Problem(local) is { } problem)
            {
                Logger.LogWarning("Rejected {Path}: {Problem}", path, problem);
                return Problem(problem, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var job = await Queue.EnqueueAsync(local);
            if (local != path) Logger.LogInformation("Queued {Path} (Jellyfin's {JellyfinPath}) as job {JobId}", local, path, job.Id);
            else Logger.LogInformation("Queued {Path} as job {JobId}", path, job.Id);
            return AcceptedAtAction(nameof(Get), new { id = job.Id }, job);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id) => await Store.FindAsync(id) is { } job ? Ok(job) : NotFound();

        private static string? ReadPath(string body, IQueryCollection query)
        {
            var trimmed = body.Trim();
            if (trimmed.StartsWith('{'))
            {
                try
                {
                    using var json = JsonDocument.Parse(trimmed);
                    foreach (var property in json.RootElement.EnumerateObject())
                    {
                        if (property.Name.Equals("path", StringComparison.OrdinalIgnoreCase)
                            && property.Value.ValueKind == JsonValueKind.String)
                            return property.Value.GetString();
                    }
                }
                catch (JsonException)
                {
                    // Fall through to the query string; the caller logs the body if that's empty too.
                }
            }
            else if (trimmed.Length > 0 && QueryHelpers.ParseQuery(trimmed).TryGetValue("path", out var formPath))
            {
                return formPath.ToString();
            }

            return query["path"].FirstOrDefault();
        }
    }
}

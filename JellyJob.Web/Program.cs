using System.Text.Json.Serialization;
using JellyJob.Core.Configuration;
using JellyJob.Core.Jobs;
using JellyJob.Core.Library;
using JellyJob.Core.Pipeline;
using JellyJob.Core.Stores;
using JellyJob.Web.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Enums as names ("Queued", not 0) in the JSON API.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRazorPages();

// Environment variables override appsettings.json, e.g. JellyJob__OutputDirectory=/output.
builder.Services.AddOptions<JellyJobConfiguration>()
    .BindConfiguration(JellyJobConfiguration.SectionName)
    .Validate(c => !string.IsNullOrWhiteSpace(c.DataDirectory), "JellyJob:DataDirectory is required")
    .Validate(c => !string.IsNullOrWhiteSpace(c.OutputDirectory), "JellyJob:OutputDirectory is required")
    // Checked after PostConfigure has resolved the paths, so the messages name the file actually looked for.
    .Validate(c => File.Exists(c.ComskipIniPath), "JellyJob:ComskipIniPath doesn't exist")
    .Validate(c => c.FfmpegDirectory is null || Directory.Exists(c.FfmpegDirectory), "JellyJob:FfmpegDirectory doesn't exist")
    .Validate(c => c.InputDirectory is null || Directory.Exists(c.InputDirectory), "JellyJob:InputDirectory doesn't exist")
    .Validate(c => c.VideoQuality is >= 0 and <= 51, "JellyJob:VideoQuality must be 0-51")
    .PostConfigure(c =>
    {
        c.DataDirectory = JellyJobConfiguration.ResolvePath(c.DataDirectory);
        c.OutputDirectory = JellyJobConfiguration.ResolvePath(c.OutputDirectory);
        c.ComskipIniPath = JellyJobConfiguration.ResolvePath(c.ComskipIniPath);
        if (!string.IsNullOrWhiteSpace(c.InputDirectory)) c.InputDirectory = JellyJobConfiguration.ResolvePath(c.InputDirectory);
        else c.InputDirectory = null;
        if (!string.IsNullOrWhiteSpace(c.FfmpegDirectory)) c.FfmpegDirectory = JellyJobConfiguration.ResolvePath(c.FfmpegDirectory);
        else c.FfmpegDirectory = null;
        // ComskipPath is left alone: a bare "comskip" means look it up on PATH.
    })
    .ValidateOnStart();

// Antiforgery and TempData encrypt with Data Protection keys. By default they live in the container's
// home directory and are lost on every image update, which breaks any form left open across it. Keep
// them in the data volume with the rest of the state.
var dataDirectory = JellyJobConfiguration.ResolvePath(
    builder.Configuration.GetSection(JellyJobConfiguration.SectionName).Get<JellyJobConfiguration>()?.DataDirectory
    ?? new JellyJobConfiguration().DataDirectory);
builder.Services.AddDataProtection()
    .SetApplicationName("JellyJob")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddSingleton<IRecordingJobStore, JsonRecordingJobStore>();
builder.Services.AddSingleton<IJobQueue, JobQueue>();
builder.Services.AddSingleton<RecordingLibrary>();
builder.Services.AddSingleton<IAdDetector, ComskipAdDetector>();
builder.Services.AddSingleton<ITranscoder, FfmpegTranscoder>();
builder.Services.AddSingleton<IRecordingPipeline, RecordingPipeline>();
builder.Services.AddHostedService<JobWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok("ok"));
app.MapControllers();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

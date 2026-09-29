# JellyJob

Post-recording pipeline for Jellyfin Live TV. Runs as a sidecar container next to Jellyfin; Jellyfin's
"Recording post-processing" setting runs curl against `POST /process` with the recording's path, and
JellyJob queues it for ad removal (comskip) and transcoding (ffmpeg). Recordings come from LinTV
(`../LinTV`) through Jellyfin as MPEG transport streams (`.ts`).

User-facing overview, deployment (Portainer/compose YAML) and every config option: [README.md](README.md).
It's the only place deployment is documented; keep its config table and full compose example in step
with `JellyJobConfiguration`.

## Layout

- `JellyJob.Core` — class library: `Configuration/JellyJobConfiguration` (the `IOptions` model, section
  `JellyJob`), `ValueObjects/` (`RecordingJob`, `JobStatus`, `AdHandling` Mark/Cut, `AdBreak`,
  `RecordingFile`), `Library/RecordingLibrary` (lists `*.ts` under `InputDirectory`; `Resolve` guards the
  Recordings page's relative paths; `Locate` maps a Jellyfin path onto `InputDirectory`), `Jobs/`
  (`JobQueue` — a channel feeding the worker, plus startup recovery; `RecordingPath` validation),
  `Stores/` (`JsonRecordingJobStore`: `{DataDirectory}/jobs.json`, cached in memory, write-then-rename via
  `JsonFile` — same pattern as LinTV), `Pipeline/`:
  - `RecordingPipeline` — orchestrates: comskip → transcode to `<out>.mkv.partial` → rename → write or
    delete the `.edl` → copy `.nfo`/`.jpg`/`.png` sidecars. Output path mirrors folders below
    `InputDirectory` (`OutputPathFor`). Scratch in `{DataDirectory}/work/{jobId}`.
  - `ComskipAdDetector` — runs comskip with `--ini= --output=<work>` and parses the `.edl` it writes.
  - `FfmpegTranscoder` — FFMpegCore for probing/progress/cancellation, but the arguments are custom:
    CPU decode + `bwdif` + NVENC encode, AAC audio. Cut mode builds one `split`/`trim`/`atrim`/`concat`
    filter graph (`MapSegments`).
  - `Edl` — parse/format; `CommercialType = 3` is what EdlToMediaSegments maps to a Commercial segment.
- `JellyJob.Web` — ASP.NET Core Razor Pages + API controllers. `Controllers/ProcessController`
  (`POST /process`, `GET /process/{id}`), `Services/JobWorker` (BackgroundService, runs one job at a
  time), `Pages/Index` (dashboard; `_Jobs` partial is polled by `wwwroot/js/site.js`), `Pages/Recordings`
  (recordings under `InputDirectory`, grouped by folder, with a Queue button each; off when it's unset).
- `JellyJob.Web/Dockerfile` — build context is the repo root. Stage names are what VS container
  debugging expects; keep them.
- `.github/workflows/publish-container.yml` — pushes `ghcr.io/<owner>/jellyjob:latest` and `:sha-xxxxxxx`
  on every push to `master`.

## Conventions

Match LinTV's style: block-scoped namespaces, `///` comments without XML tags, dependencies as
`{ private get; init; }` properties assigned in the constructor, config classes with a `SectionName`.
No CSS framework: the UI is hand-written `wwwroot/css/site.css` with light/dark tokens on `:root`.

## Gotchas

- **Jellyfin doesn't use a shell for the post-processor.** .NET splits the argument string with
  Windows rules on Linux too, so single quotes aren't quoting: `-d '{"path":"{path}"}'` arrives as
  `'{path:/x.ts}'` and curl still exits 0. The documented arguments use `--data-urlencode "path={path}"`.
- **`ProcessController.Process` must take no parameters.** Binding any parameter (even a
  `CancellationToken`) makes MVC read a form-encoded body into `Request.Form` first, leaving the raw
  body empty. curl's `-d` always sends `application/x-www-form-urlencoded`, JSON or not.
- **Paths are Jellyfin's paths.** `POST /process` gets the path as Jellyfin's container sees it. It's used
  as-is if it exists here; otherwise `RecordingLibrary.Locate` tries ever-shorter tails of it under
  `InputDirectory` (`/livetv/Red/Red.ts` → `{InputDirectory}/Red/Red.ts`), so the two containers can mount
  the same host folder at different paths. Jobs store the local path. Without `InputDirectory`, paths must
  match exactly; `RecordingPath.Problem` rejects the rest with a 422.
- The input is only read (comskip writes into `{DataDirectory}/work`), so it can be mounted `:ro`.
- Jobs left `Queued` or `Running` are re-queued at startup (`JobQueue.RecoverAsync`), and an interrupted
  job starts over, so pipeline steps must tolerate leftovers from a partial run (write to a temp name,
  then move).
- Central package management: versions go in `Directory.Packages.props`, never in a csproj. The
  Dockerfile copies it before `dotnet restore`.
- The .NET 10 base images are Ubuntu 24.04 (noble), not Debian — `apt-get install` Ubuntu package names.
  `ffmpeg` (6.1, NVENC enabled) and `comskip` (universe) both come from Ubuntu.
- **comskip's exit codes swap by platform** (`mpeg2dec.c`: `exit(result)` on Windows, `exit(!result)`
  elsewhere): 0/1 are both success, anything else is an error. Whether ads were found is read from the
  `.edl`, never the exit code.
- **comskip finds ini settings by substring** (`FindNumber(data, "name=")`), so a comment in
  `comskip.ini` containing `name=` is read as the value.
- Cut mode uses `trim`/`concat` rather than `select` + `setpts=N/FRAME_RATE/TB`: trimming keeps each
  segment's real timestamps, so dropped frames in the broadcast (common in `.ts`) don't drift A/V sync.
  Verified: a 300 s clip with two cuts came out at exactly the expected length, A/V ending within ~15 ms.
- NVENC fails with "Driver does not support the required nvenc API version" when the ffmpeg build is
  newer than the driver. The dev machine's ffmpeg 9 needs driver 610+; use `VideoEncoder=libx265` there.
- `site.js` pauses polling while `document.hidden`; a background automation browser tab never refreshes.

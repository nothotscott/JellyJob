# JellyJob

A post-recording pipeline for Jellyfin Live TV, running as a sidecar container. When Jellyfin finishes a
recording it calls JellyJob, which queues the `.ts` file and runs it through the pipeline:

1. **Detect ads** with comskip.
2. **Transcode** with ffmpeg (HEVC on the GPU via NVENC by default, AAC audio, deinterlaced, Matroska)
   into a separate output directory, meant to be its own Jellyfin library. The ads are either **cut out**
   or **marked** in an `.edl` beside the file, depending on `AdHandling`.
3. **Copy the `.nfo`** (Jellyfin's guide data for the recording) next to the output.

```
/mnt/media-library/livetv/Red/Red 2026_09_28_20_30_00.ts          ← Jellyfin records
/mnt/media-library/livetv/Red/Red 2026_09_28_20_30_00.nfo
                         ↓
/mnt/media-library/recordings/Red/Red 2026_09_28_20_30_00.mkv      ← JellyJob writes
/mnt/media-library/recordings/Red/Red 2026_09_28_20_30_00.nfo
/mnt/media-library/recordings/Red/Red 2026_09_28_20_30_00.edl      (Mark mode, when ads were found)
```

The original recording is only ever read, so its folder can be mounted read-only.

The dashboard (`http://<host>:5238/`) shows the queue and each job's progress. The **Recordings** page lists
everything in `InputDirectory` and queues any of it by hand, which is the easiest way to try the pipeline
on an existing recording before wiring up Jellyfin.

## Deploying

The image is `ghcr.io/nothotscott/jellyjob:latest`, rebuilt on every push to `master`. As a Portainer stack
(or `docker-compose.yml`), changing the paths to yours:

```yaml
services:
  jellyjob:
    image: ghcr.io/nothotscott/jellyjob:latest
    container_name: jellyjob
    restart: unless-stopped
    runtime: nvidia                  # NVENC; see "GPU" below
    user: "1000:1000"                # whoever owns the media: must read the input, write the output
    ports:
      - "5238:5238"
    volumes:
      - /mnt/your-docker-data/jellyjob/data:/data
      - /mnt/media-library/livetv:/media-library/livetv:ro              # Jellyfin's recordings; read-only
      - /mnt/media-library/recordings:/media-library/recordings         # processed output
    environment:
      - JellyJob__InputDirectory=/media-library/livetv
      - JellyJob__OutputDirectory=/media-library/recordings
      - JellyJob__AdHandling=Mark    # or Cut; see "Ads" below
      - NVIDIA_VISIBLE_DEVICES=all
      - NVIDIA_DRIVER_CAPABILITIES=all
      - TZ=America/New_York
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://127.0.0.1:5238/health"]
      interval: 30s
      timeout: 5s
      retries: 3
```

Compose's list form of `environment` takes `NAME=value`; `- NAME: value` in a list is a YAML map, which
Compose rejects.

Then:

1. **Point Jellyfin at it** (below).
2. **Add the output folder to Jellyfin as its own library**, so the processed copies don't show up next to
   the raw recordings.
3. For Mark mode, install the [EdlToMediaSegments](https://github.com/rrhett/EdlToMediaSegments) plugin in
   Jellyfin, so the marked breaks become skippable.

### Pointing Jellyfin at it

In Jellyfin: **Dashboard → Live TV → Recording post-processing**.

| Field | Value |
| --- | --- |
| Post-processor application | `/usr/bin/curl` |
| Post-processor command line arguments | `-fsS -X POST http://host.docker.internal:5238/process --data-urlencode "path={path}"` |

`host.docker.internal` resolves by itself on Docker Desktop. On Linux, add this to the **Jellyfin** service,
or put both containers on one network and use `http://jellyjob:5238/process`:

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

Use the arguments exactly as shown. Jellyfin starts the post-processor directly, **not through a shell**,
and .NET splits the argument string with Windows-style rules: double quotes group, single quotes are
ordinary characters. A shell-style `-d '{"path":"{path}"}'` reaches JellyJob as `'{path:/x/Show.ts}'`, and
curl still exits 0, so Jellyfin reports nothing wrong. If you want JSON, use `-d "{\"path\":\"{path}\"}"`.

### Paths

Jellyfin sends **its own** path for the recording, e.g. `/livetv/Red/Red 2026_09_28_20_30_00.ts` if that's
where its container sees the recordings. JellyJob uses that path if it exists in its own container, and
otherwise looks for the end of it under `InputDirectory`: `/media-library/livetv/Red/Red 2026_09_28_20_30_00.ts`.
So both containers need the **same host folder** mounted, but at any path. Without `InputDirectory`, the
paths must match exactly. A recording JellyJob can't find is rejected with a 422 saying so, which shows in
Jellyfin's log.

`InputDirectory` also decides the output layout: a recording's folders below it are recreated under
`OutputDirectory` (`Show/Season 1/Show S01E02.mkv`).

### GPU

NVENC needs the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/)
on the host, registered as a Docker runtime (`nvidia-ctk runtime configure --runtime=docker`), plus
`runtime: nvidia` and the two `NVIDIA_*` variables above. `NVIDIA_DRIVER_CAPABILITIES` must include `video`
(`all` does); that's what mounts the NVENC libraries into the container.

The image uses Ubuntu 24.04's ffmpeg (6.1), which needs a host driver of roughly 530 or newer. A driver too
old fails the job with *Driver does not support the required nvenc API version*. Without a GPU, drop
`runtime: nvidia` and the `NVIDIA_*` variables, and set `JellyJob__VideoEncoder=libx265` and
`JellyJob__VideoPreset=medium`.

### Ads: Mark or Cut

- **Mark** (default): the whole recording is transcoded, and an `.edl` beside it lists each break as type 3
  (commercial). With EdlToMediaSegments installed and Jellyfin's *Media segment scan* task run, clients
  offer to skip the breaks. A wrong detection costs nothing.
- **Cut**: the breaks are removed during the transcode, in the same pass, frame-accurately. A wrong
  detection permanently loses part of the show, and comskip is least reliable on movies (dark scenes and
  fades look like breaks).

Detection is tuned by [`comskip.ini`](JellyJob.Web/comskip.ini). To change it, copy it into the data volume
and set `JellyJob__ComskipIniPath=/data/comskip.ini`.

## Configuration

`JellyJobConfiguration`, bound from the `JellyJob` section: set it with environment variables, `__` between
the section and the name. Relative paths resolve against the app's directory.

| Variable | Default | |
| --- | --- | --- |
| `JellyJob__InputDirectory` | *(unset)* | Jellyfin's recordings folder, as mounted here. Enables the Recordings page, finding recordings Jellyfin mounts at a different path, and mirroring its folders in the output. Must exist if set. |
| `JellyJob__OutputDirectory` | `/output` | Where processed recordings are written; a separate Jellyfin library. |
| `JellyJob__DataDirectory` | `/data` | `jobs.json` (job history), Data Protection keys, and comskip's scratch files. |
| `JellyJob__AdHandling` | `Mark` | `Mark` or `Cut`. |
| `JellyJob__VideoEncoder` | `hevc_nvenc` | Any ffmpeg encoder: `h264_nvenc`, `av1_nvenc`, `libx265`, `libx264`... `*_nvenc` uses `-cq`, anything else `-crf`. |
| `JellyJob__VideoQuality` | `26` | `-cq`/`-crf`, 0–51. Lower is better and bigger. |
| `JellyJob__VideoPreset` | `p5` | `p1`–`p7` for NVENC; `ultrafast`–`veryslow` for libx264/libx265. |
| `JellyJob__JobHistoryLimit` | `100` | Finished jobs kept in `jobs.json`. Unfinished jobs are always kept. |
| `JellyJob__ComskipIniPath` | `comskip.ini` | Detection settings; the default is the one shipped in the image. |
| `JellyJob__ComskipPath` | `comskip` | The comskip executable; a bare name is looked up on `PATH`. |
| `JellyJob__FfmpegDirectory` | *(unset)* | Folder with `ffmpeg` and `ffprobe`; unset uses `PATH`. |

And a few from ASP.NET Core itself:

| Variable | Default | |
| --- | --- | --- |
| `ASPNETCORE_HTTP_PORTS` | `5238` | The port inside the container. Match the Jellyfin URL and the healthcheck if you change it. |
| `Logging__LogLevel__JellyJob` | `Information` | `Debug` also logs comskip's output line by line. |
| `TZ` | `Etc/UTC` | Only affects log timestamps; the dashboard shows times in the browser's zone. |

Every option, with its default where there is one:

```yaml
services:
  jellyjob:
    image: ghcr.io/nothotscott/jellyjob:latest
    container_name: jellyjob
    restart: unless-stopped
    runtime: nvidia
    user: "1000:1000"
    ports:
      - "5238:5238"
    volumes:
      - /mnt/your-docker-data/jellyjob/data:/data
      - /mnt/media-library/livetv:/media-library/livetv:ro
      - /mnt/media-library/recordings:/media-library/recordings
    environment:
      # Paths
      - JellyJob__InputDirectory=/media-library/livetv
      - JellyJob__OutputDirectory=/media-library/recordings
      - JellyJob__DataDirectory=/data
      # Pipeline
      - JellyJob__AdHandling=Mark
      - JellyJob__VideoEncoder=hevc_nvenc
      - JellyJob__VideoQuality=26
      - JellyJob__VideoPreset=p5
      - JellyJob__JobHistoryLimit=100
      # Tools (the image provides all three; override to use your own)
      - JellyJob__ComskipIniPath=comskip.ini         # the image's; or /data/comskip.ini once you've copied it there
      - JellyJob__ComskipPath=comskip
      # - JellyJob__FfmpegDirectory=/opt/ffmpeg/bin  # default: ffmpeg and ffprobe from PATH
      # GPU
      - NVIDIA_VISIBLE_DEVICES=all
      - NVIDIA_DRIVER_CAPABILITIES=all
      # ASP.NET Core
      - ASPNETCORE_HTTP_PORTS=5238
      - Logging__LogLevel__JellyJob=Information
      - TZ=America/New_York
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://127.0.0.1:5238/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s
```

## API

| Route | |
| --- | --- |
| `POST /process` | Queues a recording. Accepts a JSON body `{"path": "..."}` (any `Content-Type`, since curl's `-d` labels JSON as a form), a form body `path=...`, or `?path=...`. Returns `202` with the job. `400` if no path, `422` if the recording can't be found. |
| `GET /process/{id}` | A job's status, stage, progress, ad break count and output path. |
| `GET /health` | Liveness, for the healthcheck. |

Jobs run one at a time and are saved to `{DataDirectory}/jobs.json`. On startup, jobs the last run left
queued or running are re-queued; one that was mid-run starts over from the beginning. The transcode writes
`<name>.mkv.partial` and renames it when done, so Jellyfin never scans a half-written file.

## Developing

```sh
dotnet run --project JellyJob.Web        # http://localhost:5238
docker build -f JellyJob.Web/Dockerfile -t jellyjob .
```

Development uses `data` and `output` below `JellyJob.Web/bin/Debug/net10.0/`, and ffmpeg from
`D:\Programs\ffmpeg` (`appsettings.Development.json`). comskip isn't installed on Windows by default; install
it and set `JellyJob__ComskipPath`, or run the container. NVENC with a recent ffmpeg build needs a recent
driver (ffmpeg 9 wants 610+); set `JellyJob__VideoEncoder=libx265` to encode on the CPU instead.

using System.Globalization;
using System.Text;
using FFMpegCore;
using JellyJob.Core.Configuration;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Pipeline
{
    /// Transcodes with ffmpeg through FFMpegCore. Decoding and deinterlacing run on the CPU (cheap for
    /// MPEG-2, and it keeps the filter graph free of hardware frames); only the encode uses the GPU.
    public class FfmpegTranscoder : ITranscoder
    {
        private const int KeptOutputLines = 20;

        /// Keep-segments shorter than this are dropped rather than producing a sliver of show between two
        /// breaks that comskip split.
        private static readonly TimeSpan MinimumSegment = TimeSpan.FromSeconds(1);

        /// Broadcast video is usually interlaced. send_frame keeps the frame rate (29.97, not 59.94 fields),
        /// and deint=interlaced leaves frames flagged progressive untouched, so it's safe on any input.
        private const string Deinterlace = "bwdif=mode=send_frame:parity=auto:deint=interlaced";

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public ILogger<FfmpegTranscoder> Logger { private get; init; }

        public FfmpegTranscoder(IOptions<JellyJobConfiguration> config, ILogger<FfmpegTranscoder> logger)
        {
            Config = config;
            Logger = logger;
        }

        public async Task TranscodeAsync(string inputPath, string outputPath, IReadOnlyList<AdBreak> cuts,
            Action<double> onProgress, CancellationToken ct)
        {
            var config = Config.Value;
            var options = new FFOptions { BinaryFolder = config.FfmpegDirectory ?? string.Empty };

            var media = await FFProbe.AnalyseAsync(inputPath, options, ct);
            if (media.PrimaryVideoStream is null) throw new InvalidOperationException($"{inputPath} has no video stream.");

            var audioStreams = media.AudioStreams;
            var keep = KeepSegments(media.Duration, cuts);
            if (keep.Count == 0) throw new InvalidOperationException("Every part of the recording was detected as an ad; not cutting.");

            var outputDuration = keep.Aggregate(TimeSpan.Zero, (total, s) => total + ((s.End ?? media.Duration) - s.Start));
            var arguments = string.Join(' ',
                cuts.Count == 0 ? MapWhole() : MapSegments(keep, audioStreams.Count),
                VideoArguments(config),
                AudioArguments(audioStreams),
                "-max_muxing_queue_size 4096 -f matroska");

            var tail = new Queue<string>();
            var processor = FFMpegArguments
                .FromFileInput(inputPath)
                .OutputToFile(outputPath, overwrite: true, o => o.WithCustomArgument(arguments))
                .NotifyOnError(line =>
                {
                    lock (tail)
                    {
                        tail.Enqueue(line);
                        if (tail.Count > KeptOutputLines) tail.Dequeue();
                    }
                })
                .CancellableThrough(ct);
            if (outputDuration > TimeSpan.Zero) processor.NotifyOnProgress(onProgress, outputDuration);

            Logger.LogInformation("ffmpeg {Arguments}", processor.Arguments);
            var succeeded = await processor.ProcessAsynchronously(throwOnError: false, options);

            // Cancelling asks ffmpeg to stop gracefully, so it can exit 0 with a truncated file.
            ct.ThrowIfCancellationRequested();
            if (!succeeded)
            {
                string[] lines;
                lock (tail) lines = [.. tail];
                throw new InvalidOperationException($"ffmpeg failed: {Cause(lines)}\n\n{string.Join('\n', lines)}");
            }
        }

        /// The first line that says what went wrong, for the one-line summary; the dashboard shows the rest
        /// on demand. ffmpeg's own last word ("Conversion failed!") never says why.
        private static string Cause(IReadOnlyList<string> lines) =>
            lines.FirstOrDefault(l =>
                !l.StartsWith("Conversion failed", StringComparison.OrdinalIgnoreCase)
                && (l.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("not support", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("no such", StringComparison.OrdinalIgnoreCase)
                    || l.Contains("unable", StringComparison.OrdinalIgnoreCase)))
            ?? lines.LastOrDefault()
            ?? "no output";

        /// The parts of the recording to keep: the gaps between breaks. A null End runs to the end.
        public static IReadOnlyList<(TimeSpan Start, TimeSpan? End)> KeepSegments(TimeSpan duration, IReadOnlyList<AdBreak> cuts)
        {
            var keep = new List<(TimeSpan, TimeSpan?)>();
            var position = TimeSpan.Zero;
            foreach (var cut in cuts.OrderBy(c => c.Start))
            {
                if (cut.Start - position >= MinimumSegment) keep.Add((position, cut.Start));
                if (cut.End > position) position = cut.End;
            }
            // An unknown (zero) duration can't be compared against; assume there's show after the last break.
            if (duration == TimeSpan.Zero || duration - position >= MinimumSegment) keep.Add((position, null));
            return keep;
        }

        private static string MapWhole() => $"-map 0:v:0 -map 0:a? -vf {Deinterlace}";

        /// One pass: split the deinterlaced video and each audio stream once per kept segment, trim each copy
        /// to its segment, and concatenate them. Trimming keeps each segment's own timestamps, so a glitch in
        /// the broadcast doesn't drift audio against video the way frame-counting (select + setpts=N) would.
        private static string MapSegments(IReadOnlyList<(TimeSpan Start, TimeSpan? End)> keep, int audioCount)
        {
            var n = keep.Count;
            var graph = new StringBuilder();

            graph.Append($"[0:v:0]{Deinterlace},split={n}");
            for (var i = 0; i < n; i++) graph.Append($"[v{i}]");
            graph.Append(';');
            for (var i = 0; i < n; i++)
                graph.Append($"[v{i}]trim={Range(keep[i])},setpts=PTS-STARTPTS[v{i}t];");

            for (var a = 0; a < audioCount; a++)
            {
                graph.Append($"[0:a:{a}]asplit={n}");
                for (var i = 0; i < n; i++) graph.Append($"[a{a}_{i}]");
                graph.Append(';');
                for (var i = 0; i < n; i++)
                    graph.Append($"[a{a}_{i}]atrim={Range(keep[i])},asetpts=PTS-STARTPTS[a{a}_{i}t];");
            }

            // concat wants each segment's streams together: v0 a0_0 a1_0, v1 a0_1 a1_1, ...
            for (var i = 0; i < n; i++)
            {
                graph.Append($"[v{i}t]");
                for (var a = 0; a < audioCount; a++) graph.Append($"[a{a}_{i}t]");
            }
            graph.Append($"concat=n={n}:v=1:a={audioCount}[vout]");
            for (var a = 0; a < audioCount; a++) graph.Append($"[aout{a}]");

            var maps = new StringBuilder("-map [vout]");
            for (var a = 0; a < audioCount; a++) maps.Append($" -map [aout{a}]");

            // No spaces anywhere in the graph, so it needs no quoting on either platform.
            return $"-filter_complex {graph} {maps}";
        }

        private static string Range((TimeSpan Start, TimeSpan? End) segment) =>
            segment.End is { } end
                ? string.Create(CultureInfo.InvariantCulture, $"start={segment.Start.TotalSeconds:0.###}:end={end.TotalSeconds:0.###}")
                : string.Create(CultureInfo.InvariantCulture, $"start={segment.Start.TotalSeconds:0.###}");

        private static string VideoArguments(JellyJobConfiguration config) =>
            config.VideoEncoder.Contains("nvenc", StringComparison.OrdinalIgnoreCase)
                // -b:v 0 lifts NVENC's default bitrate cap so -cq alone decides the quality.
                ? $"-c:v {config.VideoEncoder} -preset {config.VideoPreset} -tune hq -rc vbr -cq {config.VideoQuality} -b:v 0"
                : $"-c:v {config.VideoEncoder} -preset {config.VideoPreset} -crf {config.VideoQuality}";

        /// AAC plays everywhere; AC3, what ATSC broadcasts, doesn't play in most browsers.
        private static string AudioArguments(IReadOnlyList<AudioStream> streams)
        {
            var arguments = new StringBuilder("-c:a aac");
            for (var i = 0; i < streams.Count; i++)
                arguments.Append(CultureInfo.InvariantCulture, $" -b:a:{i} {(streams[i].Channels > 2 ? 384 : 192)}k");
            return arguments.ToString();
        }
    }
}

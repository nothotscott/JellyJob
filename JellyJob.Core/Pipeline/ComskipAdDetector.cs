using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using JellyJob.Core.Configuration;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Pipeline
{
    /// Runs comskip and reads back the .edl it writes.
    public partial class ComskipAdDetector : IAdDetector
    {
        private const int KeptOutputLines = 20;

        /// detect_method bit for logo detection (comskip.c: #define LOGO 2).
        private const int LogoDetection = 2;

        /// comskip's compiled-in detect_method when the ini doesn't set one: black frames, logo, resolution
        /// change, aspect ratio and silence (comskip.c: commDetectMethod).
        private const int DefaultDetectMethod = 1 + 2 + 8 + 32 + 64;

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public ILogger<ComskipAdDetector> Logger { private get; init; }

        public ComskipAdDetector(IOptions<JellyJobConfiguration> config, ILogger<ComskipAdDetector> logger)
        {
            Config = config;
            Logger = logger;
        }

        public async Task<IReadOnlyList<AdBreak>> DetectAsync(string recordingPath, string workDirectory, CancellationToken ct)
        {
            var (exitCode, output) = await RunAsync(recordingPath, workDirectory, detectMethod: null, ct);

            // Logo detection is where comskip crashes on some recordings (a segfault, seen as exit 139, on
            // Ubuntu's 0.82 build; upstream fixed that case, but other MPEG-2 crashes are reported against
            // master). Losing one detection method beats losing the job, so retry once without it.
            if (Crashed(exitCode) && await DetectMethodAsync(ct) is var method && (method & LogoDetection) != 0)
            {
                Logger.LogWarning("comskip crashed (exit {ExitCode}) on {Path}; retrying without logo detection", exitCode, recordingPath);
                (exitCode, output) = await RunAsync(recordingPath, workDirectory, method & ~LogoDetection, ct);
            }

            // "Found" and "not found" are both success, but the two codes swap by platform: 0/1 on Linux, 1/0
            // on Windows (mpeg2dec.c: exit(!result) vs exit(result)). Anything else is an error. Whether
            // there were ads is read from the .edl rather than the code.
            if (exitCode is not (0 or 1))
            {
                var crash = Crashed(exitCode) ? " (crashed)" : string.Empty;
                throw new InvalidOperationException($"comskip exited with code {exitCode}{crash}:\n{output}");
            }

            var edl = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(recordingPath) + ".edl");
            var breaks = File.Exists(edl) ? Edl.Parse(await File.ReadAllTextAsync(edl, ct)) : [];
            Logger.LogInformation("comskip found {Count} ad break(s) in {Path}", breaks.Count, recordingPath);
            return breaks;
        }

        /// Returns the exit code and the last lines comskip printed. detectMethod, when given, overrides the
        /// ini's detect_method (comskip applies --detectmethod after loading the ini).
        private async Task<(int ExitCode, string Output)> RunAsync(string recordingPath, string workDirectory,
            int? detectMethod, CancellationToken ct)
        {
            var config = Config.Value;
            var start = new ProcessStartInfo(config.ComskipPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add($"--ini={config.ComskipIniPath}");
            if (detectMethod is { } method) start.ArgumentList.Add($"--detectmethod={method}");
            start.ArgumentList.Add($"--output={workDirectory}");
            start.ArgumentList.Add(recordingPath);

            // comskip is chatty; keep the tail for the error message and send the rest to debug logging.
            var tail = new Queue<string>();
            void Collect(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                Logger.LogDebug("comskip: {Line}", line);
                lock (tail)
                {
                    tail.Enqueue(line);
                    if (tail.Count > KeptOutputLines) tail.Dequeue();
                }
            }

            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => Collect(e.Data);
            process.ErrorDataReceived += (_, e) => Collect(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            lock (tail) return (process.ExitCode, string.Join('\n', tail));
        }

        /// Killed by a signal: Linux reports 128 + the signal (139 = SIGSEGV, 134 = SIGABRT); Windows reports
        /// an NTSTATUS such as 0xC0000005, which is negative as an int. comskip's own error exits are small.
        /// Its exit(-1) also lands here as 255, which only costs one pointless retry.
        private static bool Crashed(int exitCode) => exitCode >= 128 || exitCode < 0;

        /// The detect_method comskip will use, found the way comskip finds it: the first "detect_method="
        /// anywhere in the ini (comskip.c: FindNumber).
        private async Task<int> DetectMethodAsync(CancellationToken ct)
        {
            var ini = await File.ReadAllTextAsync(Config.Value.ComskipIniPath, ct);
            var match = DetectMethodPattern().Match(ini);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : DefaultDetectMethod;
        }

        [GeneratedRegex(@"detect_method=\s*(\d+)")]
        private static partial Regex DetectMethodPattern();
    }
}

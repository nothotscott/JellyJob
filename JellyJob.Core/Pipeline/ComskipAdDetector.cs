using System.Diagnostics;
using JellyJob.Core.Configuration;
using JellyJob.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JellyJob.Core.Pipeline
{
    /// Runs comskip and reads back the .edl it writes.
    public class ComskipAdDetector : IAdDetector
    {
        private const int KeptOutputLines = 20;

        public IOptions<JellyJobConfiguration> Config { private get; init; }

        public ILogger<ComskipAdDetector> Logger { private get; init; }

        public ComskipAdDetector(IOptions<JellyJobConfiguration> config, ILogger<ComskipAdDetector> logger)
        {
            Config = config;
            Logger = logger;
        }

        public async Task<IReadOnlyList<AdBreak>> DetectAsync(string recordingPath, string workDirectory, CancellationToken ct)
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

            // "Found" and "not found" are both success, but the two codes swap by platform: 0/1 on Linux, 1/0
            // on Windows (mpeg2dec.c: exit(!result) vs exit(result)). Anything else is an error. Whether
            // there were ads is read from the .edl rather than the code.
            if (process.ExitCode is not (0 or 1))
            {
                string output;
                lock (tail) output = string.Join('\n', tail);
                throw new InvalidOperationException($"comskip exited with code {process.ExitCode}:\n{output}");
            }

            var edl = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(recordingPath) + ".edl");
            var breaks = File.Exists(edl) ? Edl.Parse(await File.ReadAllTextAsync(edl, ct)) : [];
            Logger.LogInformation("comskip found {Count} ad break(s) in {Path}", breaks.Count, recordingPath);
            return breaks;
        }
    }
}

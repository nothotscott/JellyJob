using System.Text.Json;
using System.Text.Json.Serialization;

namespace JellyJob.Core.Stores
{
    /// Shared persistence for the JSON stores.
    internal static class JsonFile
    {
        // Enums as names and indented, so the files are readable when debugging by hand.
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static async Task<T?> ReadAsync<T>(string path)
        {
            if (!File.Exists(path)) return default;

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options);
        }

        /// Write-then-rename so a crash mid-write never leaves a truncated file.
        public static async Task WriteAtomicAsync<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temp = path + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, value, Options);
            File.Move(temp, path, overwrite: true);
        }
    }
}

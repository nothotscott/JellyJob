using System.Globalization;
using System.Text;
using JellyJob.Core.ValueObjects;

namespace JellyJob.Core.Pipeline
{
    /// MPlayer-style edit decision lists: one "start end type" line per region, times in seconds.
    public static class Edl
    {
        /// The type EdlToMediaSegments maps to Jellyfin's Commercial segment (0 intro, 1 preview, 2 recap,
        /// 3 commercial, 4 outro). Comskip writes its own edl_skip_field instead, which is ignored on read.
        public const int CommercialType = 3;

        public static IReadOnlyList<AdBreak> Parse(string text)
        {
            var breaks = new List<AdBreak>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2
                    || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)
                    || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end)
                    || end <= start)
                    continue;

                breaks.Add(new AdBreak(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
            }
            return breaks.OrderBy(b => b.Start).ToList();
        }

        public static string Format(IEnumerable<AdBreak> breaks, int type)
        {
            var text = new StringBuilder();
            foreach (var b in breaks)
                text.Append(CultureInfo.InvariantCulture, $"{b.Start.TotalSeconds:0.00}\t{b.End.TotalSeconds:0.00}\t{type}\n");
            return text.ToString();
        }
    }
}

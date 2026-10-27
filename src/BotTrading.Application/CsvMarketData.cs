using System.Globalization;
using System.Text;
using BotTrading.Core;

namespace BotTrading.Application;

public static class CsvMarketData
{
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static MarketSeries Parse(Stream input, string symbol, string? name = null)
    {
        if (!input.CanRead) throw new ArgumentException("Unreadable input.", nameof(input));
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = reader.ReadLine() ?? throw new FormatException("CSV is empty.");
        char separator = CountOutsideQuotes(headerLine, ';') > CountOutsideQuotes(headerLine, ',') ? ';' : ',';
        var header = SplitCsv(headerLine, separator).Select(x => x.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToArray();
        string[] required = ["date", "open", "high", "low", "close"];
        if (required.Any(x => !header.Contains(x)))
            throw new FormatException("CSV must include date,open,high,low,close headers (volume optional).");
        int dateCol = Array.IndexOf(header, "date"), openCol = Array.IndexOf(header, "open"),
            highCol = Array.IndexOf(header, "high"), lowCol = Array.IndexOf(header, "low"),
            closeCol = Array.IndexOf(header, "close"), volumeCol = Array.IndexOf(header, "volume");
        var bars = new List<Candle>();
        string? line;
        int lineNumber = 1, bytesEstimated = Encoding.UTF8.GetByteCount(headerLine) + 2;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            bytesEstimated += Encoding.UTF8.GetByteCount(line) + 2;
            if (bytesEstimated > MaxFileBytes) throw new FormatException("Maximum CSV size is 16 MiB.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = SplitCsv(line, separator);
            try
            {
                var date = DateOnly.ParseExact(values[dateCol], ["yyyy-MM-dd", "yyyyMMdd"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None);
                decimal Read(int col) => decimal.Parse(values[col], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture);
                bars.Add(new(date, Read(openCol), Read(highCol), Read(lowCol), Read(closeCol),
                    volumeCol < 0 ? 0 : Read(volumeCol)));
                if (bars.Count > 100_000) throw new FormatException("Maximum 100000 rows per series.");
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException or OverflowException)
            {
                throw new FormatException($"Malformed CSV at line {lineNumber}: {ex.Message}", ex);
            }
        }
        return new MarketSeries(symbol, name ?? symbol, bars);
    }

    internal static int CountOutsideQuotes(string value, char separator)
    {
        bool quoted = false; int count = 0;
        foreach (var ch in value) { if (ch == '"') quoted = !quoted; else if (!quoted && ch == separator) count++; }
        return count;
    }

    internal static string[] SplitCsv(string value, char separator)
    {
        var output = new List<string>(); var part = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '"')
            {
                if (quoted && i + 1 < value.Length && value[i + 1] == '"') { part.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == separator && !quoted) { output.Add(part.ToString().Trim()); part.Clear(); }
            else part.Append(c);
        }
        if (quoted) throw new FormatException("Unterminated quoted CSV field.");
        output.Add(part.ToString().Trim());
        return output.ToArray();
    }
}

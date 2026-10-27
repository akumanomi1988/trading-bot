using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BotTrading.Application;

/// <summary>Imported articles are user-provided research, not verified market data or investment advice.</summary>
public sealed record NewsEvent(DateTimeOffset PublishedAt, string Symbol, string Headline, string Source);
public sealed record NewsInsight(DateTimeOffset PublishedAt, string Symbol, string Headline, string Source,
    decimal SentimentScore, string SentimentLabel);

public static partial class NewsResearch
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxItems = 5000;
    private static readonly HashSet<string> Positive = new(StringComparer.OrdinalIgnoreCase) {
        "gain", "gains", "growth", "profit", "profits", "beat", "beats", "surge", "surges", "rally", "rallies",
        "upgrade", "upgrades", "recovery", "strong", "record", "rise", "rises", "gana", "ganancias", "beneficio", "beneficios",
        "sube", "crece", "crecimiento", "alza", "mejora", "récord"
    };
    private static readonly HashSet<string> Negative = new(StringComparer.OrdinalIgnoreCase) {
        "loss", "losses", "crash", "crashes", "drop", "drops", "fall", "falls", "decline", "declines",
        "downgrade", "downgrades", "fraud", "recession", "war", "collapse", "sanctions", "default", "risk", "risks",
        "caída", "cae", "baja", "pérdida", "pérdidas", "recesión", "guerra", "fraude", "riesgo", "sanciones"
    };

    public static IReadOnlyList<NewsEvent> Parse(Stream input)
    {
        if (!input.CanRead) throw new ArgumentException("Unreadable CSV stream.", nameof(input));
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string first = reader.ReadLine() ?? throw new FormatException("News CSV is empty.");
        char sep = CsvMarketData.CountOutsideQuotes(first, ';') > CsvMarketData.CountOutsideQuotes(first, ',') ? ';' : ',';
        var header = CsvMarketData.SplitCsv(first, sep).Select(x => x.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToArray();
        string[] required = ["publishedat", "symbol", "headline", "source"];
        if (required.Any(x => header.Count(col => col == x) != 1))
            throw new FormatException("News CSV requires publishedAt,symbol,headline,source headers.");
        if (Encoding.UTF8.GetByteCount(first) > MaxFileBytes) throw new FormatException("News CSV exceeds 1 MiB.");
        int[] columns = required.Select(x => Array.IndexOf(header, x)).ToArray();
        var events = new List<NewsEvent>();
        int lineNumber = 1;
        int bytes = Encoding.UTF8.GetByteCount(first) + 2;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            bytes = checked(bytes + Encoding.UTF8.GetByteCount(line) + 2);
            if (bytes > MaxFileBytes) throw new FormatException("News CSV exceeds 1 MiB.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] cells;
            try { cells = CsvMarketData.SplitCsv(line, sep); }
            catch (FormatException ex) { throw new FormatException($"Invalid news CSV on line {lineNumber}.", ex); }
            if (columns.Any(x => x >= cells.Length)) throw new FormatException($"Missing fields on line {lineNumber}.");
            string timeText = cells[columns[0]].Trim();
            if (!TimeZoneSuffix().IsMatch(timeText) ||
                !DateTimeOffset.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var publishedAt))
                throw new FormatException($"Line {lineNumber}: publishedAt must include an explicit UTC offset or Z.");
            var item = new NewsEvent(publishedAt.ToUniversalTime(), cells[columns[1]].ToUpperInvariant(),
                cells[columns[2]], cells[columns[3]]);
            if (!IsValid(item)) throw new FormatException($"Invalid news item on line {lineNumber}.");
            events.Add(item);
            if (events.Count > MaxItems) throw new FormatException("Too many imported news items (maximum 5000).");
        }
        return events;
    }

    public static bool IsValid(NewsEvent item) => item.Symbol is not null && item.Headline is not null && item.Source is not null &&
        SymbolPattern().IsMatch(item.Symbol) &&
        item.Headline.Length is > 0 and <= 240 && !string.IsNullOrWhiteSpace(item.Headline) &&
        item.Source.Length is > 0 and <= 100 && !string.IsNullOrWhiteSpace(item.Source) &&
        !item.Headline.Any(char.IsControl) && !item.Source.Any(char.IsControl) &&
        item.PublishedAt != default;

    public static IReadOnlyList<NewsInsight> Query(IReadOnlyCollection<NewsEvent> items, string symbol, DateOnly? cutoff = null)
    {
        if (symbol is null || !SymbolPattern().IsMatch(symbol.ToUpperInvariant()))
            throw new ArgumentException("Select a valid instrument or GLOBAL.", nameof(symbol));
        var normalized = symbol.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        return items.Where(x => (x.Symbol == normalized || x.Symbol == "GLOBAL") && x.PublishedAt <= now &&
            (!cutoff.HasValue || DateOnly.FromDateTime(x.PublishedAt.UtcDateTime) < cutoff.Value))
            .OrderByDescending(x => x.PublishedAt).Take(100).Select(ToInsight).ToArray();
    }

    public static NewsInsight ToInsight(NewsEvent item)
    {
        var words = WordPattern().Matches(item.Headline).Select(x => x.Value);
        int positive = 0, negative = 0;
        foreach (var word in words)
        {
            if (Positive.Contains(word)) positive++;
            if (Negative.Contains(word)) negative++;
        }
        decimal score = positive + negative == 0 ? 0m : decimal.Round((decimal)(positive - negative) / (positive + negative), 2);
        string label = score > 0 ? "POSITIVE" : score < 0 ? "NEGATIVE" : "NEUTRAL";
        return new NewsInsight(item.PublishedAt, item.Symbol, item.Headline, item.Source, score, label);
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._-]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();
    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeZoneSuffix();
    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}

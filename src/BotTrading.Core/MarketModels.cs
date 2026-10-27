using System.Globalization;
using System.Text.RegularExpressions;

namespace BotTrading.Core;

public sealed record Candle(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume)
{
    public void Validate()
    {
        if (Open <= 0 || High <= 0 || Low <= 0 || Close <= 0 || Volume < 0)
            throw new ArgumentException($"Invalid nonpositive OHLC or negative volume for {Date}.");
        if (High < Low || High < Math.Max(Open, Close) || Low > Math.Min(Open, Close))
            throw new ArgumentException($"Inconsistent OHLC values for {Date}.");
    }
}

public sealed record PriceProvenance(string Source, string AdjustmentStatus, string? CurrencyCode = null, string? Venue = null)
{
    public void Validate()
    {
        if (Source is not ("STOOQ" or "USER_CSV" or "UNSPECIFIED"))
            throw new ArgumentException("Unknown historical data source.");
        if (AdjustmentStatus is not ("UNKNOWN" or "RAW" or "ADJUSTED"))
            throw new ArgumentException("Adjustment status must be UNKNOWN, RAW or ADJUSTED.");
        if (CurrencyCode is not null && !Regex.IsMatch(CurrencyCode, "^[A-Z]{3}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Currency must be an ISO-style uppercase three-letter code.");
        if (Venue is not null && !Regex.IsMatch(Venue, "^[A-Z0-9._-]{1,20}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Trading venue must be an uppercase ASCII code.");
    }
}

public sealed class MarketSeries
{
    private static readonly Regex ValidTicker = new("^[A-Z0-9][A-Z0-9._-]{0,19}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public string Symbol { get; }
    public string Name { get; }
    public IReadOnlyList<Candle> Candles { get; }
    public PriceProvenance Provenance { get; }

    public MarketSeries(string symbol, string name, IReadOnlyList<Candle> candles, PriceProvenance? provenance = null)
    {
        Symbol = symbol.Trim().ToUpperInvariant();
        if (!ValidTicker.IsMatch(Symbol)) throw new ArgumentException("Symbol must be 1-20 ASCII ticker characters.", nameof(symbol));
        Name = string.IsNullOrWhiteSpace(name) ? Symbol : name.Trim()[..Math.Min(name.Trim().Length, 80)];
        Provenance = provenance ?? new PriceProvenance("UNSPECIFIED", "UNKNOWN");
        Provenance.Validate();
        var data = candles.OrderBy(c => c.Date).ToArray();
        if (data.Length == 0 || data.Length > 100_000) throw new ArgumentException("Series must contain 1-100000 candles.");
        foreach (var candle in data) candle.Validate();
        for (int i = 1; i < data.Length; i++)
            if (data[i - 1].Date == data[i].Date) throw new ArgumentException("Duplicate dates are not allowed.");
        Candles = Array.AsReadOnly(data);
    }

    public SeriesSummary ToSummary() => new(Symbol, Name, Candles.Count, Candles[0].Date, Candles[^1].Date, Candles[^1].Close, Provenance);
}

public sealed record SeriesSummary(string Symbol, string Name, int Count, DateOnly Start, DateOnly End, decimal LastClose,
    PriceProvenance? Provenance = null);

public static class Indicators
{
    public static decimal?[] Sma(IReadOnlyList<Candle> candles, int period)
    {
        if (period < 1 || period > 5000) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal?[candles.Count];
        decimal sum = 0;
        for (int i = 0; i < candles.Count; i++)
        {
            sum += candles[i].Close;
            if (i >= period) sum -= candles[i - period].Close;
            if (i >= period - 1) result[i] = sum / period;
        }
        return result;
    }

    public static decimal?[] Ema(IReadOnlyList<Candle> candles, int period)
    {
        if (period < 1 || period > 5000) throw new ArgumentOutOfRangeException(nameof(period));
        var result = new decimal?[candles.Count];
        if (candles.Count < period) return result;
        decimal seed = 0;
        for (var i = 0; i < period; i++) seed += candles[i].Close;
        decimal previous = seed / period;
        result[period - 1] = previous;
        var alpha = 2m / (period + 1);
        for (var i = period; i < candles.Count; i++)
        {
            previous += alpha * (candles[i].Close - previous);
            result[i] = previous;
        }
        return result;
    }
}

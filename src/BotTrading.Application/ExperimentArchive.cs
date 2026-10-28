using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BotTrading.Core;

namespace BotTrading.Application;

// Immutable, compact versioned summary; full candle and equity histories are NOT duplicated.
public sealed record SavedExperiment(int Version, string Id, DateTimeOffset CreatedAt,
    string Symbol, int FastPeriod, int SlowPeriod, decimal InitialCapital,
    decimal FeeBps, decimal SlippageBps, string DatasetSha256,
    int CandleCount, DateOnly FirstDate, DateOnly LastDate,
    decimal ReturnPct, decimal BenchmarkReturnPct, decimal MaxDrawdownPct,
    int RoundTrips, RiskMetrics? Risk, string StrategyKind = "SMA");

public sealed record ExperimentView(SavedExperiment Experiment, bool DatasetMatchesCurrent);

public static class ExperimentArchive
{
    public const int MaxExperiments = 100;
    private static readonly Regex HashPattern = new("^[0-9A-F]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Fingerprint(MarketSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        // Date, OHLCV, source and adjustment metadata all affect comparability.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { series.Symbol, series.Provenance, series.Candles });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static SavedExperiment Capture(MarketSeries market, BacktestResult backtest)
    {
        if (market.Symbol != backtest.Symbol)
            throw new ArgumentException("Cannot archive a result for a different instrument.");
        return new(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
            market.Symbol, backtest.FastPeriod, backtest.SlowPeriod, backtest.InitialCapital,
            5m, 5m, Fingerprint(market), market.Candles.Count, market.Candles[0].Date,
            market.Candles[^1].Date, backtest.ReturnPct, backtest.BenchmarkReturnPct,
            backtest.MaxDrawdownPct, backtest.RoundTrips, backtest.Risk, backtest.StrategyKind);
    }

    public static bool IsValid(SavedExperiment? item) =>
        item is not null && item.Version == 1 && item.Id is not null &&
        Guid.TryParseExact(item.Id, "N", out _) &&
        item.Symbol is { Length: > 0 and <= 20 } &&
        item.DatasetSha256 is not null && HashPattern.IsMatch(item.DatasetSha256) &&
        item.StrategyKind is ("SMA" or "EMA") &&
        item.FastPeriod is >= 2 and < 250 && item.SlowPeriod is >= 3 and <= 250 &&
        item.FastPeriod < item.SlowPeriod && item.InitialCapital is >= 100m and <= 1_000_000_000m &&
        item.FeeBps is >= 0m and <= 1000m && item.SlippageBps is >= 0m and <= 1000m &&
        item.CandleCount is >= 1 and <= 100_000 && item.FirstDate <= item.LastDate &&
        item.MaxDrawdownPct is >= 0m and <= 100m && item.RoundTrips >= 0;
}

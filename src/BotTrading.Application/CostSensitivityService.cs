using BotTrading.Core;

namespace BotTrading.Application;

public sealed record CostScenario(decimal FeeBps, decimal SlippageBps, decimal ReturnPct,
    decimal BenchmarkReturnPct, decimal ExcessReturnPct, decimal MaxDrawdownPct,
    int RoundTrips, decimal FinalEquity);
public sealed record CostSensitivityReport(string Symbol, int FastPeriod, int SlowPeriod,
    decimal InitialCapital, decimal BestReturnPct, decimal WorstReturnPct,
    decimal ReturnRangePctPoints, IReadOnlyList<CostScenario> Scenarios);

/// <summary>
/// Replays identical historical prices and SMA parameters under static trading-cost
/// assumptions; results are in-sample stress tests, not probabilistic forecasts.
/// </summary>
public sealed class CostSensitivityService
{
    private static readonly decimal[] CostLevelsBps = [0m, 5m, 10m, 25m, 50m, 100m];

    public CostSensitivityReport Analyze(MarketSeries series, int fastPeriod, int slowPeriod,
        decimal initialCapital = 10_000m)
    {
        ArgumentNullException.ThrowIfNull(series);
        var engine = new Backtester();
        var cases = new List<CostScenario>();
        foreach (decimal cost in CostLevelsBps)
        {
            var run = engine.Run(series, new BacktestRequest(
                series.Symbol, fastPeriod, slowPeriod, initialCapital, cost, cost));
            cases.Add(new(cost, cost, run.ReturnPct, run.BenchmarkReturnPct,
                run.ExcessReturnPct, run.MaxDrawdownPct, run.RoundTrips, run.FinalEquity));
        }
        var best = cases.Max(x => x.ReturnPct);
        var worst = cases.Min(x => x.ReturnPct);
        return new(series.Symbol, fastPeriod, slowPeriod, initialCapital,
            best, worst, best - worst, cases);
    }
}

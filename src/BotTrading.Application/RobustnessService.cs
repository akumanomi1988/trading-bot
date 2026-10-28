using BotTrading.Core;

namespace BotTrading.Application;

public sealed record ValidationReport(string Symbol, DateOnly TrainStart, DateOnly TrainEnd,
    DateOnly TestStart, DateOnly TestEnd, int FastPeriod, int SlowPeriod,
    BacktestResult Training, BacktestResult OutOfSample, string StrategyKind = "SMA");

public sealed class RobustnessService
{
    // Fixed, chronological 70/30 holdout. No test observations are used for parameter selection.
    // Indicator warm-up may use pre-test closes, as those were known before test trading.
    public ValidationReport Validate(MarketSeries series, string strategyKind = "SMA", decimal feeBps = 5m, decimal slippageBps = 5m, decimal maxAllocationPct = 100m)
    {
        if (series.Candles.Count < 300)
            throw new ArgumentException("Out-of-sample validation requires at least 300 daily candles.");
        int testStartIndex = series.Candles.Count * 7 / 10;
        var training = new MarketSeries(series.Symbol, series.Name,
            series.Candles.Take(testStartIndex).ToArray());
        var candidates = new Backtester().Optimize(training, strategyKind: strategyKind, feeBps: feeBps, slippageBps: slippageBps, maxAllocationPct: maxAllocationPct);
        if (candidates.Count == 0) throw new InvalidOperationException("Not enough training candles for strategy validation.");
        var selected = candidates[0];
        var request = new BacktestRequest(series.Symbol, selected.FastPeriod, selected.SlowPeriod, FeeBps: feeBps, SlippageBps: slippageBps, StrategyKind: strategyKind, MaxAllocationPct: maxAllocationPct);
        var outOfSample = new Backtester().Run(series, request, series.Candles[testStartIndex].Date);
        return new(series.Symbol, training.Candles[0].Date, training.Candles[^1].Date,
            series.Candles[testStartIndex].Date, series.Candles[^1].Date,
            selected.FastPeriod, selected.SlowPeriod, selected, outOfSample, strategyKind);
    }
}

// Expanding training windows and strictly disjoint forward test segments.
// Every parameter selection is restricted to observations before the fold's test start.
public sealed record WalkForwardFold(int Number, DateOnly TrainingStart, DateOnly TrainingEnd,
    DateOnly TestStart, DateOnly TestEnd, int FastPeriod, int SlowPeriod,
    decimal TrainingReturnPct, decimal TestReturnPct, decimal TestMaxDrawdownPct, int TestRoundTrips);

public sealed record WalkForwardReport(string Symbol, int FoldCount, decimal PositiveFoldPct,
    decimal MedianTestReturnPct, IReadOnlyList<WalkForwardFold> Folds, string StrategyKind = "SMA");

public sealed class WalkForwardService
{
    public WalkForwardReport Validate(MarketSeries series, int foldCount = 4, string strategyKind = "SMA",
        decimal feeBps = 5m, decimal slippageBps = 5m, decimal maxAllocationPct = 100m)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (foldCount is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(foldCount), "Fold count must be between 2 and 8.");
        if (series.Candles.Count < 400 || (series.Candles.Count / 2) < 201)
            throw new ArgumentException("Walk-forward validation requires at least 402 candles.");

        int total = series.Candles.Count;
        int initialTrainCount = total / 2;
        int remaining = total - initialTrainCount;
        if (remaining / foldCount < 20)
            throw new ArgumentException("Each test fold needs at least 20 candles.");

        var results = new List<WalkForwardFold>(foldCount);
        var engine = new Backtester();
        for (int fold = 0; fold < foldCount; fold++)
        {
            int testStart = initialTrainCount + (int)((long)remaining * fold / foldCount);
            int testEndExclusive = initialTrainCount + (int)((long)remaining * (fold + 1) / foldCount);

            // Build the train-only set BEFORE optimization. Never pass future closes into Optimize.
            var train = new MarketSeries(series.Symbol, series.Name,
                series.Candles.Take(testStart).ToArray());
            var candidates = engine.Optimize(train, strategyKind: strategyKind, feeBps: feeBps, slippageBps: slippageBps, maxAllocationPct: maxAllocationPct);
            if (candidates.Count == 0)
                throw new InvalidOperationException("No eligible in-sample parameters in walk-forward fold.");
            var chosen = candidates[0];

            // Limit the evaluation history to this fold, preserving preceding bars only for
            // indicator warm-up. A signal formed on a future close cannot influence the test.
            var historyToEndOfFold = new MarketSeries(series.Symbol, series.Name,
                series.Candles.Take(testEndExclusive).ToArray());
            var parameters = new BacktestRequest(series.Symbol, chosen.FastPeriod, chosen.SlowPeriod, FeeBps: feeBps, SlippageBps: slippageBps, StrategyKind: strategyKind, MaxAllocationPct: maxAllocationPct);
            var test = engine.Run(historyToEndOfFold, parameters, series.Candles[testStart].Date);
            if (test.Equity.Count != testEndExclusive - testStart)
                throw new InvalidOperationException("Walk-forward fold has an unexpected evaluation range.");
            results.Add(new(fold + 1, train.Candles[0].Date, train.Candles[^1].Date,
                historyToEndOfFold.Candles[testStart].Date, historyToEndOfFold.Candles[^1].Date,
                chosen.FastPeriod, chosen.SlowPeriod, chosen.ReturnPct, test.ReturnPct,
                test.MaxDrawdownPct, test.RoundTrips));
        }

        var sorted = results.Select(x => x.TestReturnPct).OrderBy(x => x).ToArray();
        decimal median = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2
            : sorted[sorted.Length / 2];
        decimal positive = 100m * results.Count(x => x.TestReturnPct > 0) / results.Count;
        return new(series.Symbol, foldCount, decimal.Round(positive, 2), decimal.Round(median, 2), results, strategyKind);
    }
}

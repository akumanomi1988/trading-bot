using BotTrading.Core;

namespace BotTrading.Application;

public sealed record ValidationReport(string Symbol, DateOnly TrainStart, DateOnly TrainEnd,
    DateOnly TestStart, DateOnly TestEnd, int FastPeriod, int SlowPeriod,
    BacktestResult Training, BacktestResult OutOfSample);

public sealed class RobustnessService
{
    // Fixed, chronological 70/30 holdout. No test observations are used for parameter selection.
    // Indicator warm-up may use pre-test closes, as those were known before test trading.
    public ValidationReport Validate(MarketSeries series)
    {
        if (series.Candles.Count < 300)
            throw new ArgumentException("Out-of-sample validation requires at least 300 daily candles.");
        int testStartIndex = series.Candles.Count * 7 / 10;
        var training = new MarketSeries(series.Symbol, series.Name,
            series.Candles.Take(testStartIndex).ToArray());
        var candidates = new Backtester().Optimize(training);
        if (candidates.Count == 0) throw new InvalidOperationException("Not enough training candles for strategy validation.");
        var selected = candidates[0];
        var request = new BacktestRequest(series.Symbol, selected.FastPeriod, selected.SlowPeriod);
        var outOfSample = new Backtester().Run(series, request, series.Candles[testStartIndex].Date);
        return new(series.Symbol, training.Candles[0].Date, training.Candles[^1].Date,
            series.Candles[testStartIndex].Date, series.Candles[^1].Date,
            selected.FastPeriod, selected.SlowPeriod, selected, outOfSample);
    }
}

public sealed record WalkForwardWindow(int Number, DateOnly TrainStart, DateOnly TrainEnd,
    DateOnly TestStart, DateOnly TestEnd, int FastPeriod, int SlowPeriod,
    BacktestResult Training, BacktestResult OutOfSample);

public sealed record WalkForwardReport(string Symbol, int FoldCount, decimal MedianTestReturnPct,
    decimal PositiveFoldPct, decimal WorstTestDrawdownPct, IReadOnlyList<WalkForwardWindow> Windows);

public sealed class WalkForwardService
{
    // Expanding training sets with consecutive, disjoint test intervals. Each test run starts
    // flat and uses previous closes only for indicator warm-up, not model selection.
    public WalkForwardReport Validate(MarketSeries series, int folds = 4)
    {
        if (folds is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(folds));
        var candles = series.Candles;
        if (candles.Count < 500) throw new ArgumentException("Walk-forward validation requires at least 500 daily candles.");
        int initialTrain = Math.Max(250, candles.Count / 2);
        int remaining = candles.Count - initialTrain;
        if (remaining / folds < 30) throw new ArgumentException("Each test fold must have at least 30 observations.");
        var backtester = new Backtester();
        var windows = new List<WalkForwardWindow>(folds);
        for (int fold = 0; fold < folds; fold++)
        {
            int testStart = initialTrain + remaining * fold / folds;
            int testEnd = initialTrain + remaining * (fold + 1) / folds;
            var training = new MarketSeries(series.Symbol, series.Name, candles.Take(testStart).ToArray());
            var best = backtester.Optimize(training).FirstOrDefault()
                ?? throw new InvalidOperationException("No valid parameter candidates for fold.");
            var observed = new MarketSeries(series.Symbol, series.Name, candles.Take(testEnd).ToArray());
            var test = backtester.Run(observed,
                new BacktestRequest(series.Symbol, best.FastPeriod, best.SlowPeriod), candles[testStart].Date);
            windows.Add(new(fold + 1, candles[0].Date, candles[testStart - 1].Date,
                candles[testStart].Date, candles[testEnd - 1].Date,
                best.FastPeriod, best.SlowPeriod, best, test));
        }
        var orderedReturns = windows.Select(x => x.OutOfSample.ReturnPct).Order().ToArray();
        decimal median = orderedReturns.Length % 2 == 0
            ? (orderedReturns[orderedReturns.Length / 2 - 1] + orderedReturns[orderedReturns.Length / 2]) / 2
            : orderedReturns[orderedReturns.Length / 2];
        return new(series.Symbol, windows.Count, decimal.Round(median, 2),
            decimal.Round(100m * windows.Count(x => x.OutOfSample.ReturnPct > 0) / windows.Count, 2),
            windows.Max(x => x.OutOfSample.MaxDrawdownPct), windows);
    }
}


// Expanding training windows and strictly disjoint forward test segments.
// Every parameter selection is restricted to observations before the fold's test start.
public sealed record WalkForwardFold(int Number, DateOnly TrainingStart, DateOnly TrainingEnd,
    DateOnly TestStart, DateOnly TestEnd, int FastPeriod, int SlowPeriod,
    decimal TrainingReturnPct, decimal TestReturnPct, decimal TestMaxDrawdownPct, int TestRoundTrips);

public sealed record WalkForwardReport(string Symbol, int FoldCount, decimal PositiveFoldPct,
    decimal MedianTestReturnPct, IReadOnlyList<WalkForwardFold> Folds);

public sealed class WalkForwardService
{
    public WalkForwardReport Validate(MarketSeries series, int foldCount = 4)
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
            var candidates = engine.Optimize(train);
            if (candidates.Count == 0)
                throw new InvalidOperationException("No eligible in-sample parameters in walk-forward fold.");
            var chosen = candidates[0];

            // Limit the evaluation history to this fold, preserving preceding bars only for
            // indicator warm-up. A signal formed on a future close cannot influence the test.
            var historyToEndOfFold = new MarketSeries(series.Symbol, series.Name,
                series.Candles.Take(testEndExclusive).ToArray());
            var parameters = new BacktestRequest(series.Symbol, chosen.FastPeriod, chosen.SlowPeriod);
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
        return new(series.Symbol, foldCount, decimal.Round(positive, 2), decimal.Round(median, 2), results);
    }
}

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

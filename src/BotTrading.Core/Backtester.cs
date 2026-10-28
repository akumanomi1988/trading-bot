namespace BotTrading.Core;

public sealed record BacktestRequest(string Symbol, int FastPeriod, int SlowPeriod, decimal InitialCapital = 10_000m,
    decimal FeeBps = 5m, decimal SlippageBps = 5m, string StrategyKind = "SMA", decimal MaxAllocationPct = 100m);
public sealed record EquityPoint(DateOnly Date, decimal Equity);
public sealed record Fill(DateOnly Date, string Side, int Quantity, decimal Price, decimal Fee, string Reason);
public sealed record BacktestResult(string Symbol, int FastPeriod, int SlowPeriod, decimal InitialCapital,
    decimal FinalEquity, decimal ReturnPct, decimal MaxDrawdownPct, int RoundTrips, decimal WinRatePct,
    IReadOnlyList<Fill> Fills, IReadOnlyList<EquityPoint> Equity,
    decimal BenchmarkReturnPct = 0m, decimal ExcessReturnPct = 0m, RiskMetrics? Risk = null, string StrategyKind = "SMA", decimal FeeBps = 5m, decimal SlippageBps = 5m, decimal MaxAllocationPct = 100m);

public sealed class Backtester
{
    public BacktestResult Run(MarketSeries series, BacktestRequest request, DateOnly? tradingStart = null)
    {
        if (request.Symbol != series.Symbol) throw new ArgumentException("Mismatched symbol.");
        if (request.FastPeriod < 2 || request.SlowPeriod < 3 || request.FastPeriod >= request.SlowPeriod || request.SlowPeriod > 250)
            throw new ArgumentException("Expected 2 <= fast < slow <= 250.");
        if (request.InitialCapital < 100 || request.InitialCapital > 1_000_000_000m)
            throw new ArgumentOutOfRangeException(nameof(request.InitialCapital));
        if (request.FeeBps < 0 || request.FeeBps > 1000 || request.SlippageBps < 0 || request.SlippageBps > 1000)
            throw new ArgumentOutOfRangeException(nameof(request.FeeBps), "Costs must be 0-1000 basis points.");
        if (request.MaxAllocationPct <= 0m || request.MaxAllocationPct > 100m)
            throw new ArgumentOutOfRangeException(nameof(request.MaxAllocationPct));
        if (request.StrategyKind is not ("SMA" or "EMA"))
            throw new ArgumentException("Strategy kind must be SMA or EMA.", nameof(request.StrategyKind));
        var bars = series.Candles;
        int startIndex = 0;
        if (tradingStart is not null)
        {
            while (startIndex < bars.Count && bars[startIndex].Date < tradingStart.Value) startIndex++;
            if (startIndex >= bars.Count) throw new ArgumentException("Trading start is after the last candle.");
        }
        var fast = request.StrategyKind == "EMA" ? Indicators.Ema(bars, request.FastPeriod) : Indicators.Sma(bars, request.FastPeriod);
        var slow = request.StrategyKind == "EMA" ? Indicators.Ema(bars, request.SlowPeriod) : Indicators.Sma(bars, request.SlowPeriod);
        decimal cash = request.InitialCapital, peak = cash, maxDrawdown = 0;
        int shares = 0, wins = 0, completed = 0, investedSessions = 0;
        decimal entryCost = 0;
        var fills = new List<Fill>();
        var equity = new List<EquityPoint>();
        for (int i = startIndex; i < bars.Count; i++)
        {
            // Only yesterday's completed closing data may generate today's market-open order.
            if (i >= 2 && fast[i - 1].HasValue && slow[i - 1].HasValue &&
                fast[i - 2].HasValue && slow[i - 2].HasValue)
            {
                bool buy = fast[i - 2] <= slow[i - 2] && fast[i - 1] > slow[i - 1];
                bool sell = fast[i - 2] >= slow[i - 2] && fast[i - 1] < slow[i - 1];
                if (buy && shares == 0)
                {
                    var px = bars[i].Open * (1 + request.SlippageBps / 10000);
                    decimal allocationBudget = cash * request.MaxAllocationPct / 100m;
                    int qty = (int)Math.Min(int.MaxValue, decimal.Floor(allocationBudget / (px * (1m + request.FeeBps / 10000m))));
                    if (qty > 0)
                    {
                        var fee = qty * px * request.FeeBps / 10000;
                        entryCost = qty * px + fee;
                        cash -= entryCost;
                        shares = qty;
                        fills.Add(new(bars[i].Date, "BUY", qty, px, fee, $"{request.StrategyKind} cross at previous close"));
                    }
                }
                else if (sell && shares > 0)
                {
                    Close(bars[i].Date, bars[i].Open, $"{request.StrategyKind} cross at previous close");
                }
            }
            // Count sessions with intraday exposure, including terminal liquidation day.
            if (shares > 0) investedSessions++;
            if (i == bars.Count - 1 && shares > 0)
                Close(bars[i].Date, bars[i].Close, "End-of-sample liquidation at close");
            var account = cash + shares * bars[i].Close;
            if (account > peak) peak = account;
            var drawdown = peak <= 0 ? 0 : (peak - account) / peak * 100;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            equity.Add(new(bars[i].Date, decimal.Round(account, 2)));
        }
        var final = equity[^1].Equity;
        // Fair passive benchmark: acquire whole shares at the first trading-day open,
        // hold without rebalancing, liquidate at the final close. Apply identical
        // slippage and fees. OOS tests start this benchmark at their own test date.
        var benchmarkBuy = bars[startIndex].Open * (1m + request.SlippageBps / 10000m);
        var benchmarkShares = (int)Math.Min(int.MaxValue,
            decimal.Floor(request.InitialCapital / (benchmarkBuy * (1m + request.FeeBps / 10000m))));
        var benchmarkBuyFee = benchmarkShares * benchmarkBuy * request.FeeBps / 10000m;
        var benchmarkCash = request.InitialCapital - benchmarkShares * benchmarkBuy - benchmarkBuyFee;
        var benchmarkSell = bars[^1].Close * (1m - request.SlippageBps / 10000m);
        var benchmarkSellFee = benchmarkShares * benchmarkSell * request.FeeBps / 10000m;
        var benchmarkFinal = benchmarkCash + benchmarkShares * benchmarkSell - benchmarkSellFee;
        var benchmarkReturn = decimal.Round((benchmarkFinal / request.InitialCapital - 1m) * 100m, 2);
        var strategyReturn = decimal.Round((final / request.InitialCapital - 1m) * 100m, 2);
        return new(series.Symbol, request.FastPeriod, request.SlowPeriod, request.InitialCapital,
            final, strategyReturn,
            decimal.Round(maxDrawdown, 2), completed,
            completed == 0 ? 0 : decimal.Round((decimal)wins / completed * 100, 2), fills, equity,
            benchmarkReturn, strategyReturn - benchmarkReturn,
            RiskAnalytics.Analyze(equity, investedSessions), request.StrategyKind, request.FeeBps, request.SlippageBps, request.MaxAllocationPct);

        void Close(DateOnly date, decimal rawPrice, string reason)
        {
            var px = rawPrice * (1 - request.SlippageBps / 10000);
            var fee = shares * px * request.FeeBps / 10000;
            var proceeds = shares * px - fee;
            cash += proceeds;
            if (proceeds > entryCost) wins++;
            completed++;
            fills.Add(new(date, "SELL", shares, px, fee, reason));
            shares = 0;
            entryCost = 0;
        }
    }

    // In-sample grid search is exploratory, NOT a validated out-of-sample strategy.
    public IReadOnlyList<BacktestResult> Optimize(MarketSeries series, decimal initialCapital = 10_000m,
        string strategyKind = "SMA", decimal feeBps = 5m, decimal slippageBps = 5m, decimal maxAllocationPct = 100m)
    {
        var results = new List<BacktestResult>();
        foreach (int slow in new[] { 15, 20, 30, 50, 100, 200 })
            foreach (int fast in new[] { 3, 5, 10, 15, 20, 30, 50 })
                if (fast < slow && series.Candles.Count > slow + 3)
                    results.Add(Run(series, new(series.Symbol, fast, slow, initialCapital, feeBps, slippageBps, strategyKind, maxAllocationPct)));
        return results.OrderByDescending(x => x.FinalEquity).Take(12).ToArray();
    }
}

namespace BotTrading.Core;

public sealed record BacktestRequest(string Symbol, int FastPeriod, int SlowPeriod, decimal InitialCapital = 10_000m,
    decimal FeeBps = 5m, decimal SlippageBps = 5m);
public sealed record EquityPoint(DateOnly Date, decimal Equity);
public sealed record Fill(DateOnly Date, string Side, int Quantity, decimal Price, decimal Fee, string Reason);
public sealed record BacktestResult(string Symbol, int FastPeriod, int SlowPeriod, decimal InitialCapital,
    decimal FinalEquity, decimal ReturnPct, decimal MaxDrawdownPct, int RoundTrips, decimal WinRatePct,
    IReadOnlyList<Fill> Fills, IReadOnlyList<EquityPoint> Equity);

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
        var bars = series.Candles;
        int startIndex = 0;
        if (tradingStart is not null)
        {
            while (startIndex < bars.Count && bars[startIndex].Date < tradingStart.Value) startIndex++;
            if (startIndex >= bars.Count) throw new ArgumentException("Trading start is after the last candle.");
        }
        var fast = Indicators.Sma(bars, request.FastPeriod);
        var slow = Indicators.Sma(bars, request.SlowPeriod);
        decimal cash = request.InitialCapital, peak = cash, maxDrawdown = 0;
        int shares = 0, wins = 0, completed = 0;
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
                    int qty = (int)Math.Min(int.MaxValue, decimal.Floor(cash / (px * (1 + request.FeeBps / 10000))));
                    if (qty > 0)
                    {
                        var fee = qty * px * request.FeeBps / 10000;
                        entryCost = qty * px + fee;
                        cash -= entryCost;
                        shares = qty;
                        fills.Add(new(bars[i].Date, "BUY", qty, px, fee, "SMA cross at previous close"));
                    }
                }
                else if (sell && shares > 0)
                {
                    Close(bars[i].Date, bars[i].Open, "SMA cross at previous close");
                }
            }
            if (i == bars.Count - 1 && shares > 0)
                Close(bars[i].Date, bars[i].Close, "End-of-sample liquidation at close");
            var account = cash + shares * bars[i].Close;
            if (account > peak) peak = account;
            var drawdown = peak <= 0 ? 0 : (peak - account) / peak * 100;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            equity.Add(new(bars[i].Date, decimal.Round(account, 2)));
        }
        var final = equity[^1].Equity;
        return new(series.Symbol, request.FastPeriod, request.SlowPeriod, request.InitialCapital,
            final, decimal.Round((final / request.InitialCapital - 1) * 100, 2),
            decimal.Round(maxDrawdown, 2), completed,
            completed == 0 ? 0 : decimal.Round((decimal)wins / completed * 100, 2), fills, equity);

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
    public IReadOnlyList<BacktestResult> Optimize(MarketSeries series, decimal initialCapital = 10_000m)
    {
        var results = new List<BacktestResult>();
        foreach (int slow in new[] { 15, 20, 30, 50, 100, 200 })
            foreach (int fast in new[] { 3, 5, 10, 15, 20, 30, 50 })
                if (fast < slow && series.Candles.Count > slow + 3)
                    results.Add(Run(series, new(series.Symbol, fast, slow, initialCapital)));
        return results.OrderByDescending(x => x.FinalEquity).Take(12).ToArray();
    }
}

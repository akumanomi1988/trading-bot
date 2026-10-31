namespace BotTrading.Core;

/// <summary>
/// Independent equal-capital strategy sleeves combined into a single historical portfolio.
/// No cross-asset rebalancing, currency conversion, dividends, or split reconstruction.
/// All series must already have independently verified adjusted prices in the same currency.
/// </summary>
public sealed record PortfolioBacktestResult(string CurrencyCode, DateOnly Start, DateOnly End,
    decimal InitialCapital, decimal FinalEquity, decimal ReturnPct, decimal BenchmarkReturnPct,
    decimal ExcessReturnPct, decimal MaxDrawdownPct, RiskMetrics Risk,
    IReadOnlyList<BacktestResult> Legs, IReadOnlyList<EquityPoint> Equity);

public sealed class PortfolioBacktester
{
    private readonly Backtester _engine = new();

    public PortfolioBacktestResult Run(IReadOnlyList<MarketSeries> series, int fast, int slow,
        decimal initialCapital = 10_000m, string strategyKind = "SMA",
        decimal feeBps = 5m, decimal slippageBps = 5m, decimal maxAllocationPct = 100m)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count is < 2 or > 10) throw new ArgumentException("Expected 2-10 distinct portfolio instruments.");
        if (series.Any(x => x is null) || series.Select(x => x.Symbol).Distinct(StringComparer.Ordinal).Count() != series.Count)
            throw new ArgumentException("Duplicate or missing portfolio instruments are not allowed.");
        if (initialCapital < series.Count * 100m || initialCapital > 1_000_000_000m)
            throw new ArgumentOutOfRangeException(nameof(initialCapital));
        var currency = series[0].Provenance.CurrencyCode;
        if (currency is null || series.Any(x => x.Provenance.CurrencyCode != currency))
            throw new InvalidOperationException("Portfolio backtesting requires verified matching instrument currencies; FX conversion is not implemented.");
        if (series.Any(x => x.Provenance.AdjustmentStatus != "ADJUSTED"))
            throw new InvalidOperationException("Portfolio backtesting requires user-verified adjusted price histories for every instrument; corporate actions are not reconstructed.");

        var first = series.Max(x => x.Candles[0].Date);
        var last = series.Min(x => x.Candles[^1].Date);
        if (last <= first) throw new ArgumentException("Portfolio instruments have no overlapping trading interval.");
        var comparable = series.Select(x => new MarketSeries(x.Symbol, x.Name,
            x.Candles.Where(c => c.Date <= last).ToArray(), x.Provenance)).ToArray();
        // Require a meaningful shared period; never extrapolate prices into a non-overlapping era.
        var sharedDates = comparable.Select(x => x.Candles.Where(c => c.Date >= first)
            .Select(c => c.Date).ToHashSet()).ToArray();
        if (sharedDates[0].Count(date => sharedDates.Skip(1).All(s => s.Contains(date))) < 10)
            throw new ArgumentException("At least ten common trading sessions are required for portfolio backtesting.");

        decimal perLeg = decimal.Round(initialCapital / comparable.Length, 2);
        var budgets = Enumerable.Range(0, comparable.Length)
            .Select(i => i == comparable.Length - 1 ? initialCapital - perLeg * i : perLeg).ToArray();
        var legs = comparable.Select((market, i) => _engine.Run(market,
            new BacktestRequest(market.Symbol, fast, slow, budgets[i], feeBps, slippageBps, strategyKind, maxAllocationPct),
            tradingStart: first)).ToArray();
        var dates = legs.SelectMany(x => x.Equity.Select(p => p.Date)).Distinct().OrderBy(x => x).ToArray();
        var values = budgets.ToArray();
        var positions = new int[legs.Length];
        var equityIndex = new int[legs.Length];
        var fillIndex = new int[legs.Length];
        var curve = new List<EquityPoint>(dates.Length);
        decimal peak = initialCapital, maxDrawdown = 0;
        int exposedSessions = 0;
        foreach (var date in dates)
        {
            bool exposed = positions.Any(x => x > 0);
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                while (equityIndex[i] < leg.Equity.Count && leg.Equity[equityIndex[i]].Date == date)
                    values[i] = leg.Equity[equityIndex[i]++].Equity;
                while (fillIndex[i] < leg.Fills.Count && leg.Fills[fillIndex[i]].Date == date)
                {
                    var fill = leg.Fills[fillIndex[i]++];
                    positions[i] += fill.Side == "BUY" ? fill.Quantity : -fill.Quantity;
                    if (positions[i] < 0) throw new InvalidOperationException("Negative portfolio position.");
                    if (fill.Side == "BUY") exposed = true;
                }
                if (positions[i] > 0) exposed = true;
            }
            if (exposed) exposedSessions++;
            var total = values.Sum();
            if (total > peak) peak = total;
            var drawdown = peak <= 0m ? 0m : (peak - total) / peak * 100m;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            curve.Add(new EquityPoint(date, decimal.Round(total, 2)));
        }
        var finalEquity = curve[^1].Equity;
        decimal benchmark = 0m;
        for (int i = 0; i < legs.Length; i++)
            benchmark += budgets[i] / initialCapital * legs[i].BenchmarkReturnPct;
        var returnPct = decimal.Round((finalEquity / initialCapital - 1m) * 100m, 2);
        var benchmarkPct = decimal.Round(benchmark, 2);
        return new PortfolioBacktestResult(currency, dates[0], dates[^1], initialCapital, finalEquity,
            returnPct, benchmarkPct, returnPct - benchmarkPct, decimal.Round(maxDrawdown, 2),
            RiskAnalytics.Analyze(curve, exposedSessions), legs, curve);
    }
}

namespace BotTrading.Core;

/// <summary>
/// Descriptive risk measures for a DAILY close-to-close strategy equity curve.
/// Assumes 252 trading sessions per year and a zero risk-free rate.
/// No interpolation, calendar correction or corporate-action adjustment is performed.
/// </summary>
public sealed record RiskMetrics(
    decimal? AnnualizedVolatilityPct,
    decimal? SharpeRatio,
    decimal ExposurePct,
    decimal? WorstSessionPct,
    int ReturnObservations);

public static class RiskAnalytics
{
    public static RiskMetrics Analyze(IReadOnlyList<EquityPoint> equity, int investedSessions)
    {
        ArgumentNullException.ThrowIfNull(equity);
        if (equity.Count == 0) throw new ArgumentException("An equity curve cannot be empty.", nameof(equity));
        if (investedSessions < 0 || investedSessions > equity.Count)
            throw new ArgumentOutOfRangeException(nameof(investedSessions));
        for (int i = 0; i < equity.Count; i++)
        {
            if (equity[i].Equity <= 0)
                throw new ArgumentException("Daily equity must be strictly positive.", nameof(equity));
            if (i > 0 && equity[i].Date <= equity[i - 1].Date)
                throw new ArgumentException("Daily equity dates must increase strictly.", nameof(equity));
        }

        var exposure = decimal.Round((decimal)investedSessions / equity.Count * 100m, 2);
        if (equity.Count == 1) return new(null, null, exposure, null, 0);

        // Welford's online sample variance: stable even for near-flat daily returns.
        // Each interval is ONE observed trading session, not a calendar day.
        int n = 0;
        double mean = 0d, m2 = 0d, worst = double.PositiveInfinity;
        for (int i = 1; i < equity.Count; i++)
        {
            double daily = (double)equity[i].Equity / (double)equity[i - 1].Equity - 1d;
            if (!double.IsFinite(daily)) throw new ArgumentException("Nonfinite equity return.", nameof(equity));
            ++n;
            double delta = daily - mean;
            mean += delta / n;
            m2 += delta * (daily - mean);
            worst = Math.Min(worst, daily);
        }

        decimal worstPct = decimal.Round((decimal)(worst * 100d), 2);
        if (n < 2) return new(null, null, exposure, worstPct, n);

        double sampleDeviation = Math.Sqrt(Math.Max(0d, m2 / (n - 1)));
        double annualization = Math.Sqrt(252d);
        decimal volatility = decimal.Round((decimal)(sampleDeviation * annualization * 100d), 2);
        // Sharpe is undefined for a constant equity curve, not an infinite reward/risk ratio.
        decimal? sharpe = sampleDeviation < 1e-12d
            ? null
            : decimal.Round((decimal)(mean / sampleDeviation * annualization), 3);
        return new(volatility, sharpe, exposure, worstPct, n);
    }
}

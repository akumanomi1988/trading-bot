namespace BotTrading.Core;

/// <summary>
/// Daily OHLC-based monthly dollar-cost averaging. This does NOT model split adjustments,
/// dividends, FX, market holidays or fractional share purchase. Never treat unadjusted
/// historical prices as an investment-performance record.
/// </summary>
public sealed record DcaPoint(DateOnly Month, DateOnly? ExecutionDate, decimal Contributions,
    decimal Cash, int Shares, decimal MarkPrice, decimal Equity, decimal ReturnPct);
public sealed record DcaReport(string Symbol, decimal MonthlyContribution, DateOnly StartMonth, DateOnly EndMonth,
    int MonthCount, decimal Contributions, decimal FinalEquity, decimal Gain, decimal ReturnPct, int Shares,
    IReadOnlyList<DcaPoint> Points);

public sealed class DcaSimulator
{
    public DcaReport Run(MarketSeries series, decimal monthlyContribution = 100m,
        DateOnly? start = null, DateOnly? end = null, decimal feeBps = 5m, decimal slippageBps = 5m)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (monthlyContribution is < 1m or > 100_000m)
            throw new ArgumentOutOfRangeException(nameof(monthlyContribution), "Monthly contributions must be 1-100000.");
        if (feeBps is < 0m or > 1000m || slippageBps is < 0m or > 1000m)
            throw new ArgumentOutOfRangeException(nameof(feeBps), "Costs must be 0-1000 basis points.");
        var candles = series.Candles;
        DateOnly first = candles[0].Date;
        DateOnly last = end.HasValue && end.Value < candles[^1].Date ? end.Value : candles[^1].Date;
        DateOnly from = start ?? first;
        if (from < first && (from.Year < first.Year || from.Month < first.Month && from.Year == first.Year))
            throw new ArgumentException("Requested start precedes the available historical month. Earlier contributions cannot be reconstructed.", nameof(start));
        if (from > last) throw new ArgumentException("Requested DCA start must precede the final visible historical date.");
        var firstMonth = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(last.Year, last.Month, 1);
        decimal cash = 0, contributions = 0, mark = 0;
        int totalShares = 0;
        int index = 0;
        while (index < candles.Count && candles[index].Date < from) index++;
        var output = new List<DcaPoint>();
        for (DateOnly month = firstMonth; ; month = month.AddMonths(1))
        {
            contributions += monthlyContribution;
            cash += monthlyContribution;
            DateOnly? execution = null;
            if (index < candles.Count && candles[index].Date <= last &&
                candles[index].Date.Year == month.Year && candles[index].Date.Month == month.Month)
            {
                var opening = candles[index].Open * (1m + slippageBps / 10000m);
                var unitCost = opening * (1m + feeBps / 10000m);
                int remainingCapacity = int.MaxValue - totalShares;
                int quantity = remainingCapacity == 0 ? 0 : unitCost <= cash / remainingCapacity
                    ? remainingCapacity : (int)decimal.Floor(cash / unitCost);
                if (quantity > 0)
                {
                    var notional = quantity * opening;
                    var fee = notional * feeBps / 10000m;
                    cash -= notional + fee;
                    totalShares += quantity;
                    execution = candles[index].Date;
                }
            }
            while (index < candles.Count && candles[index].Date <= last &&
                candles[index].Date.Year == month.Year && candles[index].Date.Month == month.Month)
            {
                mark = candles[index].Close;
                index++;
            }
            decimal equity = cash + totalShares * mark;
            decimal resultPct = decimal.Round((equity / contributions - 1m) * 100m, 2);
            output.Add(new DcaPoint(month, execution, decimal.Round(contributions, 2),
                decimal.Round(cash, 2), totalShares, mark, decimal.Round(equity, 2), resultPct));
            if (month == lastMonth) break;
        }
        var finalEquity = output[^1].Equity;
        var gain = decimal.Round(finalEquity - contributions, 2);
        return new DcaReport(series.Symbol, monthlyContribution, firstMonth, lastMonth, output.Count,
            contributions, finalEquity, gain,
            decimal.Round((finalEquity / contributions - 1m) * 100m, 2), totalShares, output);
    }
}

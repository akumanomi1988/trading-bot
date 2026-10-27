namespace BotTrading.Core;

/// <summary>Heuristic data integrity checks. Missing weekdays may be legitimate exchange holidays.
/// Large unadjusted price moves may be corporate actions, not data errors.</summary>
public sealed record CalendarGap(DateOnly After, DateOnly Before, int MissingWeekdays);
public sealed record PriceJump(DateOnly Date, decimal PreviousClose, decimal Close, decimal AbsoluteChangePct);
public sealed record DataQualityReport(string Symbol, DateOnly First, DateOnly Last, int CandleCount,
    int CandidateMissingWeekdays, int CalendarGapCount, int ZeroVolumeBars, int LargePriceMoveCount,
    IReadOnlyList<CalendarGap> LargestCalendarGaps, IReadOnlyList<PriceJump> LargestPriceMoves);

public sealed class DataQualityService
{
    public DataQualityReport Analyze(MarketSeries series, DateOnly? cutoff = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        var candles = series.Candles;
        int visibleCount = candles.Count;
        if (cutoff.HasValue)
        {
            // No reported metric or event is permitted to inspect samples after the replay cutoff.
            int lo = 0, hi = candles.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (candles[mid].Date <= cutoff.Value) lo = mid + 1;
                else hi = mid;
            }
            visibleCount = lo;
        }
        if (visibleCount == 0) throw new ArgumentException("No candles exist on or before the requested cutoff.", nameof(cutoff));

        int missingTotal = 0, gapCount = 0, zeroVolumeCount = 0, jumpCount = 0;
        var gaps = new List<CalendarGap>();
        var jumps = new List<PriceJump>();
        for (int i = 0; i < visibleCount; i++)
        {
            var current = candles[i];
            if (current.Volume == 0) zeroVolumeCount++;
            if (i == 0) continue;
            var previous = candles[i - 1];
            int missing = CountMissingWeekdays(previous.Date, current.Date);
            if (missing > 0)
            {
                missingTotal = checked(missingTotal + missing);
                gapCount++;
                gaps.Add(new CalendarGap(previous.Date, current.Date, missing));
            }
            if (Math.Abs(current.Close - previous.Close) > previous.Close * 0.25m)
            {
                jumpCount++;
                // Bound the displayed percentage for pathological but numerically valid OHLC input.
                double ratio = (double)current.Close / (double)previous.Close;
                double change = Math.Abs(ratio - 1d) * 100d;
                decimal pct = double.IsFinite(change) ? (decimal)Math.Min(1_000_000d, change) : 1_000_000m;
                jumps.Add(new PriceJump(current.Date, previous.Close, current.Close, decimal.Round(pct, 2)));
            }
        }
        return new DataQualityReport(series.Symbol, candles[0].Date, candles[visibleCount - 1].Date,
            visibleCount, missingTotal, gapCount, zeroVolumeCount, jumpCount,
            gaps.OrderByDescending(x => x.MissingWeekdays).ThenBy(x => x.Before).Take(10).ToArray(),
            jumps.OrderByDescending(x => x.AbsoluteChangePct).ThenBy(x => x.Date).Take(10).ToArray());
    }

    private static int CountMissingWeekdays(DateOnly left, DateOnly right)
    {
        int days = right.DayNumber - left.DayNumber - 1;
        if (days <= 0) return 0;
        int weeks = days / 7;
        int count = weeks * 5;
        for (int day = 1; day <= days % 7; day++)
        {
            var weekday = (DayOfWeek)(((int)left.DayOfWeek + day) % 7);
            if (weekday is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        }
        return count;
    }
}

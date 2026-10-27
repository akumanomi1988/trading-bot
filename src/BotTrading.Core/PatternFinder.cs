namespace BotTrading.Core;

public sealed record PatternMatch(string Symbol, DateOnly Start, DateOnly End, double Correlation, double Distance);
public sealed record PatternSearchResult(string QuerySymbol, DateOnly QueryStart, DateOnly QueryEnd, int Window, IReadOnlyList<PatternMatch> Matches);

public sealed class PatternFinder
{
    // The selected pattern uses returns, not nominal prices; only information through the cutoff date is examined.
    public PatternSearchResult Find(MarketSeries query, IReadOnlyCollection<MarketSeries> universe,
        int window, int maxResults = 12, int? queryEndIndex = null)
    {
        if (window < 5 || window > 250) throw new ArgumentOutOfRangeException(nameof(window));
        if (maxResults < 1 || maxResults > 100) throw new ArgumentOutOfRangeException(nameof(maxResults));
        int end = queryEndIndex ?? query.Candles.Count - 1;
        if (end < window - 1 || end >= query.Candles.Count) throw new ArgumentOutOfRangeException(nameof(queryEndIndex));
        int start = end - window + 1, count = window - 1;
        var q = Returns(query.Candles);
        var qReturns = q.AsSpan(start, count);
        double querySum = 0, querySq = 0;
        foreach (double value in qReturns) { querySum += value; querySq += value * value; }
        double queryVariance = querySq - querySum * querySum / count;
        var best = new PriorityQueue<PatternMatch, (double Score, double NegDistance)>();
        if (queryVariance <= 1e-24) return Result();

        foreach (var series in universe)
        {
            if (series.Candles.Count < window) continue;
            var s = Returns(series.Candles);
            double sampleSum = 0, sampleSq = 0;
            for (int j = 0; j < count; j++) { sampleSum += s[j]; sampleSq += s[j] * s[j]; }
            for (int i = 0; i + window <= series.Candles.Count; i++)
            {
                int candidateEnd = i + window - 1;
                if (i > 0)
                {
                    double leaving = s[i - 1], entering = s[i + count - 1];
                    sampleSum += entering - leaving;
                    sampleSq += entering * entering - leaving * leaving;
                }
                if (series.Symbol == query.Symbol && candidateEnd >= start) continue;
                if (series.Candles[candidateEnd].Date > query.Candles[end].Date) break;
                double sampleVariance = sampleSq - sampleSum * sampleSum / count;
                if (sampleVariance <= 1e-24) continue;
                double dot = 0;
                for (int j = 0; j < count; j++) dot += qReturns[j] * s[i + j];
                double correlation = Math.Clamp((dot - querySum * sampleSum / count) /
                    Math.Sqrt(queryVariance * sampleVariance), -1, 1);
                double distance = Math.Sqrt(Math.Max(0, (querySq + sampleSq - 2 * dot) / count));
                var result = new PatternMatch(series.Symbol, series.Candles[i].Date,
                    series.Candles[candidateEnd].Date, Math.Round(correlation, 6), Math.Round(distance, 8));
                var priority = (result.Correlation, -result.Distance);
                best.Enqueue(result, priority);
                if (best.Count > maxResults) best.Dequeue();
            }
        }
        return Result();

        PatternSearchResult Result() => new(query.Symbol, query.Candles[start].Date, query.Candles[end].Date, window,
            best.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Correlation)
                .ThenBy(x => x.Distance).ToArray());
    }

    private static double[] Returns(IReadOnlyList<Candle> bars)
    {
        var result = new double[bars.Count - 1];
        for (int i = 1; i < bars.Count; i++)
            result[i - 1] = (double)((bars[i].Close - bars[i - 1].Close) / bars[i - 1].Close);
        return result;
    }
}

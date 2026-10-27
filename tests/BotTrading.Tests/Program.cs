using System.Text;
using BotTrading.Application;
using BotTrading.Core;

int pass = 0, fail = 0;
void Check(string label, Action test)
{
    try { test(); Console.WriteLine($"PASS {label}"); pass++; }
    catch (Exception ex) { Console.WriteLine($"FAIL {label}: {ex.Message}"); fail++; }
}
void True(bool assertion, string? detail = null) { if (!assertion) throw new Exception(detail ?? "Assertion failed"); }
void Nearly(decimal actual, decimal expected, decimal tolerance = 0.000001m) => True(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}.");
void Throws<T>(Action run) where T : Exception
{
    try { run(); throw new Exception($"Expected {typeof(T).Name}."); }
    catch (T) { }
}
MarketSeries Series(string symbol, params decimal[] closes)
{
    var first = new DateOnly(2020, 1, 1);
    return new MarketSeries(symbol, symbol, closes.Select((close, i) =>
        new Candle(first.AddDays(i), close, close + 1, close - 1, close, 100m)).ToArray());
}
MarketSeries Csv(string csv, string symbol = "TEST") => CsvMarketData.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), symbol);

Check("market series sorts dates", () => {
    var a = Series("AA", 10, 11).Candles;
    var result = new MarketSeries("aa", "Demo", [a[1], a[0]]);
    True(result.Symbol == "AA" && result.Candles[0].Date < result.Candles[1].Date);
});
Check("duplicate dates rejected", () => {
    var candle = Series("AA", 12).Candles[0];
    Throws<ArgumentException>(() => new MarketSeries("AA", "", [candle, candle]));
});
Check("invalid OHLC rejected", () => Throws<ArgumentException>(() =>
    new MarketSeries("AA", "", [new(new DateOnly(2026, 1, 1), 100, 101, 99, 110, 5)])));
Check("invalid ticker rejected", () => Throws<ArgumentException>(() => Series("HTTP://X", 10)));
Check("CSV Stooq header", () => {
    var result = Csv("Date,Open,High,Low,Close,Volume\n2025-01-03,100,102,99,101,500\n2025-01-02,99,101,98,100,600\n");
    True(result.Candles.Count == 2 && result.Candles[0].Volume == 600);
});
Check("CSV semicolon and optional volume", () => {
    var result = Csv("date;open;high;low;close\n2025-01-03;100;101;99;100\n");
    True(result.Candles[0].Volume == 0);
});
Check("CSV malformed row rejected", () => Throws<FormatException>(() => Csv("date,open,high,low,close\nnotadate,2,3,1,2")));
Check("CSV missing headers rejected", () => Throws<FormatException>(() => Csv("timestamp,close\na,2")));
Check("CSV negative volume rejected", () => Throws<ArgumentException>(() => Csv("date,open,high,low,close,volume\n2025-01-03,100,101,99,100,-1")));
Check("SMA warmup", () => {
    var sma = Indicators.Sma(Series("AA", 10, 20, 30, 40).Candles, 3);
    True(sma[0] is null && sma[1] is null);
    Nearly(sma[2]!.Value, 20);
    Nearly(sma[3]!.Value, 30);
});
Check("EMA seeds from SMA", () => {
    var ema = Indicators.Ema(Series("AA", 10, 20, 30, 40).Candles, 3);
    True(ema[0] is null && ema[1] is null);
    Nearly(ema[2]!.Value, 20);
    Nearly(ema[3]!.Value, 30);
});
Check("pattern identical returns across different scales", () => {
    var a = Series("AA", 10, 11, 13, 12, 15, 14, 17, 19, 18);
    var b = Series("BB", 20, 22, 26, 24, 30, 28, 34, 38, 36);
    var matches = new PatternFinder().Find(a, [a, b], 8);
    True(matches.Matches.Any(x => x.Symbol == "BB" && x.Correlation > .99));
});
Check("pattern excludes overlapping self comparison", () => {
    var a = Series("AA", 10, 11, 13, 12, 15, 14, 17, 19);
    True(new PatternFinder().Find(a, [a], 8).Matches.Count == 0);
});
Check("pattern excludes future cross-asset data", () => {
    var a = Series("AA", 10, 11, 13, 12, 15, 14, 17, 19);
    var future = new MarketSeries("BB", "Future", Series("BB", 20, 22, 26, 24, 30, 28, 34, 38).Candles
        .Select(c => c with { Date = c.Date.AddYears(5) }).ToArray());
    True(new PatternFinder().Find(a, [a, future], 8).Matches.Count == 0);
});
Check("pattern constant returns not treated as matching", () => {
    var a = Series("AA", 10, 10, 10, 10, 10, 10, 10);
    var b = Series("BB", 20, 20, 20, 20, 20, 20, 20);
    True(new PatternFinder().Find(a, [a, b], 5).Matches.Count == 0);
});
Check("backtest validation fast lower slow", () => Throws<ArgumentException>(() =>
    new Backtester().Run(Series("AA", 10, 11, 12, 13), new("AA", 10, 3))));
Check("backtest stable market never fills", () => {
    var data = Series("AA", Enumerable.Repeat(10m, 40).ToArray());
    var result = new Backtester().Run(data, new("AA", 3, 7));
    True(result.Fills.Count == 0 && result.Equity.Count == 40);
    Nearly(result.FinalEquity, 10_000);
});
Check("backtest execution never on signal-close bar", () => {
    var closes = new decimal[] {10,9,8,7,6,8,12,16,18,20,18,15,11,8,6,12,18,22};
    var data = Series("AA", closes);
    var result = new Backtester().Run(data, new("AA", 2, 4));
    True(result.Fills.Any(x => x.Side == "BUY"), "Expected SMA cross.");
    var fast = Indicators.Sma(data.Candles, 2); var slow = Indicators.Sma(data.Candles, 4);
    for (int i = 0; i < result.Fills.Count; i++)
    {
        var fill = result.Fills[i];
        if (fill.Reason.Contains("End-of-sample")) continue;
        var index = Array.FindIndex(data.Candles.ToArray(), x => x.Date == fill.Date);
        True(index >= 2 && fast[index - 1].HasValue && slow[index - 1].HasValue);
        bool cross = fill.Side == "BUY"
            ? fast[index - 2] <= slow[index - 2] && fast[index - 1] > slow[index - 1]
            : fast[index - 2] >= slow[index - 2] && fast[index - 1] < slow[index - 1];
        True(cross, "Fill did not follow a previous-day cross.");
    }
});
Check("backtest initial capital conserved less costs", () => {
    var data = Series("AA", 10,9,8,7,6,8,12,16,18,20,18,15,11,8,6,12,18,22);
    var result = new Backtester().Run(data, new("AA", 2, 4));
    True(result.FinalEquity >= 0 && result.Fills.All(x=>x.Fee>=0));
    True(result.MaxDrawdownPct>=0 && result.MaxDrawdownPct<=100);
});
Check("paper broker buy+sell and exact cash", () => {
    var market = Series("AA", 100);
    var paper = new PaperBroker(new(1000, [], []));
    var bought = paper.Place(market, "BUY", 2, 0, 0);
    Nearly(paper.State.Cash, 800);
    True(bought.Price == 100 && paper.State.Positions[0].Quantity == 2);
    paper.Place(market, "SELL", 2, 0, 0);
    Nearly(paper.State.Cash, 1000);
    True(paper.State.Positions.Count == 0);
});
Check("paper broker rejects short sale", () => Throws<InvalidOperationException>(() =>
    new PaperBroker(new(1000, [], [])).Place(Series("AA",100), "SELL", 1)));
Check("paper broker rejects insufficient cash", () => Throws<InvalidOperationException>(() =>
    new PaperBroker(new(5, [], [])).Place(Series("AA",100), "BUY", 1)));
Check("paper brokerage persists and reloads", () => {
    string dir = Path.Combine(Path.GetTempPath(), "bt-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new WorkspaceStore(dir);
        var market = Series("AA", 10, 12, 11);
        store.Save(new([market], new(1000, [], [])));
        var state = store.Load();
        True(state.Series.Count == 1 && state.Series[0].Candles.Count == 3);
        Nearly(state.Series[0].Candles[2].Close, 11);
        Nearly(state.Paper.Cash,1000);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive:true); }
});
Check("paper market value uses last imported close", () => {
    var broker = new PaperBroker(new(800, [new Position("AA",2,100)], []));
    var state = broker.Snapshot([Series("AA", 100, 110)]);
    Nearly(state.Equity,1020);
    Nearly(state.UnrealizedPnL,20);
});
Check("optimization results never exceed grid", () => {
    var data = Series("AA", Enumerable.Range(0,300).Select(i=>10m+(i%23)).ToArray());
    var results = new Backtester().Optimize(data);
    True(results.Count <= 12 && results.All(r=>r.FastPeriod<r.SlowPeriod));
});
Check("out-of-sample split never selects using future data", () => {
    var candles = Enumerable.Range(0,350).Select(i => 100m+(i%41)+i/12m).ToArray();
    var original = Series("AA", candles);
    var modified = Series("AA", candles.Select((p,i) => i<245?p:100m+i%31).ToArray());
    var validator = new RobustnessService();
    var one = validator.Validate(original);
    var two = validator.Validate(modified);
    True(one.FastPeriod==two.FastPeriod && one.SlowPeriod==two.SlowPeriod);
    True(one.Training.FinalEquity == two.Training.FinalEquity);
    True(one.OutOfSample.Equity[0].Date == one.TestStart);
});
Check("out-of-sample requires 300 candles", () => Throws<ArgumentException>(() =>
    new RobustnessService().Validate(Series("AA", Enumerable.Repeat(100m,120).ToArray()))));
Check("walk-forward requires sufficient history", () => Throws<ArgumentException>(() =>
    new WalkForwardService().Validate(Series("AA", Enumerable.Repeat(100m, 350).ToArray()))));
Check("walk-forward validates fold count", () => Throws<ArgumentOutOfRangeException>(() =>
    new WalkForwardService().Validate(Series("AA", Enumerable.Repeat(100m, 500).ToArray()), 1)));
Check("walk-forward test segments are contiguous and do not overlap", () => {
    var data = Series("AA", Enumerable.Range(0, 600).Select(i => 100m + i % 43 + i / 12m).ToArray());
    var report = new WalkForwardService().Validate(data);
    True(report.Folds.Count == 4);
    int first = 300;
    for (int i = 0; i < 4; i++)
    {
        var f = report.Folds[i];
        True(f.TrainingEnd < f.TestStart && f.TestStart == data.Candles[first].Date);
        True(f.FastPeriod >= 2 && f.FastPeriod < f.SlowPeriod);
        first = data.Candles.ToList().FindIndex(x => x.Date == f.TestEnd) + 1;
        if (i < 3) True(f.TestEnd < report.Folds[i + 1].TestStart);
    }
    True(first == data.Candles.Count);
});
Check("walk-forward first fold unaffected by unseen future prices", () => {
    var data = Enumerable.Range(0, 600).Select(i => 110m + i % 19 + i / 13m).ToArray();
    var changed = data.Select((value, i) => i < 375 ? value : 180m + i % 31).ToArray();
    var validator = new WalkForwardService();
    var one = validator.Validate(Series("AA", data));
    var two = validator.Validate(Series("AA", changed));
    True(one.Folds[0] == two.Folds[0], "A future modification changed an earlier test fold.");
    True(one.Folds[0].TestEnd < two.Folds[1].TestStart);
});
Check("workspace rejects invalid persisted paper fills without rewriting", () => {
    string dir = Path.Combine(Path.GetTempPath(), "bt-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var market = Series("AA", 100, 110);
        var store = new WorkspaceStore(dir);
        store.Save(new([market], new(1000, [], [new PaperFill(DateTimeOffset.UtcNow, "AA", "BUY", 1, 100, 0)])));
        string path = Path.Combine(dir, "workspace.v1.json");
        string good = File.ReadAllText(path);
        string tampered = good.Replace("\"side\": \"BUY\"", "\"side\": \"SHORT\"");
        True(good != tampered, "Expected persisted fill to be present in fixture.");
        File.WriteAllText(path, tampered);
        Throws<InvalidDataException>(() => store.Load());
        True(File.ReadAllText(path) == tampered, "Loading must never overwrite corrupt state.");
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});
Check("paper snapshot is internally consistent after state replacement", () => {
    var broker = new PaperBroker(new(1000, [], []));
    broker.Reset(new(900, [new Position("AA", 1, 100)], []));
    var view = broker.Snapshot([Series("AA", 120)]);
    Nearly(view.Cash, 900);
    Nearly(view.Equity, 1020);
    Nearly(view.Positions[0].MarketValue, 120);
});
Check("replay fills next opening, never the submitting close", () => {
    var series = Series("AA", 100, 105, 110, 120);
    var session = PaperReplayEngine.Start(series, series.Candles[0].Date);
    session = PaperReplayEngine.Submit(session, series, "BUY", 2);
    True(session.Orders[0].Status == "PENDING" && session.Cash == 10_000m);
    session = PaperReplayEngine.Advance(session, series);
    var fill = session.Orders[0];
    True(fill.Status == "FILLED" && fill.ExecutedAt == series.Candles[1].Date);
    True(fill.Price > 105m && fill.Fee > 0 && session.Quantity == 2);
    var once = session.Cash;
    session = PaperReplayEngine.Advance(session, series);
    Nearly(session.Cash, once);
    True(session.Orders.Count == 1 && session.Orders[0].Status == "FILLED");
});
Check("replay rejects orders it cannot fund or cover", () => {
    var data = Series("AA", 100, 110, 120, 130);
    var session = PaperReplayEngine.Start(data, data.Candles[0].Date, 1000);
    session = PaperReplayEngine.Submit(session, data, "SELL", 1);
    session = PaperReplayEngine.Submit(session, data, "BUY", 1000);
    session = PaperReplayEngine.Advance(session, data);
    True(session.Orders.All(x => x.Status == "REJECTED"));
    Nearly(session.Cash, 1000);
    True(session.Quantity == 0);
});
Check("replay selling holdings uses subsequent open and clears cost basis", () => {
    var data = Series("AA", 100, 110, 120, 130);
    var session = PaperReplayEngine.Start(data, data.Candles[0].Date);
    session = PaperReplayEngine.Submit(session, data, "BUY", 2);
    session = PaperReplayEngine.Advance(session, data);
    session = PaperReplayEngine.Submit(session, data, "SELL", 2);
    session = PaperReplayEngine.Advance(session, data);
    True(session.Orders[1].Status == "FILLED" && session.Orders[1].ExecutedAt == data.Candles[2].Date);
    True(session.Quantity == 0 && session.AverageCost == 0m && session.Cash > 10000m);
});
Check("replay rejects history advancing beyond final candle", () => {
    var data = Series("AA", 100, 110, 120);
    var session = PaperReplayEngine.Start(data, data.Candles[0].Date);
    Throws<InvalidOperationException>(() => PaperReplayEngine.Advance(session, data, 3));
    session = PaperReplayEngine.Advance(session, data, 2);
    True(PaperReplayEngine.Snapshot(session, data).AtEnd);
    Throws<InvalidOperationException>(() => PaperReplayEngine.Submit(session, data, "BUY", 1));
});
Check("replay recovery from workspace preserves cursor and pending order", () => {
    var dir = Path.Combine(Path.GetTempPath(), "bt-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var data = Series("AA", 100, 110, 120);
        var session = PaperReplayEngine.Submit(PaperReplayEngine.Start(data, data.Candles[0].Date), data, "BUY", 1);
        var store = new WorkspaceStore(dir);
        store.Save(new([data], new(10000m, [], []), session));
        var recovered = store.Load().Replay ?? throw new Exception("Replay disappeared.");
        True(recovered.CurrentDate == data.Candles[0].Date && recovered.Orders[0].Status == "PENDING");
        recovered = PaperReplayEngine.Advance(recovered, data);
        True(recovered.Quantity == 1 && recovered.Orders[0].Status == "FILLED");
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});
Check("historical replay cannot use a later candle to alter an earlier fill", () => {
    var first = Series("AA", 100, 110, 120, 130);
    var second = Series("AA", 100, 110, 200, 210);
    var a = PaperReplayEngine.Submit(PaperReplayEngine.Start(first, first.Candles[0].Date), first, "BUY", 1);
    var b = PaperReplayEngine.Submit(PaperReplayEngine.Start(second, second.Candles[0].Date), second, "BUY", 1);
    a = PaperReplayEngine.Advance(a, first);
    b = PaperReplayEngine.Advance(b, second);
    True(a.Orders[0] == b.Orders[0] && a.Cash == b.Cash && a.CurrentDate == b.CurrentDate);
});
Check("active replay refuses reimport that rewrites already viewed history", () => {
    string dir = Path.Combine(Path.GetTempPath(), "bt-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var series = Series("AA", 100, 110, 120, 130);
        var replay = PaperReplayEngine.Start(series, series.Candles[0].Date);
        var store = new WorkspaceStore(dir);
        store.Save(new([series], new(10000m, [], []), replay));
        using var workstation = new Workstation(store);
        string path = Path.Combine(dir, "replacement.csv");
        var rows = new List<string> { "Date,Open,High,Low,Close,Volume" };
        foreach (var bar in series.Candles)
        {
            var close = bar.Date == replay.CurrentDate ? bar.Close + 2 : bar.Close;
            rows.Add($"{bar.Date:yyyy-MM-dd},{close},{close + 1},{close - 1},{close},100");
        }
        File.WriteAllLines(path, rows);
        Throws<InvalidOperationException>(() => workstation.ImportCsvAsync(path, "AA").GetAwaiter().GetResult());
        True(workstation.GetSeries("AA").Candles[0].Close == 100m);
        workstation.ClearReplayAsync().GetAwaiter().GetResult();
        var updated = workstation.ImportCsvAsync(path, "AA").GetAwaiter().GetResult();
        True(updated.LastClose == 130m && workstation.GetSeries("AA").Candles[0].Close == 102m);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});
Check("buy-and-hold benchmark charges both entry and exit costs", () => {
    var data = Series("AA", Enumerable.Repeat(100m, 40).ToArray());
    var result = new Backtester().Run(data, new("AA", 3, 8));
    True(result.RoundTrips == 0 && result.BenchmarkReturnPct < 0m);
    Nearly(result.ReturnPct, 0m);
    Nearly(result.ExcessReturnPct, -result.BenchmarkReturnPct);
});
Check("out-of-sample benchmark opens on test-period first bar only", () => {
    var closes = Enumerable.Range(0, 350).Select(i => i < 245 ? 100m : 200m).ToArray();
    var data = Series("AA", closes);
    var result = new Backtester().Run(data, new("AA", 2, 5), data.Candles[245].Date);
    True(result.Equity[0].Date == data.Candles[245].Date);
    True(result.BenchmarkReturnPct < 0m, "The benchmark must not purchase at old in-sample prices.");
});
Check("data quality skips weekends and flags missing weekdays", () => {
    var candles = new[] {
        new Candle(new DateOnly(2026, 1, 2), 100, 100, 100, 100, 1), // Friday
        new Candle(new DateOnly(2026, 1, 5), 100, 100, 100, 100, 0), // Monday, no weekday gap
        new Candle(new DateOnly(2026, 1, 7), 100, 140, 100, 140, 1) // Wednesday, Tuesday missing
    };
    var report = new DataQualityService().Analyze(new MarketSeries("AA", "Test", candles));
    True(report.CandidateMissingWeekdays == 1 && report.CalendarGapCount == 1);
    True(report.ZeroVolumeBars == 1 && report.LargePriceMoveCount == 1);
    True(report.LargestCalendarGaps[0].MissingWeekdays == 1);
    Nearly(report.LargestPriceMoves[0].AbsoluteChangePct, 40m);
});
Check("data quality strictly excludes future bars after replay cutoff", () => {
    var data = new MarketSeries("AA", "Test", new[] {
        new Candle(new DateOnly(2026, 1, 5), 100, 100, 100, 100, 5),
        new Candle(new DateOnly(2026, 1, 6), 100, 100, 100, 100, 5),
        new Candle(new DateOnly(2026, 1, 12), 100, 200, 100, 200, 0)
    });
    var full = new DataQualityService().Analyze(data);
    var historical = new DataQualityService().Analyze(data, new DateOnly(2026, 1, 6));
    True(full.LargePriceMoveCount == 1 && full.CalendarGapCount == 1);
    True(historical.LargePriceMoveCount == 0 && historical.CalendarGapCount == 0);
    True(historical.ZeroVolumeBars == 0 && historical.CandleCount == 2);
    Throws<ArgumentException>(() => new DataQualityService().Analyze(data, new DateOnly(2026, 1, 4)));
});
Check("news CSV retains attribution and quoted commas", () => {
    const string csv = "publishedAt,symbol,headline,source\n" +
        "2026-01-02T11:30:00Z,AA,\"Profit, strong growth\",Test Wire\n" +
        "2026-01-05T19:30:00+01:00,GLOBAL,\"War, risk rises\",Example Source\n";
    var items = NewsResearch.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
    True(items.Count == 2 && items[0].Headline == "Profit, strong growth" && items[0].Source == "Test Wire");
    True(items[1].PublishedAt == new DateTimeOffset(2026, 1, 5, 18, 30, 0, TimeSpan.Zero));
    var sentiment = NewsResearch.ToInsight(items[0]);
    True(sentiment.SentimentScore > 0 && sentiment.SentimentLabel == "POSITIVE");
});
Check("news chronology excludes cutoff date and future publications", () => {
    var items = new[] {
        new NewsEvent(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), "AA", "Profit", "Demo"),
        new NewsEvent(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), "GLOBAL", "War", "Demo"),
        new NewsEvent(new DateTimeOffset(2026, 1, 6, 9, 0, 0, TimeSpan.Zero), "BB", "Recession", "Demo")
    };
    var monday = NewsResearch.Query(items, "AA", new DateOnly(2026, 1, 5));
    True(monday.Count == 1 && monday[0].Headline == "Profit");
    var tuesday = NewsResearch.Query(items, "AA", new DateOnly(2026, 1, 6));
    True(tuesday.Count == 2 && tuesday[0].Symbol == "GLOBAL" && tuesday[0].SentimentScore < 0);
    True(NewsResearch.Query(items, "BB", new DateOnly(2026, 1, 5)).Count == 0);
});
Check("news parser rejects timezone-naive dates and invalid article records", () => {
    var raw = "publishedAt,symbol,headline,source\n2026-01-02T11:00:00,AA,Profit,Demo\n";
    Throws<FormatException>(() => NewsResearch.Parse(new MemoryStream(Encoding.UTF8.GetBytes(raw))));
    raw = "publishedAt,symbol,headline,source\n2026-01-02T11:00:00Z,AA,,Demo\n";
    Throws<FormatException>(() => NewsResearch.Parse(new MemoryStream(Encoding.UTF8.GetBytes(raw))));
});
Check("news research persists and remains separate from paper workspace", () => {
    string dir = Path.Combine(Path.GetTempPath(), "bottrading-news-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var market = Series("AA", 100m, 101m, 102m);
        var store = new WorkspaceStore(dir);
        store.Save(new([market], new(10_000m, [], [])));
        var csv = Path.Combine(dir, "news.csv");
        File.WriteAllText(csv, "publishedAt,symbol,headline,source\n2020-01-01T07:00:00Z,AA,Profit,Demo\n");
        using (var workstation = new Workstation(store))
        {
            True(workstation.ImportNewsCsvAsync(csv).GetAwaiter().GetResult() == 1);
            True(workstation.NewsSnapshot("AA").Count == 1);
            True(workstation.PaperSnapshot().Cash == 10_000m);
        }
        using (var workstation = new Workstation(store))
        {
            True(workstation.NewsSnapshot("AA").Count == 1);
            True(workstation.NewsSnapshot("AA", new DateOnly(2020, 1, 1)).Count == 0);
        }
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
});
Console.WriteLine($"\nTotal: {pass} passed, {fail} failed");
Environment.ExitCode = fail == 0 ? 0 : 1;

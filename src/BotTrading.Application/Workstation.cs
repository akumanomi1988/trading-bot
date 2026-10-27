using BotTrading.Core;

namespace BotTrading.Application;

public sealed class Workstation : IDisposable
{
    private readonly WorkspaceStore _store;
    private readonly MarketDataClient _feed = new();
    private readonly Backtester _backtester = new();
    private readonly PatternFinder _patterns = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _marketSync = new();
    private readonly List<MarketSeries> _markets;
    private readonly PaperBroker _paper;
    private ReplaySession? _replay;
    private IReadOnlyList<NewsEvent> _news = [];
    private IReadOnlyList<SavedExperiment> _experiments = [];

    public Workstation(WorkspaceStore? store = null)
    {
        _store = store ?? new WorkspaceStore();
        var state = _store.Load();
        _markets = state.Series.ToList();
        _paper = new(state.Paper);
        _replay = state.Replay;
        _news = state.News ?? [];
        _experiments = state.Experiments ?? [];
    }

    public async Task ExportBackupAsync(string destinationPath)
    {
        await _writeLock.WaitAsync();
        try
        {
            Workspace state;
            lock (_marketSync) state = new(_markets.ToArray(), _paper.State, _replay, _news, _experiments);
            await Task.Run(() => _store.ExportBackup(state, destinationPath));
        }
        finally { _writeLock.Release(); }
    }

    public async Task RestoreBackupAsync(string sourcePath)
    {
        // Read and validate the complete candidate BEFORE changing the persisted workspace.
        var replacement = await Task.Run(() => _store.ReadBackup(sourcePath));
        await _writeLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                lock (_marketSync)
                {
                    // A deterministic rollback copy is saved before atomic replacement.
                    _store.ExportBackup(new(_markets.ToArray(), _paper.State, _replay, _news, _experiments),
                        _store.RecoveryBackupPath);
                    _store.Save(replacement);
                    _markets.Clear();
                    _markets.AddRange(replacement.Series);
                    _paper.Reset(replacement.Paper);
                    _replay = replacement.Replay;
                    _news = replacement.News ?? [];
                    _experiments = replacement.Experiments ?? [];
                }
            });
        }
        finally { _writeLock.Release(); }
    }

    public IReadOnlyList<ExperimentView> ListExperiments()
    {
        lock (_marketSync)
        {
            var symbols = _experiments.Select(x => x.Symbol).ToHashSet(StringComparer.Ordinal);
            var fingerprints = _markets.Where(x => symbols.Contains(x.Symbol))
                .ToDictionary(x => x.Symbol, ExperimentArchive.Fingerprint, StringComparer.Ordinal);
            return _experiments.OrderByDescending(x => x.CreatedAt)
                .Select(x => new ExperimentView(x, fingerprints.TryGetValue(x.Symbol, out var hash) &&
                    string.Equals(hash, x.DatasetSha256, StringComparison.Ordinal))).ToArray();
        }
    }

    public async Task<ExperimentView> SaveExperimentAsync(string symbol, int fast, int slow)
    {
        await _writeLock.WaitAsync();
        try
        {
            MarketSeries market;
            lock (_marketSync) market = GetSeries(symbol);
            // Perform the backtest before mutating persistence.
            var experiment = await Task.Run(() =>
                ExperimentArchive.Capture(market,
                    _backtester.Run(market, new BacktestRequest(market.Symbol, fast, slow))));
            lock (_marketSync)
            {
                if (_experiments.Count >= ExperimentArchive.MaxExperiments)
                    throw new InvalidOperationException("The experiment archive is full (100 records).");
                var previous = _experiments;
                _experiments = _experiments.Append(experiment).ToArray();
                try { Save(); }
                catch { _experiments = previous; throw; }
            }
            return new ExperimentView(experiment, true);
        }
        finally { _writeLock.Release(); }
    }

    public async Task DeleteExperimentAsync(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid experiment identifier.", nameof(id));
        await _writeLock.WaitAsync();
        try
        {
            lock (_marketSync)
            {
                if (!_experiments.Any(x => x.Id == id)) throw new KeyNotFoundException("Experiment not found.");
                var previous = _experiments;
                _experiments = _experiments.Where(x => x.Id != id).ToArray();
                try { Save(); }
                catch { _experiments = previous; throw; }
            }
        }
        finally { _writeLock.Release(); }
    }

    public IReadOnlyList<SeriesSummary> ListSeries()
    {
        lock (_marketSync) return _markets.Select(x => x.ToSummary()).OrderBy(x => x.Symbol).ToArray();
    }

    public MarketSeries GetSeries(string symbol)
    {
        lock (_marketSync) return _markets.FirstOrDefault(x => x.Symbol == symbol.Trim().ToUpperInvariant())
            ?? throw new KeyNotFoundException($"Instrument '{symbol}' is not loaded.");
    }

    public PatternSearchResult FindPatterns(string symbol, int window, int maxResults,
        DateOnly? endDate = null, string? targetSymbol = null)
    {
        var series = GetSeries(symbol);
        int end = endDate.HasValue ? FindLastAtOrBefore(series.Candles, endDate.Value) : series.Candles.Count - 1;
        MarketSeries[] snapshot;
        lock (_marketSync) snapshot = _markets.ToArray();
        if (!string.IsNullOrWhiteSpace(targetSymbol))
            snapshot = [GetSeries(targetSymbol)];
        return _patterns.Find(series, snapshot, window, maxResults, end);
    }

    public BacktestResult Backtest(string symbol, int fast, int slow, decimal capital = 10_000m)
    {
        var series = GetSeries(symbol);
        return _backtester.Run(series, new(symbol.ToUpperInvariant(), fast, slow, capital));
    }

    public CostSensitivityReport AnalyzeCostSensitivity(string symbol, int fast, int slow) =>
        new CostSensitivityService().Analyze(GetSeries(symbol), fast, slow);

    public IReadOnlyList<BacktestResult> Optimize(string symbol) => _backtester.Optimize(GetSeries(symbol));
    public DcaReport SimulateDca(string symbol, decimal monthlyContribution, DateOnly? start = null, DateOnly? end = null) =>
        new DcaSimulator().Run(GetSeries(symbol), monthlyContribution, start, end);
    public PaperView PaperSnapshot()
    {
        lock (_marketSync) return _paper.Snapshot(_markets);
    }
    public DataQualityReport AnalyzeQuality(string symbol, DateOnly? cutoff = null) =>
        new DataQualityService().Analyze(GetSeries(symbol), cutoff);
    public ValidationReport ValidateOutOfSample(string symbol) => new RobustnessService().Validate(GetSeries(symbol));
    public WalkForwardReport ValidateWalkForward(string symbol) => new WalkForwardService().Validate(GetSeries(symbol));

    public ReplaySnapshot? ReplaySnapshot()
    {
        lock (_marketSync) return _replay is null ? null : PaperReplayEngine.Snapshot(_replay, GetSeries(_replay.Symbol));
    }

    public IReadOnlyList<NewsInsight> NewsSnapshot(string symbol, DateOnly? cutoff = null)
    {
        lock (_marketSync) return NewsResearch.Query(_news, symbol, cutoff);
    }

    public async Task<int> ImportNewsCsvAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > NewsResearch.MaxFileBytes)
            throw new InvalidDataException("News CSV missing or larger than 1 MiB.");
        await using var stream = File.OpenRead(path);
        var imported = NewsResearch.Parse(stream);
        await _writeLock.WaitAsync();
        try
        {
            lock (_marketSync)
            {
                var next = _news.Concat(imported)
                    .GroupBy(x => (x.PublishedAt, x.Symbol, x.Headline, x.Source))
                    .Select(x => x.First()).OrderByDescending(x => x.PublishedAt).Take(NewsResearch.MaxItems).ToArray();
                _store.Save(new(_markets.ToArray(), _paper.State, _replay, next, _experiments));
                _news = next;
                return imported.Count;
            }
        }
        finally { _writeLock.Release(); }
    }

    public Task<ReplaySnapshot> StartReplayAsync(string symbol, DateOnly startDate) =>
        UpdateReplayAsync(_ => PaperReplayEngine.Start(GetSeries(symbol), startDate));

    public Task<ReplaySnapshot> SubmitReplayOrderAsync(string side, int quantity,
        string orderType = "MARKET", decimal? triggerPrice = null, DateOnly? goodThrough = null) =>
        UpdateReplayAsync(previous => previous is null
            ? throw new InvalidOperationException("Start a market replay before submitting orders.")
            : PaperReplayEngine.Submit(previous, GetSeries(previous.Symbol), side, quantity, orderType, triggerPrice, goodThrough));

    public Task<ReplaySnapshot> CancelReplayOrderAsync(long sequence) =>
        UpdateReplayAsync(previous => previous is null
            ? throw new InvalidOperationException("Start a replay before cancelling an order.")
            : PaperReplayEngine.Cancel(previous, GetSeries(previous.Symbol), sequence));

    public Task<ReplaySnapshot> AdvanceReplayAsync(int steps) =>
        UpdateReplayAsync(previous => previous is null
            ? throw new InvalidOperationException("Start a market replay before advancing time.")
            : PaperReplayEngine.Advance(previous, GetSeries(previous.Symbol), steps));

    public async Task ClearReplayAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            lock (_marketSync)
            {
                var previous = _replay;
                _replay = null;
                try { Save(); }
                catch { _replay = previous; throw; }
            }
        }
        finally { _writeLock.Release(); }
    }

    private async Task<ReplaySnapshot> UpdateReplayAsync(Func<ReplaySession?, ReplaySession> change)
    {
        await _writeLock.WaitAsync();
        try
        {
            lock (_marketSync)
            {
                var previous = _replay;
                var updated = change(previous);
                _replay = updated;
                try { Save(); }
                catch { _replay = previous; throw; }
                return PaperReplayEngine.Snapshot(updated, GetSeries(updated.Symbol));
            }
        }
        finally { _writeLock.Release(); }
    }

    public async Task<SeriesSummary> ImportCsvAsync(string path, string symbol, string? name = null)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > CsvMarketData.MaxFileBytes) throw new InvalidDataException("CSV missing or larger than 16 MiB.");
        await using var stream = File.OpenRead(path);
        var imported = CsvMarketData.Parse(stream, symbol, name);
        return await AddOrReplaceAsync(imported);
    }

    public async Task<SeriesSummary> DownloadAsync(string ticker, CancellationToken ct = default)
    {
        var imported = await _feed.DownloadStooqAsync(ticker, ct);
        return await AddOrReplaceAsync(imported);
    }

    public async Task<PaperFill> PaperOrderAsync(string symbol, string side, int quantity)
    {
        await _writeLock.WaitAsync();
        try
        {
            var previous = _paper.State;
            try
            {
                var fill = _paper.Place(GetSeries(symbol), side, quantity);
                Save();
                return fill;
            }
            catch { _paper.Reset(previous); throw; }
        }
        finally { _writeLock.Release(); }
    }

    public async Task<PaperView> ResetPaperAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            var previous = _paper.State;
            try
            {
                _paper.Reset(new(10_000, [], []));
                Save();
                return PaperSnapshot();
            }
            catch { _paper.Reset(previous); throw; }
        }
        finally { _writeLock.Release(); }
    }

    private async Task<SeriesSummary> AddOrReplaceAsync(MarketSeries market)
    {
        await _writeLock.WaitAsync();
        try
        {
            lock (_marketSync)
            {
                var next = _markets.ToList();
                int existing = next.FindIndex(x => x.Symbol == market.Symbol);
                if (existing >= 0) next[existing] = market;
                else
                {
                    if (next.Count >= 25) throw new InvalidOperationException("Maximum 25 instruments in local workspace.");
                    next.Add(market);
                }
                if (_replay is not null && _replay.Symbol == market.Symbol)
                {
                    var previous = _markets.First(x => x.Symbol == market.Symbol);
                    var oldHistory = previous.Candles.Where(x => x.Date <= _replay.CurrentDate).ToArray();
                    var newHistory = market.Candles.Where(x => x.Date <= _replay.CurrentDate).ToArray();
                    if (!oldHistory.SequenceEqual(newHistory))
                        throw new InvalidOperationException("Imported data would rewrite the active replay history. Discard the replay first.");
                    PaperReplayEngine.ValidatePersisted(_replay, market);
                }
                _store.Save(new(next, _paper.State, _replay, _news, _experiments));
                _markets.Clear();
                _markets.AddRange(next);
                return market.ToSummary();
            }
        }
        finally { _writeLock.Release(); }
    }

    private static int FindLastAtOrBefore(IReadOnlyList<Candle> candles, DateOnly date)
    {
        int lo = 0, hi = candles.Count - 1, result = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (candles[mid].Date <= date) { result = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return result;
    }

    private void Save()
    {
        lock (_marketSync) _store.Save(new(_markets.ToArray(), _paper.State, _replay, _news, _experiments));
    }
    public void Dispose() { _feed.Dispose(); _writeLock.Dispose(); }
}

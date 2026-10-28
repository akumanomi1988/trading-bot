using System.Text.Json;
using BotTrading.Core;

namespace BotTrading.Application;

public sealed record Position(string Symbol, int Quantity, decimal AveragePrice);
public sealed record PaperFill(DateTimeOffset Timestamp, string Symbol, string Side, int Quantity, decimal Price, decimal Fee);
public sealed record PaperAccount(decimal Cash, IReadOnlyList<Position> Positions, IReadOnlyList<PaperFill> Fills);
public sealed record PaperView(decimal Cash, decimal Equity, decimal UnrealizedPnL, IReadOnlyList<PositionView> Positions, IReadOnlyList<PaperFill> Fills);
public sealed record PositionView(string Symbol, int Quantity, decimal AveragePrice, decimal MarkPrice, decimal MarketValue, decimal UnrealizedPnL);

public sealed class WorkspaceStore
{
    private const long MaxWorkspaceBytes = 512L * 1024 * 1024;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public WorkspaceStore(string? directory = null)
    {
        string root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotTrading");
        _path = Path.Combine(root, "workspace.v1.json");
    }

    public Workspace Load() => File.Exists(_path) ? ReadValidated(_path) : new([], new(10_000, [], []));

    public string RecoveryBackupPath => Path.Combine(Path.GetDirectoryName(_path)!, "workspace.pre-restore.v1.json");

    // Never interpret a missing restore source as a fresh empty workspace.
    public Workspace ReadBackup(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("The selected backup file is missing.", sourcePath);
        try { return ReadValidated(sourcePath); }
        catch (JsonException ex) { throw new InvalidDataException("The backup is not valid workspace JSON.", ex); }
    }

    private Workspace ReadValidated(string path)
    {
        if (new FileInfo(path).Length > MaxWorkspaceBytes)
            throw new InvalidDataException("The workspace exceeds the 512 MiB import safety limit.");
        var saved = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path), _json)
            ?? throw new InvalidDataException("Workspace JSON is empty.");
        if (saved.SchemaVersion is not (0 or 1))
            throw new InvalidDataException("Unsupported workspace schema version. No data was overwritten.");
        if (saved.Series is null || saved.Paper is null || saved.Paper.Positions is null || saved.Paper.Fills is null)
            throw new InvalidDataException("Workspace JSON is incomplete. No data was overwritten.");
        if (saved.Series.Count > 25) throw new InvalidDataException("Workspace contains too many instruments.");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<MarketSeries>();
        foreach (var item in saved.Series)
        {
            var market = new MarketSeries(item.Symbol, item.Name, item.Candles, item.Provenance);
            if (!unique.Add(market.Symbol)) throw new InvalidDataException("Duplicate series in workspace.");
            validated.Add(market);
        }
        if (saved.Replay is not null)
        {
            var replaySeries = validated.FirstOrDefault(x => x.Symbol == saved.Replay.Symbol)
                ?? throw new InvalidDataException("Replay market is missing from the workspace.");
            PaperReplayEngine.ValidatePersisted(saved.Replay, replaySeries);
        }
        if (saved.Paper.Cash < 0 || saved.Paper.Positions.GroupBy(x => x.Symbol, StringComparer.Ordinal).Any(x => x.Count() > 1) ||
            saved.Paper.Positions.Any(x => x.Symbol is null || x.Quantity <= 0 || x.AveragePrice <= 0 || !unique.Contains(x.Symbol)) ||
            saved.Paper.Fills.Count > 5000 || saved.Paper.Fills.Any(x => x.Symbol is null || !unique.Contains(x.Symbol) ||
                x.Side is not ("BUY" or "SELL") || x.Quantity <= 0 || x.Quantity > 1_000_000 || x.Price <= 0 || x.Fee < 0))
            throw new InvalidDataException("Invalid paper account state. No data was overwritten.");
        var savedNews = saved.News ?? [];
        if (savedNews.Count > NewsResearch.MaxItems || savedNews.Any(x => !NewsResearch.IsValid(x)))
            throw new InvalidDataException("Invalid news research state. No data was overwritten.");
        var existingExperiments = saved.Experiments ?? [];
        if (existingExperiments.Any(x => x is null))
            throw new InvalidDataException("Null experiment in workspace.");
        // Old version-1 experiment JSON did not contain strategyKind and was always SMA.
        // Normalize the in-memory representation without rewriting the user's original file.
        var experiments = existingExperiments.Select(x =>
            x.StrategyKind is null ? x with { StrategyKind = "SMA" } : x).ToArray();
        if (experiments.Length > ExperimentArchive.MaxExperiments ||
            experiments.Any(x => !ExperimentArchive.IsValid(x)) ||
            experiments.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != experiments.Length)
            throw new InvalidDataException("Invalid saved strategy experiments. No data was overwritten.");
        return new(validated, saved.Paper, saved.Replay, savedNews, experiments, 1);
    }

    public void Save(Workspace workspace)
    {
        if (workspace.SchemaVersion != 1)
            throw new ArgumentException("Unsupported workspace schema version.", nameof(workspace));
        WriteAtomic(workspace, _path);
    }

    public void ExportBackup(Workspace workspace, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Backup destination is required.", nameof(destinationPath));
        var target = Path.GetFullPath(destinationPath);
        if (string.Equals(target, Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot export over the live workspace.");
        WriteAtomic(workspace, target);
    }

    private void WriteAtomic(Workspace workspace, string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $"workspace.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(workspace, _json));
            // File.Replace atomically replaces the destination on Windows when it exists.
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record Workspace(IReadOnlyList<MarketSeries> Series, PaperAccount Paper, ReplaySession? Replay = null,
    IReadOnlyList<NewsEvent>? News = null, IReadOnlyList<SavedExperiment>? Experiments = null,
    int SchemaVersion = 1);

public sealed class PaperBroker
{
    private PaperAccount _state;
    public PaperBroker(PaperAccount initial) => _state = initial;
    public PaperAccount State => _state;
    public void Reset(PaperAccount replacement) => _state = replacement;

    public PaperFill Place(MarketSeries market, string side, int quantity, decimal feeBps = 5m, decimal slippageBps = 5m)
    {
        if (quantity is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (feeBps < 0 || feeBps > 1000 || slippageBps < 0 || slippageBps > 1000)
            throw new ArgumentOutOfRangeException(nameof(feeBps));
        side = side.Trim().ToUpperInvariant();
        if (side != "BUY" && side != "SELL") throw new ArgumentException("Only BUY or SELL is supported.");
        var price = market.Candles[^1].Close * (side == "BUY" ? 1 + slippageBps / 10000 : 1 - slippageBps / 10000);
        decimal fee = quantity * price * feeBps / 10000;
        var positions = _state.Positions.ToDictionary(x => x.Symbol, StringComparer.Ordinal);
        positions.TryGetValue(market.Symbol, out var old);
        int oldQty = old?.Quantity ?? 0;
        decimal cash = _state.Cash;
        if (side == "BUY")
        {
            if (positions.Keys.Any(key => key != market.Symbol))
                throw new InvalidOperationException("Multi-currency portfolio valuation is not supported: close the existing instrument first.");
            decimal cost = quantity * price + fee;
            if (cost > cash) throw new InvalidOperationException("Insufficient simulated cash.");
            cash -= cost;
            positions[market.Symbol] = new(market.Symbol, oldQty + quantity,
                ((old?.AveragePrice ?? 0) * oldQty + quantity * price + fee) / (oldQty + quantity));
        }
        else
        {
            if (oldQty < quantity) throw new InvalidOperationException("Insufficient simulated holdings; short-selling is disabled.");
            cash += quantity * price - fee;
            if (oldQty == quantity) positions.Remove(market.Symbol);
            else positions[market.Symbol] = old! with { Quantity = oldQty - quantity };
        }
        var fill = new PaperFill(DateTimeOffset.UtcNow, market.Symbol, side, quantity, price, fee);
        _state = new(cash, positions.Values.OrderBy(x => x.Symbol).ToArray(), _state.Fills.Append(fill).TakeLast(5000).ToArray());
        return fill;
    }

    public PaperView Snapshot(IReadOnlyCollection<MarketSeries> markets)
    {
        // The owner may place an order concurrently with a read. Capture one immutable
        // account snapshot so cash, positions, and fills refer to the same state version.
        var account = _state;
        var marks = markets.ToDictionary(x => x.Symbol, x => x.Candles[^1].Close, StringComparer.Ordinal);
        var positions = account.Positions.Select(p =>
        {
            decimal mark = marks.GetValueOrDefault(p.Symbol, p.AveragePrice);
            return new PositionView(p.Symbol, p.Quantity, p.AveragePrice, mark,
                mark * p.Quantity, (mark - p.AveragePrice) * p.Quantity);
        }).ToArray();
        return new(account.Cash, account.Cash + positions.Sum(x => x.MarketValue), positions.Sum(x => x.UnrealizedPnL), positions, account.Fills);
    }
}

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
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public WorkspaceStore(string? directory = null)
    {
        string root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BotTrading");
        _path = Path.Combine(root, "workspace.v1.json");
    }

    public Workspace Load()
    {
        if (!File.Exists(_path)) return new([], new(10_000, [], []));
        var saved = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(_path), _json)
            ?? throw new InvalidDataException("Workspace JSON is empty.");
        if (saved.Series is null || saved.Paper is null || saved.Paper.Positions is null || saved.Paper.Fills is null)
            throw new InvalidDataException("Workspace JSON is incomplete. No data was overwritten.");
        if (saved.Series.Count > 25) throw new InvalidDataException("Workspace contains too many instruments.");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<MarketSeries>();
        foreach (var item in saved.Series)
        {
            var market = new MarketSeries(item.Symbol, item.Name, item.Candles);
            if (!unique.Add(market.Symbol)) throw new InvalidDataException("Duplicate series in workspace.");
            validated.Add(market);
        }
        if (saved.Paper.Cash < 0 || saved.Paper.Positions.GroupBy(x => x.Symbol, StringComparer.Ordinal).Any(x => x.Count() > 1) ||
            saved.Paper.Positions.Any(x => x.Symbol is null || x.Quantity <= 0 || x.AveragePrice <= 0 || !unique.Contains(x.Symbol)) ||
            saved.Paper.Fills.Count > 5000 || saved.Paper.Fills.Any(x => x.Symbol is null || !unique.Contains(x.Symbol) ||
                x.Side is not ("BUY" or "SELL") || x.Quantity <= 0 || x.Quantity > 1_000_000 || x.Price <= 0 || x.Fee < 0))
            throw new InvalidDataException("Invalid paper account state. No data was overwritten.");
        return new(validated, saved.Paper);
    }

    public void Save(Workspace workspace)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, $"workspace.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(workspace, _json));
            // File.Replace atomically replaces the destination on Windows when it exists.
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record Workspace(IReadOnlyList<MarketSeries> Series, PaperAccount Paper);

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

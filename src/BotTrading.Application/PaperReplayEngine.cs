using BotTrading.Core;

namespace BotTrading.Application;

public sealed record ReplayOrder(long Sequence, DateOnly RequestedAt, string Side, int Quantity,
    string Status, DateOnly? ExecutedAt = null, decimal? Price = null, decimal Fee = 0m, string? Reason = null,
    string? OrderType = null, decimal? TriggerPrice = null, DateOnly? GoodThrough = null);

// Independent from the quick paper account. A historical replay cannot silently mark its
// account to the latest imported close without leaking information from the future.
public sealed record ReplaySession(string Symbol, DateOnly CurrentDate, decimal InitialCapital,
    decimal Cash, int Quantity, decimal AverageCost, IReadOnlyList<ReplayOrder> Orders);

public sealed record ReplaySnapshot(string Symbol, DateOnly CurrentDate, DateOnly FinalDate,
    decimal Cash, int Quantity, decimal AverageCost, decimal MarkPrice, decimal Equity,
    decimal UnrealizedPnL, bool AtEnd, int RemainingSessions, IReadOnlyList<ReplayOrder> Orders);

public static class PaperReplayEngine
{
    private const decimal FeeBps = 5m;
    private const decimal SlippageBps = 5m;

    public static ReplaySession Start(MarketSeries series, DateOnly requestedDate, decimal capital = 10_000m)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (capital < 100m || capital > 1_000_000_000m)
            throw new ArgumentOutOfRangeException(nameof(capital));
        if (series.Candles.Count < 2)
            throw new ArgumentException("A historical replay requires at least two candles.");
        var index = FindStartingIndex(series.Candles, requestedDate);
        if (index >= series.Candles.Count - 1)
            throw new ArgumentException("Replay start must precede the final historical candle.");
        return new(series.Symbol, series.Candles[index].Date, capital, capital, 0, 0m, []);
    }

    public static ReplaySession Submit(ReplaySession state, MarketSeries series, string side, int quantity,
        string orderType = "MARKET", decimal? triggerPrice = null, DateOnly? goodThrough = null)
    {
        int index = ValidateAndFind(state, series);
        if (index >= series.Candles.Count - 1) throw new InvalidOperationException("Replay has reached the final candle.");
        if (quantity is < 1 or > 1_000_000) throw new ArgumentOutOfRangeException(nameof(quantity));
        side = side?.Trim().ToUpperInvariant() ?? throw new ArgumentNullException(nameof(side));
        if (side is not ("BUY" or "SELL")) throw new ArgumentException("Only BUY or SELL orders are supported.");
        orderType = orderType?.Trim().ToUpperInvariant() ?? throw new ArgumentNullException(nameof(orderType));
        if (orderType is not ("MARKET" or "LIMIT" or "STOP"))
            throw new ArgumentException("Only MARKET, LIMIT and STOP order types are supported.", nameof(orderType));
        if (orderType == "MARKET" ? triggerPrice is not null : triggerPrice is null or <= 0)
            throw new ArgumentException("Conditional orders require a positive trigger price; market orders must not have one.", nameof(triggerPrice));
        if (goodThrough.HasValue && goodThrough.Value <= state.CurrentDate)
            throw new ArgumentException("Good-through date must be later than the submission date.", nameof(goodThrough));
        if (state.Orders.Count >= 5000) throw new InvalidOperationException("Replay order history is full; start a new replay.");
        var sequence = state.Orders.Count == 0 ? 1 : state.Orders[^1].Sequence + 1;
        var order = new ReplayOrder(sequence, state.CurrentDate, side, quantity, "PENDING", OrderType: orderType, TriggerPrice: triggerPrice, GoodThrough: goodThrough);
        return state with { Orders = state.Orders.Append(order).ToArray() };
    }

    public static ReplaySession Cancel(ReplaySession state, MarketSeries series, long sequence)
    {
        _ = ValidateAndFind(state, series);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        var order = state.Orders.FirstOrDefault(x => x.Sequence == sequence)
            ?? throw new KeyNotFoundException("Replay order not found.");
        if (order.Status != "PENDING")
            throw new InvalidOperationException("Only pending orders can be cancelled.");
        return state with { Orders = state.Orders.Select(x => x.Sequence == sequence
            ? x with { Status = "CANCELLED", ExecutedAt = state.CurrentDate, Reason = "USER_CANCELLED" }
            : x).ToArray() };
    }

    public static ReplaySession Advance(ReplaySession state, MarketSeries series, int steps = 1)
    {
        int position = ValidateAndFind(state, series);
        if (steps is < 1 or > 250) throw new ArgumentOutOfRangeException(nameof(steps));
        if (position + steps >= series.Candles.Count)
            throw new InvalidOperationException("Cannot advance beyond the final available historical candle.");
        var current = state;
        for (int k = 1; k <= steps; k++)
        {
            var bar = series.Candles[position + k];
            decimal cash = current.Cash;
            int shares = current.Quantity;
            decimal avgCost = current.AverageCost;
            var changed = new List<ReplayOrder>(current.Orders.Count);
            foreach (var order in current.Orders)
            {
                if (order.Status != "PENDING" || order.RequestedAt >= bar.Date)
                {
                    changed.Add(order);
                    continue;
                }
                // The good-through date is inclusive. Expiry is recorded when the first
                // subsequent observed daily candle occurs; missing sessions are never invented.
                if (order.GoodThrough.HasValue && bar.Date > order.GoodThrough.Value)
                {
                    changed.Add(order with { Status = "EXPIRED", ExecutedAt = bar.Date, Reason = "GOOD_THROUGH_ELAPSED" });
                    continue;
                }
                var buy = order.Side == "BUY";
                string type = order.OrderType ?? "MARKET";
                // Orders are submitted after the previous completed daily candle.
                // LIMIT remains queued until touched. STOP becomes a market execution when triggered.
                decimal open = bar.Open;
                decimal rawPrice;
                if (type == "LIMIT")
                {
                    decimal limit = order.TriggerPrice!.Value;
                    if (buy && bar.Low > limit || !buy && bar.High < limit)
                    {
                        changed.Add(order);
                        continue;
                    }
                    rawPrice = buy ? Math.Min(open, limit) : Math.Max(open, limit);
                }
                else if (type == "STOP")
                {
                    decimal stop = order.TriggerPrice!.Value;
                    if (buy && bar.High < stop || !buy && bar.Low > stop)
                    {
                        changed.Add(order);
                        continue;
                    }
                    rawPrice = buy ? Math.Max(open, stop) : Math.Min(open, stop);
                }
                else rawPrice = open;
                decimal price = rawPrice * (buy ? 1m + SlippageBps / 10000m : 1m - SlippageBps / 10000m);
                if (type == "LIMIT")
                    price = buy ? Math.Min(price, order.TriggerPrice!.Value) : Math.Max(price, order.TriggerPrice!.Value);
                var fee = price * order.Quantity * FeeBps / 10000m;
                decimal gross = price * order.Quantity;
                string? reject = buy && gross + fee > cash ? "INSUFFICIENT_CASH"
                    : !buy && order.Quantity > shares ? "INSUFFICIENT_SHARES" : null;
                if (reject is not null)
                {
                    changed.Add(order with { Status = "REJECTED", ExecutedAt = bar.Date, Reason = reject });
                    continue;
                }
                if (buy)
                {
                    avgCost = (avgCost * shares + gross + fee) / (shares + order.Quantity);
                    shares += order.Quantity;
                    cash -= gross + fee;
                }
                else
                {
                    shares -= order.Quantity;
                    cash += gross - fee;
                    if (shares == 0) avgCost = 0m;
                }
                changed.Add(order with { Status = "FILLED", ExecutedAt = bar.Date, Price = price, Fee = fee });
            }
            current = current with { CurrentDate = bar.Date, Cash = cash, Quantity = shares,
                AverageCost = avgCost, Orders = changed };
        }
        return current;
    }

    public static ReplaySnapshot Snapshot(ReplaySession state, MarketSeries series)
    {
        int index = ValidateAndFind(state, series);
        decimal mark = series.Candles[index].Close;
        decimal marketValue = state.Quantity * mark;
        return new(state.Symbol, state.CurrentDate, series.Candles[^1].Date, state.Cash,
            state.Quantity, state.AverageCost, mark, state.Cash + marketValue,
            (mark - state.AverageCost) * state.Quantity, index == series.Candles.Count - 1,
            series.Candles.Count - index - 1, state.Orders);
    }

    public static void ValidatePersisted(ReplaySession state, MarketSeries series)
    {
        _ = ValidateAndFind(state, series);
        if (state.Orders is null) throw new InvalidDataException("Replay order history is missing.");
        if (state.InitialCapital is < 100m or > 1_000_000_000m || state.Cash < 0 ||
            state.Quantity < 0 || state.AverageCost < 0 || (state.Quantity == 0 && state.AverageCost != 0) ||
            (state.Quantity > 0 && state.AverageCost == 0) ||
            state.Orders.Count > 5000 || state.Orders.Any(x => x.Sequence < 1 ||
                x.RequestedAt > state.CurrentDate || x.Side is not ("BUY" or "SELL") ||
                x.OrderType is not (null or "MARKET" or "LIMIT" or "STOP") ||
                ((x.OrderType is null or "MARKET") != (x.TriggerPrice is null)) ||
                x.TriggerPrice is <= 0 ||
                x.Status is not ("PENDING" or "FILLED" or "REJECTED" or "CANCELLED" or "EXPIRED") ||
                (x.GoodThrough.HasValue && x.GoodThrough.Value <= x.RequestedAt) ||
                (x.Status == "PENDING" && x.GoodThrough.HasValue && state.CurrentDate > x.GoodThrough.Value) ||
                (x.Status == "EXPIRED" && (!x.GoodThrough.HasValue || x.ExecutedAt <= x.GoodThrough.Value)) || x.Quantity is < 1 or > 1_000_000 ||
                x.Price < 0 || x.Fee < 0 || (x.Status == "PENDING" && x.ExecutedAt is not null) ||
                (x.Status != "PENDING" && (x.ExecutedAt is null || x.ExecutedAt > state.CurrentDate ||
                    x.ExecutedAt < x.RequestedAt)) || (x.Status != "FILLED" && x.Fee != 0m) ||
                (x.Status == "FILLED" && (x.Price is null || x.Price <= 0)) ||
                (x.Status != "FILLED" && x.Price is not null)))
            throw new InvalidDataException("Invalid persisted replay session.");
        for (int i = 0; i < state.Orders.Count; i++)
            if (state.Orders[i].Sequence != i + 1) throw new InvalidDataException("Invalid replay order sequence.");
    }

    private static int ValidateAndFind(ReplaySession state, MarketSeries series)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(series);
        if (state.Symbol != series.Symbol) throw new ArgumentException("Replay symbol does not match history.");
        var bars = series.Candles;
        int low = 0, high = bars.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (bars[mid].Date == state.CurrentDate) return mid;
            if (bars[mid].Date < state.CurrentDate) low = mid + 1; else high = mid - 1;
        }
        throw new InvalidDataException("The replay cursor is absent from the selected market history.");
    }

    private static int FindStartingIndex(IReadOnlyList<Candle> bars, DateOnly date)
    {
        int left = 0, right = bars.Count;
        while (left < right)
        {
            int mid = left + (right - left) / 2;
            if (bars[mid].Date < date) left = mid + 1; else right = mid;
        }
        return left;
    }
}

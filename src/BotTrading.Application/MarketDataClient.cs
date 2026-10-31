using BotTrading.Core;
using System.Net;

namespace BotTrading.Application;

public sealed class MarketDataClient : IDisposable
{
    private readonly HttpClient _http;
    // A custom handler supports offline deterministic HTTP acceptance tests; production
    // uses a fixed HTTPS endpoint and disables redirect following.
    public MarketDataClient(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new HttpClientHandler {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
        }, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(18);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BotTrading/0.1 historical-data-research");
    }

    // Free Stooq historical daily export; provider has no availability/SLA guarantee.
    public async Task<MarketSeries> DownloadStooqAsync(string symbol, CancellationToken cancellationToken = default,
        string? apiKey = null)
    {
        // Never accept a URL or host from WebView2; only a strictly validated ticker.
        _ = new MarketSeries(symbol, symbol, [new(new DateOnly(2000, 1, 1), 1, 1, 1, 1, 0)]);
        symbol = symbol.Trim().ToLowerInvariant();
        if (apiKey is not null && (apiKey.Length is < 8 or > 128 ||
            !System.Text.RegularExpressions.Regex.IsMatch(apiKey, "^[A-Za-z0-9_-]+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
            throw new ArgumentException("Stooq API key must be 8-128 ASCII alphanumeric characters (hyphen and underscore allowed).", nameof(apiKey));
        // The key is never logged, serialized to the workspace, or sent to any other origin.
        string endpoint = $"https://stooq.com/q/d/l/?s={Uri.EscapeDataString(symbol)}&i=d";
        if (apiKey is not null) endpoint += "&apikey=" + Uri.EscapeDataString(apiKey);
        using var response = await _http.GetAsync(endpoint,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Stooq rate-limited the historical download (429). Wait before retrying or import a local CSV.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new HttpRequestException("Stooq denied access to its historical CSV. Check the provider's current terms and API-key requirements, or import a locally downloaded CSV.");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"Historical provider returned HTTP {(int)response.StatusCode}. The local workspace was not changed.");
        if (response.Content.Headers.ContentType?.MediaType?.Equals("text/html", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidDataException("Stooq returned an HTML challenge instead of OHLC CSV. Provider access may require a key or a manual CSV export.");
        if (response.Content.Headers.ContentLength is > CsvMarketData.MaxFileBytes)
            throw new InvalidDataException("Provider dataset exceeds 16 MiB.");
        await using var data = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new LimitedReadStream(data, CsvMarketData.MaxFileBytes);
        try
        {
            return CsvMarketData.Parse(limited, symbol, symbol, new PriceProvenance("STOOQ", "UNKNOWN"));
        }
        catch (FormatException ex)
        {
            throw new FormatException("Stooq did not return valid OHLC CSV. It may require an API key, have reached its quota, or be blocking automated access. Export CSV manually and import it if permitted.", ex);
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner; private readonly long _max; private long _total;
        public LimitedReadStream(Stream inner, long max) { _inner = inner; _max = max; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count); _total += n;
            if (_total > _max) throw new InvalidDataException("Downloaded dataset exceeds size limit.");
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            int n = _inner.Read(buffer); _total += n;
            if (_total > _max) throw new InvalidDataException("Downloaded dataset exceeds size limit.");
            return n;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await _inner.DisposeAsync(); await base.DisposeAsync(); }
    }
}

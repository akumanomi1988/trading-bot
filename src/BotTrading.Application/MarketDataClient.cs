using BotTrading.Core;
using System.Net;

namespace BotTrading.Application;

public sealed class MarketDataClient : IDisposable
{
    private readonly HttpClient _http;
    public MarketDataClient()
    {
        _http = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip });
        _http.Timeout = TimeSpan.FromSeconds(18);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("BotTrading/0.1 historical-data-research");
    }

    // Free Stooq historical daily export; provider has no availability/SLA guarantee.
    public async Task<MarketSeries> DownloadStooqAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // Never accept a URL or host from WebView2; only a strictly validated ticker.
        _ = new MarketSeries(symbol, symbol, [new(new DateOnly(2000, 1, 1), 1, 1, 1, 1, 0)]);
        symbol = symbol.Trim().ToLowerInvariant();
        using var response = await _http.GetAsync($"https://stooq.com/q/d/l/?s={Uri.EscapeDataString(symbol)}&i=d",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"Provider returned {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentLength is > CsvMarketData.MaxFileBytes)
            throw new InvalidDataException("Provider dataset exceeds 16 MiB.");
        await using var data = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new LimitedReadStream(data, CsvMarketData.MaxFileBytes);
        return CsvMarketData.Parse(limited, symbol, symbol, new PriceProvenance("STOOQ", "UNKNOWN"));
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

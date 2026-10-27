export type Candle = { date: string; open: number; high: number; low: number; close: number; volume: number };
export type Asset = { symbol: string; name: string; count: number; start: string; end: string; lastClose: number };
export type Series = { symbol: string; name: string; candles: Candle[] };
export type Pattern = { symbol: string; start: string; end: string; correlation: number; distance: number };
export type PatternSearch = { querySymbol: string; queryStart: string; queryEnd: string; window: number; matches: Pattern[] };
export type DataQualityReport = { symbol: string; first: string; last: string; candleCount: number;
  candidateMissingWeekdays: number; calendarGapCount: number; zeroVolumeBars: number;
  largePriceMoveCount: number; largestCalendarGaps: { after: string; before: string; missingWeekdays: number }[];
  largestPriceMoves: { date: string; previousClose: number; close: number; absoluteChangePct: number }[] };
export type Fill = { date: string; side: 'BUY' | 'SELL'; quantity: number; price: number; fee: number; reason: string };
export type BacktestResult = {
  symbol: string; fastPeriod: number; slowPeriod: number; initialCapital: number;
  finalEquity: number; returnPct: number; maxDrawdownPct: number; roundTrips: number;
  winRatePct: number; fills: Fill[]; equity: { date: string; equity: number }[];
  benchmarkReturnPct: number; excessReturnPct: number;
};
export type PaperPosition = { symbol: string; quantity: number; averagePrice: number; markPrice: number; marketValue: number; unrealizedPnL: number };
export type PaperSnapshot = {
  cash: number; equity: number; unrealizedPnL: number; positions: PaperPosition[];
  fills: { timestamp: string; symbol: string; side: string; quantity: number; price: number; fee: number }[];
};
export type ValidationReport = {
  symbol: string; trainStart: string; trainEnd: string; testStart: string; testEnd: string;
  fastPeriod: number; slowPeriod: number; training: BacktestResult; outOfSample: BacktestResult;
};
export type WalkForwardReport = {
  symbol: string; foldCount: number; positiveFoldPct: number; medianTestReturnPct: number;
  folds: { number: number; trainingStart: string; trainingEnd: string; testStart: string;
    testEnd: string; fastPeriod: number; slowPeriod: number; trainingReturnPct: number;
    testReturnPct: number; testMaxDrawdownPct: number; testRoundTrips: number }[];
};
export type ReplayOrder = { sequence: number; requestedAt: string; side: 'BUY' | 'SELL'; quantity: number;
  status: 'PENDING' | 'FILLED' | 'REJECTED'; executedAt?: string | null; price?: number | null; fee: number; reason?: string | null };
export type ReplaySnapshot = { symbol: string; currentDate: string; finalDate: string; cash: number;
  quantity: number; averageCost: number; markPrice: number; equity: number; unrealizedPnL: number;
  atEnd: boolean; remainingSessions: number; orders: ReplayOrder[] };
export type NewsInsight = { publishedAt: string; symbol: string; headline: string; source: string;
  sentimentScore: number; sentimentLabel: 'POSITIVE' | 'NEGATIVE' | 'NEUTRAL' };
export type AppInfo = { version: string; mode: string; execution: string; source: string };
type WebViewPort = { postMessage(message: unknown): void; addEventListener(type: 'message', handler: (event: MessageEvent) => void): void };
declare global { interface Window { chrome?: { webview?: WebViewPort } } }

type ResponseMessage = { id: string; ok: boolean; result?: unknown; error?: { code: string; message: string } };
type Pending = { resolve(value: unknown): void; reject(error: Error): void; timeout: ReturnType<typeof setTimeout> };

class DesktopBridge {
  private pending = new Map<string, Pending>();
  readonly connected: boolean;

  constructor() {
    const port = window.chrome?.webview;
    this.connected = Boolean(port);
    port?.addEventListener('message', (event) => {
      const message = event.data as ResponseMessage;
      if (!message || typeof message.id !== 'string') return;
      const pending = this.pending.get(message.id);
      if (!pending) return;
      clearTimeout(pending.timeout);
      this.pending.delete(message.id);
      if (message.ok) pending.resolve(message.result);
      else pending.reject(new Error(message.error?.message ?? 'Unexpected desktop service error'));
    });
  }

  call<T>(type: string, payload: Record<string, unknown> = {}, timeoutMs = 30_000): Promise<T> {
    const port = window.chrome?.webview;
    if (!port) return Promise.reject(new Error('This interface must run inside the Windows WebView2 desktop host.'));
    if (!/^[a-z]+\.[a-zA-Z]+$/.test(type)) return Promise.reject(new Error('Invalid command.'));
    const id = crypto.randomUUID();
    return new Promise<T>((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Desktop command '${type}' timed out.`));
      }, timeoutMs);
      this.pending.set(id, { resolve: (v) => resolve(v as T), reject, timeout });
      try { port.postMessage({ id, type, payload }); }
      catch (error) { clearTimeout(timeout); this.pending.delete(id); reject(error); }
    });
  }
}

export const bridge = new DesktopBridge();

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, ArrowDownRight, ArrowUpRight, BarChart3, ChevronDown, CircleAlert, Database, Download, FileUp, FlaskConical, GitCompareArrows, Layers3, Play, RefreshCw, Search, ShieldCheck, Wallet } from 'lucide-react';
import { bridge, type AppInfo, type Asset, type BacktestResult, type Candle, type PaperSnapshot, type Pattern, type PatternSearch, type Series, type ValidationReport, type WalkForwardReport } from './bridge';
import { buildPlot, buildTimePlot, normalized, sharedDomain, type ChartPoint } from './charts';

type Tab = 'compare' | 'strategy' | 'paper';
const cash = (value: number) => `${number(value)} u.m.`;
const pct = (value: number) => `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`;
const number = (value: number) => new Intl.NumberFormat('es-ES', { maximumFractionDigits: 2 }).format(value);
const dateLabel = (value?: string) => value ? new Date(`${value}T12:00:00`).toLocaleDateString('es-ES', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';

function MiniChart({ points, tone = 'cyan', domain }: { points: ChartPoint[]; tone?: 'cyan' | 'purple'; domain?: [number, number] }) {
  const plot = useMemo(() => buildPlot(points, 700, 170, domain), [points, domain]);
  return <svg viewBox="0 0 700 170" preserveAspectRatio="none" className={`mini-chart ${tone}`} role="img" aria-label="Serie histórica normalizada a porcentaje">
    <defs><linearGradient id={`area-${tone}`} x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor={tone === 'cyan' ? '#17d2d2' : '#a891ff'} stopOpacity=".2" /><stop offset="1" stopColor={tone === 'cyan' ? '#17d2d2' : '#a891ff'} stopOpacity="0" /></linearGradient></defs>
    {[.25,.5,.75].map(y => <line key={y} x1="0" x2="700" y1={170*y} y2={170*y} stroke="#ffffff" strokeOpacity=".08" strokeDasharray="4 6" />)}
    {points.length > 0 && <><path d={plot.fill} fill={`url(#area-${tone})`}/><path d={plot.path} fill="none" stroke={tone === 'cyan' ? '#19d5d0' : '#ad9eff'} strokeWidth="2.5" vectorEffect="non-scaling-stroke" /></>}
  </svg>;
}

function OverlayChart({ first, second }: { first: ChartPoint[]; second: ChartPoint[] }) {
  const domain = useMemo(() => sharedDomain(first, second), [first, second]);
  const firstDate = [first[0]?.date, second[0]?.date].filter((x): x is string => Boolean(x)).sort()[0] ?? '';
  const lastDate = [first.at(-1)?.date, second.at(-1)?.date].filter((x): x is string => Boolean(x)).sort().at(-1) ?? '';
  const a = useMemo(() => buildTimePlot(first, 900, 210, domain, firstDate, lastDate), [first, domain, firstDate, lastDate]);
  const b = useMemo(() => buildTimePlot(second, 900, 210, domain, firstDate, lastDate), [second, domain, firstDate, lastDate]);
  return <div className="overlay-chart">
    <div className="axis-labels"><span>{pct(domain[1])}</span><span>0,00%</span><span>{pct(domain[0])}</span></div>
    <svg viewBox="0 0 900 210" preserveAspectRatio="none" role="img" aria-label="Comparación de rentabilidad normalizada entre los dos activos">
      {[0,.25,.5,.75,1].map(y => <line key={y} x1="0" x2="900" y1={210*y} y2={210*y} stroke="#ffffff" strokeOpacity=".07" strokeDasharray="4 6"/>)}
      <line x1="0" x2="900" y1={a.zeroY} y2={a.zeroY} stroke="#6f819a" strokeOpacity=".6" strokeDasharray="3 4" />
      {first.length > 0 && <path d={a.path} stroke="#18d3cd" strokeWidth="2.7" vectorEffect="non-scaling-stroke" fill="none" />}
      {second.length > 0 && <path d={b.path} stroke="#ad9eff" strokeWidth="2.7" vectorEffect="non-scaling-stroke" fill="none" />}
    </svg>
  </div>;
}

function ShapeOverlay({ first, second }: { first: ChartPoint[]; second: ChartPoint[] }) {
  const domain = sharedDomain(first, second);
  const a = buildPlot(first, 900, 180, domain);
  const b = buildPlot(second, 900, 180, domain);
  return <div className="shape-chart"><svg viewBox="0 0 900 180" preserveAspectRatio="none" role="img" aria-label="Comparación de dos patrones de rendimientos por sesión relativa">
    {[0,.25,.5,.75,1].map(y=><line key={y} x1="0" x2="900" y1={180*y} y2={180*y} stroke="#ffffff" strokeOpacity=".08" strokeDasharray="4 5"/>)}
    <path d={a.path} stroke="#18d3cd" strokeWidth="2.5" fill="none" vectorEffect="non-scaling-stroke"/>
    <path d={b.path} stroke="#ae9bff" strokeWidth="2.5" fill="none" vectorEffect="non-scaling-stroke"/>
  </svg><div className="chart-footer"><span>SESIÓN 1</span><span>SESIÓN {Math.max(first.length,second.length)}</span></div></div>;
}

function AssetCard({ slot, series, onSelect, symbols, selected, cutoff, lookback }: { slot: 'A' | 'B'; series?: Series; onSelect: (s: string) => void; symbols: Asset[]; selected: string; cutoff?: string; lookback: number }) {
  const visible = useMemo(() => (series?.candles ?? []).filter(c => !cutoff || c.date <= cutoff).slice(-lookback), [series, cutoff, lookback]);
  const points = useMemo(() => normalized(visible), [visible]);
  const delta = points.at(-1)?.value ?? 0;
  const end = visible.at(-1);
  const first = visible[0];
  return <section className={`asset-card asset-${slot.toLowerCase()}`}>
    <div className="asset-topline"><span className={`slot-pill slot-${slot.toLowerCase()}`}>ACTIVO {slot}</span><span className="label-muted">Histórico diario · Cierre</span></div>
    <div className="asset-select-line">
      <div className="select-shell"><select aria-label={`Seleccionar activo ${slot}`} value={selected} onChange={e => onSelect(e.target.value)}><option value="">Seleccionar activo</option>{symbols.map(asset => <option key={asset.symbol} value={asset.symbol}>{asset.symbol} · {asset.name}</option>)}</select><ChevronDown size={16}/></div>
      <span className="asset-last">{end ? number(end.close) : '—'}</span>
    </div>
    <div className="asset-symbol-line"><span>{series ? `${visible.length} sesiones visibles` : 'Carga o descarga históricos para empezar'}</span><span className={delta >= 0 ? 'positive' : 'negative'}>{points.length > 1 ? pct(delta) : '—'}</span></div>
    <MiniChart points={points} tone={slot === 'A' ? 'cyan' : 'purple'} />
    <div className="chart-footer"><span>{dateLabel(first?.date)}</span><span>{dateLabel(end?.date)}</span></div>
  </section>;
}

function Stat({ label, value, small }: { label: string; value: string; small?: boolean }) {
  return <div className="stat"><span>{label}</span><strong className={small ? 'stat-small' : ''}>{value}</strong></div>;
}

export default function App() {
  const [tab, setTab] = useState<Tab>('compare');
  const [assets, setAssets] = useState<Asset[]>([]);
  const [selectedA, setSelectedA] = useState('');
  const [selectedB, setSelectedB] = useState('');
  const [seriesBySymbol, setSeriesBySymbol] = useState<Record<string, Series>>({});
  const [info, setInfo] = useState<AppInfo | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [ticker, setTicker] = useState('AAPL.US');
  const [lookback, setLookback] = useState(180);
  const [replayEnd, setReplayEnd] = useState<number | null>(null);
  const [windowSize, setWindowSize] = useState(30);
  const [patterns, setPatterns] = useState<PatternSearch | null>(null);
  const [selectedMatch, setSelectedMatch] = useState<Pattern | null>(null);
  const [fast, setFast] = useState(10);
  const [slow, setSlow] = useState(30);
  const [backtest, setBacktest] = useState<BacktestResult | null>(null);
  const [optimized, setOptimized] = useState<BacktestResult[]>([]);
  const [validation, setValidation] = useState<ValidationReport | null>(null);
  const [walkForward, setWalkForward] = useState<WalkForwardReport | null>(null);
  const [paper, setPaper] = useState<PaperSnapshot | null>(null);
  const [qty, setQty] = useState(1);

  const refresh = useCallback(async () => {
    const available = await bridge.call<Asset[]>('assets.list');
    setAssets(available);
    setSelectedA(previous => available.some(a => a.symbol === previous) ? previous : available[0]?.symbol ?? '');
    setSelectedB(previous => available.some(a => a.symbol === previous) ? previous : available[1]?.symbol ?? available[0]?.symbol ?? '');
  }, []);

  useEffect(() => {
    if (!bridge.connected) { setError('Es necesario abrir BotTrading desde la aplicación Windows. El navegador independiente no tiene acceso al motor cuantitativo.'); return; }
    Promise.all([bridge.call<AppInfo>('app.info'), bridge.call<Asset[]>('assets.list')])
      .then(([data, items]) => { setInfo(data); setAssets(items); setSelectedA(items[0]?.symbol ?? ''); setSelectedB(items[1]?.symbol ?? items[0]?.symbol ?? ''); })
      .catch(e => setError(String(e.message)));
  }, []);

  useEffect(() => {
    for (const symbol of new Set([selectedA, selectedB].filter(Boolean))) {
      if (!seriesBySymbol[symbol]) bridge.call<Series>('assets.series', { symbol })
        .then(item => setSeriesBySymbol(current => ({ ...current, [symbol]: item })))
        .catch(e => setError(String(e.message)));
    }
  }, [selectedA, selectedB, seriesBySymbol]);

  const a = seriesBySymbol[selectedA];
  const b = seriesBySymbol[selectedB];
  const cutoff = a ? a.candles[replayEnd ?? a.candles.length - 1]?.date : undefined;
  const visibleA = useMemo(() => a?.candles.filter(c => !cutoff || c.date <= cutoff).slice(-lookback) ?? [], [a, cutoff, lookback]);
  const visibleB = useMemo(() => b?.candles.filter(c => !cutoff || c.date <= cutoff).slice(-lookback) ?? [], [b, cutoff, lookback]);
  const aligned = useMemo(() => {
    const bDates = new Set(visibleB.map(c => c.date));
    const firstShared = visibleA.find(c => bDates.has(c.date))?.date;
    return firstShared ? { a: visibleA.filter(c => c.date >= firstShared), b: visibleB.filter(c => c.date >= firstShared) }
      : { a: [] as Candle[], b: [] as Candle[] };
  }, [visibleA, visibleB]);
  const pointsA = useMemo(() => normalized(aligned.a), [aligned]);
  const pointsB = useMemo(() => normalized(aligned.b), [aligned]);
  const sameAssets = Boolean(selectedA && selectedA === selectedB);
  const matchSeries = selectedMatch && seriesBySymbol[selectedMatch.symbol];
  const matchCandles = useMemo(() => matchSeries?.candles.filter(c => selectedMatch && c.date >= selectedMatch.start && c.date <= selectedMatch.end) ?? [], [matchSeries, selectedMatch]);
  const queryCandles = useMemo(() => a?.candles.filter(c => patterns && c.date >= patterns.queryStart && c.date <= patterns.queryEnd) ?? [], [a, patterns]);

  async function task<T>(operation: () => Promise<T>, onSuccess?: (result: T) => void | Promise<void>) {
    setBusy(true); setError('');
    try { const result = await operation(); if (onSuccess) await onSuccess(result); }
    catch (err) { setError(err instanceof Error ? err.message : String(err)); }
    finally { setBusy(false); }
  }

  async function addAsset(importCsv: boolean) {
    const symbol = ticker.trim().toUpperCase();
    if (!/^[A-Z0-9][A-Z0-9._-]{0,19}$/.test(symbol)) { setError('Ticker inválido (1–20 caracteres A-Z, 0-9, punto, guion).'); return; }
    await task(async () => bridge.call<Asset & { cancelled?: boolean }>(importCsv ? 'assets.importCsv' : 'assets.download', { symbol }, importCsv ? 120_000 : 40_000),
      async result => {
        if (result.cancelled) return;
        setSeriesBySymbol(current => { const next = { ...current }; delete next[symbol]; return next; });
        await refresh();
        if (!selectedA) setSelectedA(symbol);
        else if (!selectedB || selectedB === selectedA) setSelectedB(symbol);
        setReplayEnd(null); setPatterns(null); setSelectedMatch(null); setBacktest(null); setOptimized([]); setValidation(null); setWalkForward(null);
      });
  }

  function changeSelection(slot: 'A' | 'B', symbol: string) {
    if (slot === 'A') { setSelectedA(symbol); setReplayEnd(null); }
    else setSelectedB(symbol);
    setPatterns(null); setSelectedMatch(null); setBacktest(null); setOptimized([]); setValidation(null); setWalkForward(null);
  }

  async function loadPaper() { await task(() => bridge.call<PaperSnapshot>('paper.snapshot'), setPaper); }
  useEffect(() => { if (tab === 'paper' && bridge.connected) void loadPaper(); }, [tab]); // Intentional refresh on tab activation.

  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Activity size={22} strokeWidth={2.8}/></div><div><strong>BOT<span>TRADING</span></strong><small>QUANT WORKSTATION</small></div></div>
      <div className="nav-label">WORKSPACE</div>
      <nav aria-label="Navegación principal">
        <button className={tab === 'compare' ? 'nav-item active' : 'nav-item'} onClick={() => setTab('compare')}><GitCompareArrows size={19}/> Comparador de activos</button>
        <button className={tab === 'strategy' ? 'nav-item active' : 'nav-item'} onClick={() => setTab('strategy')}><FlaskConical size={19}/> Strategy Lab</button>
        <button className={tab === 'paper' ? 'nav-item active' : 'nav-item'} onClick={() => setTab('paper')}><Wallet size={19}/> Paper Trading</button>
      </nav>
      <div className="side-spacer" />
      <div className="sidebar-note"><ShieldCheck size={17}/><span>Entorno de simulación<br/><strong>Sin órdenes reales</strong></span></div>
      <div className="side-version">ENGINE v{info?.version ?? '—'} <span className={bridge.connected ? 'connection connected' : 'connection'} /> {bridge.connected ? 'LOCAL' : 'OFFLINE'}</div>
    </aside>
    <div className="main-area">
      <header className="topbar"><div className="top-breadcrumb">WORKSPACE <span>/</span> {tab === 'compare' ? 'MARKET EXPLORER' : tab === 'strategy' ? 'STRATEGY LAB' : 'PAPER TRADING'}</div><div className="top-right"><span className="live-marker"><span/> HISTÓRICOS · NO EN VIVO</span><span className="user-avatar">BT</span></div></header>
      <main>
        {error && <div role="alert" className="error-banner"><CircleAlert size={18}/><span>{error}</span><button onClick={() => setError('')} aria-label="Cerrar error">×</button></div>}
        <section className="page-heading"><div><div className="eyebrow"><Layers3 size={13}/> RESEARCH TERMINAL / 01</div><h1>{tab === 'compare' ? 'Explorador de mercado' : tab === 'strategy' ? 'Laboratorio de estrategias' : 'Cartera simulada'}</h1><p>{tab === 'compare' ? 'Compara activos, identifica estructuras repetidas y explora los datos sin sesgo de escala.' : tab === 'strategy' ? 'Evalúa cruces de medias con ejecución diferida, costes y control de drawdown.' : 'Órdenes simuladas a precios históricos. No se envían operaciones a ningún bróker.'}</p></div><div className="heading-badge"><span className="heading-dot"/> WORKSPACE LOCAL</div></section>

        <section className="data-toolbar"><div className="toolbar-title"><Database size={18}/><span>FUENTES DE DATOS</span><strong>{assets.length} activos</strong></div><div className="toolbar-form"><label className="sr-only" htmlFor="ticker">Ticker o símbolo de importación</label><input id="ticker" value={ticker} maxLength={20} onChange={e => setTicker(e.target.value.toUpperCase())} placeholder="Ej. AAPL.US"/><button className="btn secondary" disabled={busy} onClick={() => addAsset(true)}><FileUp size={15}/> Importar CSV</button><button className="btn primary" disabled={busy} onClick={() => addAsset(false)}><Download size={15}/>{busy ? 'Procesando…' : 'Descargar histórico'}</button></div></section>
        {assets.length === 0 && <div className="empty-state"><Database size={32}/><h2>Workspace sin datos históricos</h2><p>Descarga velas diarias gratuitas desde Stooq con un ticker como AAPL.US o importa un CSV OHLC. Los datos quedan almacenados en tu perfil Windows.</p></div>}

        {tab === 'compare' && <>
          <div className="section-heading"><div><h2>Comparador dual</h2><p>Dos activos, mismo corte temporal y ventanas equivalentes</p></div><div className="segmented" role="group" aria-label="Ventana de sesiones">{[90,180,365].map(n => <button key={n} aria-pressed={lookback === n} className={lookback === n ? 'chosen' : ''} onClick={() => setLookback(n)}>{n}D</button>)}</div></div>
          <div className="dual-cards"><AssetCard slot="A" selected={selectedA} onSelect={v => changeSelection('A', v)} symbols={assets} series={a} cutoff={cutoff} lookback={lookback}/><AssetCard slot="B" selected={selectedB} onSelect={v => changeSelection('B', v)} symbols={assets} series={b} cutoff={cutoff} lookback={lookback}/></div>
          <section className="panel overlay-panel"><div className="panel-header"><div><div className="section-eyebrow">NORMALIZED PERFORMANCE</div><h2>Rendimiento relativo</h2><p>Variación porcentual desde la primera fecha común; eje X en tiempo calendario real.</p></div><div className="legend"><span><i className="dot cyan"/>{selectedA || 'Activo A'}</span><span><i className="dot purple"/>{selectedB || 'Activo B'}</span></div></div><OverlayChart first={pointsA} second={pointsB}/><div className="chart-footer"><span>{dateLabel(aligned.a[0]?.date)}</span><span>{dateLabel(cutoff)}</span></div>{a && b && aligned.a.length===0 && <p className="inline-warning">Los instrumentos no tienen fechas de cotización comunes en la ventana actual.</p>}{sameAssets && <p className="inline-warning">Has seleccionado el mismo activo dos veces; para patrones entre instrumentos, elige otro en B.</p>}</section>
          <section className="panel replay-panel"><div className="replay-title"><Play size={17}/><div><h3>Replay histórico sincronizado</h3><p>Limita el gráfico y la búsqueda de patrones a información disponible hasta la fecha seleccionada.</p></div></div><div className="replay-control"><label htmlFor="replay">FECHA DE CORTE</label><input id="replay" type="range" min={0} max={Math.max(1,(a?.candles.length ?? 1)-1)} value={replayEnd ?? Math.max(0,(a?.candles.length ?? 1)-1)} disabled={!a || a.candles.length < 2} onChange={e => { setReplayEnd(Number(e.target.value)); setPatterns(null); setSelectedMatch(null); }} /><strong>{dateLabel(cutoff)}</strong><button className="btn flat" onClick={() => {setReplayEnd(null); setPatterns(null); setSelectedMatch(null);}} disabled={replayEnd === null}>Restablecer</button></div></section>
          <section className="panel pattern-panel"><div className="panel-header"><div><div className="section-eyebrow">PATTERN INTELLIGENCE</div><h2>Detector de patrones repetidos</h2><p>{selectedB && !sameAssets ? `Búsqueda dirigida: ${selectedA} frente a ${selectedB}` : 'Búsqueda en todo el catálogo'} · correlación de rendimientos sin anticipación.</p></div><div className="pattern-actions"><label htmlFor="pattern-window">VENTANA</label><select id="pattern-window" value={windowSize} onChange={e => setWindowSize(Number(e.target.value))}>{[10,20,30,60,90,120].map(n => <option key={n} value={n}>{n} sesiones</option>)}</select><button className="btn primary" disabled={busy || !a || a.candles.filter(c=>!cutoff||c.date<=cutoff).length<windowSize} onClick={() => task(() => bridge.call<PatternSearch>('patterns.find',{symbol:selectedA,window:windowSize,maxResults:12,endDate:cutoff,targetSymbol:selectedB&&!sameAssets?selectedB:null},60_000), result => {setPatterns(result);setSelectedMatch(null);})}><Search size={15}/> Buscar patrones</button></div></div>
            {patterns ? <div className="matches"><div className="match-header"><span>INSTRUMENTO</span><span>INTERVALO</span><span>CORRELACIÓN</span><span>DISTANCIA</span></div>{patterns.matches.map((m,i) => <button className={`match-row match-button ${selectedMatch?.start===m.start&&selectedMatch?.symbol===m.symbol?'selected':''}`} key={`${m.symbol}-${m.start}-${i}`} onClick={() => {setSelectedMatch(m);if (!seriesBySymbol[m.symbol]) void bridge.call<Series>('assets.series',{symbol:m.symbol}).then(item=>setSeriesBySymbol(current=>({...current,[m.symbol]:item}))).catch(e=>setError(String(e.message)));}}><strong><span className="rank">{String(i+1).padStart(2,'0')}</span>{m.symbol}</strong><span>{dateLabel(m.start)} — {dateLabel(m.end)}</span><span className="correlation"><span className="bar"><i style={{width:`${Math.max(0,(m.correlation+1)*50)}%`}}/></span>{(m.correlation*100).toFixed(1)}%</span><span>{m.distance.toFixed(5)}</span></button>)}{patterns.matches.length===0&&<div className="table-empty">No hay ventanas comparables para este intervalo; incorpora más históricos.</div>}</div>:<div className="table-empty">Selecciona un activo con suficientes velas y ejecuta la búsqueda para ver coincidencias.</div>}
            {selectedMatch && matchCandles.length > 1 && queryCandles.length > 1 && <div className="pattern-detail"><div className="pattern-detail-heading"><strong>Huella de patrón · {selectedMatch.symbol}</strong><span>{dateLabel(selectedMatch.start)} — {dateLabel(selectedMatch.end)}</span></div><p>Comparación de forma: cada ventana se normaliza desde su primera vela, sin pretender simultaneidad temporal.</p><ShapeOverlay first={normalized(queryCandles)} second={normalized(matchCandles)}/><div className="legend"><span><i className="dot cyan"/>Consulta A</span><span><i className="dot purple"/>Coincidencia {selectedMatch.symbol}</span></div></div>}
          </section>
        </>}

        {tab === 'strategy' && <>
          <div className="strategy-layout"><section className="panel"><div className="section-eyebrow">BACKTEST ENGINE / SMA CROSSOVER</div><h2>Configuración de estrategia</h2><p className="panel-description">Compra cuando SMA rápida cruza al alza la lenta. Venta en cruce bajista. Señal al cierre, ejecución a la siguiente apertura.</p><div className="field-grid"><label>ACTIVO<select value={selectedA} onChange={e=>changeSelection('A',e.target.value)}><option value="">Seleccionar</option>{assets.map(x=><option key={x.symbol} value={x.symbol}>{x.symbol}</option>)}</select></label><label>SMA RÁPIDA<input type="number" min="2" max="249" value={fast} onChange={e=>setFast(Number(e.target.value))}/></label><label>SMA LENTA<input type="number" min="3" max="250" value={slow} onChange={e=>setSlow(Number(e.target.value))}/></label></div><div className="assumptions"><span>Capital: 10.000 u.m.</span><span>Fee: 5 bps</span><span>Slippage: 5 bps</span><span>Solo posiciones largas</span></div><div className="button-row"><button className="btn primary" disabled={busy||!a||fast<2||fast>=slow||slow>250} onClick={()=>task(()=>bridge.call<BacktestResult>('backtest.run',{symbol:selectedA,fastPeriod:fast,slowPeriod:slow}),setBacktest)}><Play size={16}/> Ejecutar backtest</button><button className="btn secondary" disabled={busy||!a} onClick={()=>task(()=>bridge.call<BacktestResult[]>('backtest.optimize',{symbol:selectedA},60_000),setOptimized)}><RefreshCw size={15}/> Explorar parámetros</button><button className="btn secondary" disabled={busy||!a||a.candles.length<300} onClick={()=>task(()=>bridge.call<ValidationReport>('strategy.validate',{symbol:selectedA},60_000),setValidation)}><ShieldCheck size={15}/> Validar fuera de muestra</button><button className="btn secondary" disabled={busy||!a||a.candles.length<402} onClick={()=>task(()=>bridge.call<WalkForwardReport>('strategy.walkForward',{symbol:selectedA},120_000),setWalkForward)}><ShieldCheck size={15}/> Walk-forward · 4 ventanas</button></div><p className="caution">Resultados históricos, no predicciones. La optimización se calcula dentro de la muestra: necesita validación fuera de muestra antes de cualquier uso práctico.</p></section>
          <section className="panel results-panel"><div className="section-eyebrow">PERFORMANCE REPORT</div><h2>Resultado de simulación</h2>{backtest?<><div className="kpi-grid"><Stat label="CAPITAL FINAL" value={cash(backtest.finalEquity)}/><Stat label="RENTABILIDAD" value={pct(backtest.returnPct)}/><Stat label="MAX. DRAWDOWN" value={`-${number(backtest.maxDrawdownPct)}%`}/><Stat label="OPERACIONES CERRADAS" value={String(backtest.roundTrips)}/><Stat label="OPERACIONES GANADORAS" value={number(backtest.winRatePct)+'%'}/><Stat label="PARÁMETROS" value={`${backtest.fastPeriod} / ${backtest.slowPeriod}`} small/></div><div className="equity-chart"><MiniChart points={backtest.equity.map(p=>({date:p.date,value:(p.equity/backtest.initialCapital-1)*100}))}/></div><h3>Últimas ejecuciones</h3><div className="fills">{backtest.fills.slice(-6).reverse().map((f,i)=><div key={i}><span className={f.side==='BUY'?'positive':'negative'}>{f.side==='BUY'?<ArrowUpRight size={14}/>:<ArrowDownRight size={14}/>} {f.side}</span><span>{dateLabel(f.date)}</span><strong>{f.quantity} × {number(f.price)}</strong></div>)}{backtest.fills.length===0&&<p>No se produjeron cruces operables en el período disponible.</p>}</div></>:<div className="no-result"><BarChart3 size={38}/><span>Ejecuta un backtest para ver métricas y curva de capital.</span></div>}</section></div>
          {validation&&<section className="panel"><div className="section-eyebrow">CHRONOLOGICAL HOLDOUT / 70% TRAIN · 30% TEST</div><h2>Validación fuera de muestra</h2><p className="panel-description">Parámetros seleccionados exclusivamente con datos de entrenamiento; resultado evaluado sobre fechas posteriores y sin reajustar los parámetros.</p><div className="validation-grid"><div><span>ENTRENAMIENTO · {dateLabel(validation.trainStart)} — {dateLabel(validation.trainEnd)}</span><strong>{pct(validation.training.returnPct)}</strong><small>Drawdown: {number(validation.training.maxDrawdownPct)}%</small></div><div><span>PRUEBA · {dateLabel(validation.testStart)} — {dateLabel(validation.testEnd)}</span><strong className={validation.outOfSample.returnPct>=0?'positive':'negative'}>{pct(validation.outOfSample.returnPct)}</strong><small>Drawdown: {number(validation.outOfSample.maxDrawdownPct)}% · SMA {validation.fastPeriod}/{validation.slowPeriod}</small></div></div><p className="caution">Un único holdout cronológico no sustituye validación walk-forward múltiple ni garantiza rendimiento futuro.</p></section>}
          {walkForward&&<section className="panel"><div className="section-eyebrow">EXPANDING WALK-FORWARD / 4 NON-OVERLAPPING TEST WINDOWS</div><h2>Estabilidad temporal de parámetros</h2><p className="panel-description">Cada ventana recalibra la estrategia con el pasado exclusivamente y prueba en el bloque futuro siguiente. Las rentabilidades por ventana son independientes; no se agregan como una rentabilidad de cartera continua.</p><div className="validation-grid"><div><span>VENTANAS DE PRUEBA POSITIVAS</span><strong>{number(walkForward.positiveFoldPct)}%</strong></div><div><span>MEDIANA DE RENTABILIDAD POR VENTANA</span><strong className={walkForward.medianTestReturnPct>=0?'positive':'negative'}>{pct(walkForward.medianTestReturnPct)}</strong></div></div><div className="matches"><div className="match-header optimize-header"><span>VENTANA / SMA</span><span>PRUEBA</span><span>DRAWDOWN</span><span>CIERRES</span></div>{walkForward.folds.map(f=><div className="match-row optimize-header" key={f.number}><strong>{f.number} · {f.fastPeriod}/{f.slowPeriod}<small> {dateLabel(f.testStart)} – {dateLabel(f.testEnd)}</small></strong><span className={f.testReturnPct>=0?'positive':'negative'}>{pct(f.testReturnPct)}</span><span>{number(f.testMaxDrawdownPct)}%</span><span>{f.testRoundTrips}</span></div>)}</div><p className="caution">Las ventanas son disjuntas, sin anticipación de datos. No se modelan cambios de régimen, sesgo de supervivencia ni acciones corporativas.</p></section>}
                    {optimized.length>0&&<section className="panel"><div className="section-eyebrow">IN-SAMPLE PARAMETER GRID</div><h2>Exploración de parámetros</h2><p className="panel-description">Ordenado por capital final dentro de la misma muestra. No constituye evidencia de robustez.</p><div className="matches"><div className="match-header optimize-header"><span>RÁPIDA / LENTA</span><span>RENTABILIDAD</span><span>DRAWDOWN</span><span>CIERRES</span></div>{optimized.map(r=><div className="match-row optimize-header" key={`${r.fastPeriod}-${r.slowPeriod}`}><strong>{r.fastPeriod} / {r.slowPeriod}</strong><span className={r.returnPct>=0?'positive':'negative'}>{pct(r.returnPct)}</span><span>{number(r.maxDrawdownPct)}%</span><span>{r.roundTrips}</span></div>)}</div></section>}
        </>}

        {tab==='paper'&&<div className="strategy-layout"><section className="panel"><div className="section-eyebrow">SIMULATION ONLY / NO BROKER CONNECTION</div><h2>Orden simulada</h2><p className="panel-description">Las ejecuciones usan el último cierre del histórico cargado con coste y deslizamiento simulados. No representan cotizaciones actuales.</p><div className="field-grid"><label>INSTRUMENTO<select value={selectedA} onChange={e=>changeSelection('A',e.target.value)}><option value="">Seleccionar</option>{assets.map(x=><option key={x.symbol} value={x.symbol}>{x.symbol}</option>)}</select></label><label>CANTIDAD<input type="number" min="1" max="1000000" value={qty} onChange={e=>setQty(Number(e.target.value))}/></label></div><div className="button-row"><button className="btn primary" disabled={busy||!a||qty<1||!Number.isInteger(qty)} onClick={()=>task(()=>bridge.call('paper.order',{symbol:selectedA,side:'BUY',quantity:qty}),()=>{void loadPaper();})}><ArrowUpRight size={16}/> Comprar (simulado)</button><button className="btn secondary" disabled={busy||!a||qty<1||!Number.isInteger(qty)} onClick={()=>task(()=>bridge.call('paper.order',{symbol:selectedA,side:'SELL',quantity:qty}),()=>{void loadPaper();})}><ArrowDownRight size={16}/> Vender (simulado)</button></div><p className="caution">Sin apalancamiento, ventas en corto, FX ni cotizaciones en vivo. Hasta incorporar divisas, la cartera admite un único instrumento. Los datos persisten localmente.</p><button className="danger-link" disabled={busy} onClick={()=>{if(window.confirm('¿Restablecer toda la cartera simulada a 10.000 unidades monetarias? Se perderán las operaciones anteriores.')) void task(()=>bridge.call<PaperSnapshot>('paper.reset'),setPaper);}}>Restablecer cartera simulada</button></section><section className="panel results-panel"><div className="section-eyebrow">PAPER PORTFOLIO</div><h2>Balance de cartera</h2>{paper?<><div className="kpi-grid"><Stat label="PATRIMONIO SIMULADO" value={cash(paper.equity)}/><Stat label="EFECTIVO DISPONIBLE" value={cash(paper.cash)}/><Stat label="P/L NO REALIZADO" value={cash(paper.unrealizedPnL)}/><Stat label="POSICIONES" value={String(paper.positions.length)}/></div><h3>Posiciones abiertas</h3><div className="positions">{paper.positions.map(p=><div key={p.symbol}><strong>{p.symbol}</strong><span>{p.quantity} uds. · {number(p.averagePrice)} media</span><span className={p.unrealizedPnL>=0?'positive':'negative'}>{cash(p.unrealizedPnL)}</span></div>)}{paper.positions.length===0&&<p>No hay posiciones abiertas.</p>}</div><h3>Últimas operaciones</h3><div className="fills">{paper.fills.slice(-6).reverse().map((f,i)=><div key={i}><span className={f.side==='BUY'?'positive':'negative'}>{f.side}</span><span>{f.symbol} · {f.quantity} uds.</span><strong>{cash(f.price)}</strong></div>)}{paper.fills.length===0&&<p>Sin operaciones ejecutadas.</p>}</div></>:<div className="no-result"><Wallet size={36}/><span>No se ha recuperado el balance.</span></div>}</section></div>}

        <footer><span><ShieldCheck size={14}/> Solo investigación y simulación; no constituye asesoramiento financiero.</span><span>LOCAL-FIRST · GRATIS · SIN BRÓKER</span></footer>
      </main>
    </div>
  </div>;
}

import { useMemo, useState } from 'react';
import { QueryClientProvider, useQuery } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import { ApiUnreachable } from '../components/ApiUnreachable';
import { NexusDetailPanel } from '../components/NexusDetailPanel';
import { moduleApi } from '../services/apiHost';
import { formatInTz } from '../services/timezone';
import '../styles/modules.css';
import '../styles/nexus.css';

const API = moduleApi(5700);
export const BASE = `${API}/api/nexus/sentinel`;

const qc = makeModuleQueryClient(20_000);

type Page = 'watchlist' | 'portfolio' | 'alerts';

const TABS: { id: Page; label: string }[] = [
  { id: 'watchlist', label: 'Watchlist' },
  { id: 'portfolio',  label: 'Portfolio' },
  { id: 'alerts',     label: 'Alerts' },
];

const MC = 'var(--nexus)';
const style = { '--mc': MC } as React.CSSProperties;

// ── Types (mirrors API_CONTRACT.md) ──────────────────────────────────────

interface BoardRow {
  symbol: string;
  price: number;
  changePct: number | null;
  action: string;
  conviction: number;
  composite: number;
  edge: number;
  riskApproved: boolean;
  riskEntry: number | null;
  riskStop: number | null;
  riskTarget: number | null;
  riskRr: number | null;
  recommendedStyle: string | null;
  swingSide: string | null;
  swingEntryLow: number | null;
  swingEntryHigh: number | null;
  dayOrState: string | null;
  dayBias: string | null;
  dayVwap: number | null;
  dayRvol: number | null;
  freshness: string;
  ranAt: string;
}

interface StatusDto {
  schemaVersion: number;
  lastRunAt: string | null;
  marketOpen: boolean;
  trackedCount: number;
  openAlerts24h: number;
}

interface PositionRow {
  symbol: string;
  quantity: number;
  avgCost: number;
  currentPrice: number;
  marketValue: number;
  unrealizedPl: number;
  unrealizedPlPct: number;
  updatedAt: string;
}

export interface Signal { name: string; score: number; stance: string; detail: string; value: number | null; }
export interface Report { analyst: string; stance: string; score: number; summary: string; signals: Signal[]; }
export interface DayTrade {
  session: string; interval: string; vwap: number; priceVsVwapPct: number;
  orLow: number; orHigh: number; orState: string; rvol: number; bias: string; notes: string[];
}
export interface TradePlan {
  recommendedStyle: string; dayTradeScore: number; swingTradeScore: number;
  dayNotes: string[]; swingNotes: string[]; swingSide: string | null;
  swingEntryLow: number | null; swingEntryHigh: number | null;
  swingStop: number | null; swingTarget: number | null; dayTrade: DayTrade | null;
}
export interface TickerDetail {
  symbol: string; action: string; conviction: number; price: number; composite: number;
  asOf: string; source: string; thesis: string;
  meta: { company?: string; cap?: number; pe?: number | null; changePct?: number; freshness?: string; setups?: string[]; patterns?: string[] };
  debate: { bullPoints: string[]; bearPoints: string[]; bullStrength: number; bearStrength: number; edge: number; synthesis: string };
  risk: { approved: boolean; positionPct: number; entry: number; stop: number; target: number; maxRiskPct: number; rr: number; checks: string[]; flags: string[] };
  reports: Report[];
  tradePlan: TradePlan | null;
}

// ── Helpers ───────────────────────────────────────────────────────────────

export const get = <T,>(url: string): Promise<T> =>
  fetch(url, { headers: authHeaders() }).then(r => {
    if (!r.ok) throw new Error(r.status.toString());
    return r.json();
  });

export function friendlyError(e: unknown): string {
  const msg = e instanceof Error ? e.message : String(e);
  if (msg === 'Failed to fetch' || /network|load failed/i.test(msg))
    return "Can't reach Nexus. Make sure the Nexus API is running on port 5700 (start the full stack with maaya-start.ps1).";
  if (msg === '503') return "Sentinel's database isn't there yet — the engine hasn't started or written its first cycle.";
  if (msg === '401') return 'Your session expired. Please sign out and sign in again.';
  if (msg === '404') return 'No verdict for this symbol yet.';
  return `Something went wrong (${msg}).`;
}

export function fmtMoney(n: number, decimals = 2) {
  const sign = n < 0 ? '-' : '';
  return `${sign}$${Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals })}`;
}
export function fmtCap(n?: number | null) {
  if (n == null) return '—';
  if (n >= 1e12) return `$${(n / 1e12).toFixed(2)}T`;
  if (n >= 1e9) return `$${(n / 1e9).toFixed(2)}B`;
  if (n >= 1e6) return `$${(n / 1e6).toFixed(1)}M`;
  return fmtMoney(n, 0);
}
export function fmtPct(n: number | null | undefined, decimals = 2) {
  if (n == null) return '—';
  const sign = n > 0 ? '+' : '';
  return `${sign}${n.toFixed(decimals)}%`;
}
export function relTime(iso: string | null) {
  if (!iso) return '—';
  const diffMs = Date.now() - new Date(iso).getTime();
  const mins = Math.round(diffMs / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.round(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.round(hrs / 24)}d ago`;
}

export function ActionBadge({ action }: { action: string }) {
  return <span className={`nexus-action-badge ${action.toLowerCase()}`}>{action}</span>;
}

export function FreshnessBadge({ freshness }: { freshness: string }) {
  return (
    <span className={`nexus-freshness-badge ${freshness.toLowerCase()}`}>
      <span className="dot" />{freshness}
    </span>
  );
}

export function ConvictionPips({ value }: { value: number }) {
  return (
    <span className="nexus-conviction-track" title={`${value}/10`}>
      {Array.from({ length: 10 }, (_, i) => (
        <span key={i} className={`nexus-conviction-pip ${i < value ? 'filled' : ''}`} />
      ))}
    </span>
  );
}

// ── Status bar ───────────────────────────────────────────────────────────

function StatusBar() {
  const { data, isError } = useQuery<StatusDto>({
    queryKey: ['nexus-status'],
    queryFn: () => get(`${BASE}/status`),
    refetchInterval: 45_000,
  });
  if (isError || !data) return null;

  const staleWarning = data.marketOpen && data.lastRunAt &&
    (Date.now() - new Date(data.lastRunAt).getTime()) > 30 * 60 * 1000;

  return (
    <div className="nexus-status-bar">
      <span className="nexus-status-item">
        <span className={`nexus-status-dot ${data.marketOpen ? 'open' : 'closed'}`} />
        Market {data.marketOpen ? <b>Open</b> : <b>Closed</b>}
      </span>
      <span className="nexus-status-item">Last run <b>{relTime(data.lastRunAt)}</b></span>
      <span className="nexus-status-item">Tracking <b>{data.trackedCount}</b></span>
      <span className="nexus-status-item">Alerts (24h) <b>{data.openAlerts24h}</b></span>
      {staleWarning && <span className="nexus-stale-banner">Data may be delayed — check the engine</span>}
    </div>
  );
}

// ── Watchlist / board ────────────────────────────────────────────────────

type SortKey = 'conviction' | 'price' | 'changePct' | 'symbol';

function WatchlistPage({ onSelect }: { onSelect: (symbol: string) => void }) {
  const [sortKey, setSortKey] = useState<SortKey>('conviction');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');

  const boardQ = useQuery<BoardRow[]>({
    queryKey: ['nexus-board'],
    queryFn: () => get(`${BASE}/board`),
    refetchInterval: 45_000,
  });

  const rows = useMemo(() => {
    const data = boardQ.data ?? [];
    const sorted = [...data].sort((a, b) => {
      const av = a[sortKey] ?? -Infinity;
      const bv = b[sortKey] ?? -Infinity;
      if (typeof av === 'string' && typeof bv === 'string') return av.localeCompare(bv);
      return (av as number) - (bv as number);
    });
    return sortDir === 'desc' ? sorted.reverse() : sorted;
  }, [boardQ.data, sortKey, sortDir]);

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) setSortDir(d => (d === 'desc' ? 'asc' : 'desc'));
    else { setSortKey(key); setSortDir('desc'); }
  };

  if (boardQ.isError) {
    const msg = friendlyError(boardQ.error);
    if (msg.startsWith("Can't reach")) return <ApiUnreachable name="Nexus" port={5700} mc={MC} onRetry={() => boardQ.refetch()} />;
    return (
      <div className="module-empty" style={style}>
        <div className="module-empty-icon">⚠️</div>
        <h2>Sentinel isn't ready</h2>
        <p>{msg}</p>
        <button className="module-retry-btn" onClick={() => boardQ.refetch()}>Retry</button>
      </div>
    );
  }

  if (boardQ.isPending) {
    return <div className="module-empty" style={style}><p>Loading the desk…</p></div>;
  }

  if (rows.length === 0) {
    return (
      <div className="module-empty" style={style}>
        <div className="module-empty-icon">📋</div>
        <h2>No symbols tracked yet</h2>
        <p>Sentinel hasn't screened anything yet — this is normal before market open or before the first cycle runs.</p>
      </div>
    );
  }

  const Th = ({ k, children, align }: { k: SortKey; children: React.ReactNode; align?: 'right' }) => (
    <th className={sortKey === k ? 'sorted' : ''} style={align ? { textAlign: 'right' } : undefined} onClick={() => toggleSort(k)}>
      {children}{sortKey === k ? (sortDir === 'desc' ? ' ▾' : ' ▴') : ''}
    </th>
  );

  return (
    <div style={style}>
      <div className="nexus-board-wrap">
        <table className="nexus-board">
          <thead>
            <tr>
              <Th k="symbol">Symbol</Th>
              <Th k="price" align="right">Price</Th>
              <Th k="changePct" align="right">Chg %</Th>
              <th>Action</th>
              <Th k="conviction">Conviction</Th>
              <th>Freshness</th>
              <th>Ran</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(r => (
              <tr key={r.symbol} onClick={() => onSelect(r.symbol)}>
                <td className="nexus-sym-cell">{r.symbol}</td>
                <td className="nexus-price-cell" style={{ textAlign: 'right' }}>{fmtMoney(r.price)}</td>
                <td style={{ textAlign: 'right' }}>
                  <span className={`nexus-chg ${r.changePct == null ? 'flat' : r.changePct > 0 ? 'up' : r.changePct < 0 ? 'down' : 'flat'}`}>
                    {fmtPct(r.changePct)}
                  </span>
                </td>
                <td><ActionBadge action={r.action} /></td>
                <td><ConvictionPips value={r.conviction} /></td>
                <td><FreshnessBadge freshness={r.freshness} /></td>
                <td style={{ color: 'var(--text3)', fontSize: '0.72rem' }}>{relTime(r.ranAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Portfolio ────────────────────────────────────────────────────────────

function PortfolioPage() {
  const posQ = useQuery<PositionRow[]>({
    queryKey: ['nexus-positions'],
    queryFn: () => get(`${BASE}/positions`),
    refetchInterval: 45_000,
  });

  if (posQ.isError) {
    const msg = friendlyError(posQ.error);
    if (msg.startsWith("Can't reach")) return <ApiUnreachable name="Nexus" port={5700} mc={MC} onRetry={() => posQ.refetch()} />;
    return (
      <div className="module-empty" style={style}>
        <div className="module-empty-icon">⚠️</div>
        <h2>Sentinel isn't ready</h2>
        <p>{msg}</p>
        <button className="module-retry-btn" onClick={() => posQ.refetch()}>Retry</button>
      </div>
    );
  }

  if (posQ.isPending) return <div className="module-empty" style={style}><p>Loading positions…</p></div>;

  const positions = posQ.data ?? [];
  if (positions.length === 0) {
    return (
      <div className="module-empty" style={style}>
        <div className="module-empty-icon">💼</div>
        <h2>No positions</h2>
        <p>Nothing held right now — this is normal until the Robinhood portfolio loop runs.</p>
      </div>
    );
  }

  return (
    <div style={style}>
      <div className="nexus-board-wrap">
        <table className="nexus-board">
          <thead>
            <tr>
              <th>Symbol</th>
              <th style={{ textAlign: 'right' }}>Qty</th>
              <th style={{ textAlign: 'right' }}>Avg Cost</th>
              <th style={{ textAlign: 'right' }}>Price</th>
              <th style={{ textAlign: 'right' }}>Market Value</th>
              <th style={{ textAlign: 'right' }}>Unrealized P/L</th>
            </tr>
          </thead>
          <tbody>
            {positions.map(p => (
              <tr key={p.symbol}>
                <td className="nexus-sym-cell">{p.symbol}</td>
                <td style={{ textAlign: 'right' }}>{p.quantity}</td>
                <td style={{ textAlign: 'right' }}>{fmtMoney(p.avgCost)}</td>
                <td style={{ textAlign: 'right' }}>{fmtMoney(p.currentPrice)}</td>
                <td style={{ textAlign: 'right' }}>{fmtMoney(p.marketValue)}</td>
                <td style={{ textAlign: 'right' }}>
                  <span className={`nexus-pl ${p.unrealizedPl >= 0 ? 'up' : 'down'}`}>
                    {fmtMoney(p.unrealizedPl)} ({fmtPct(p.unrealizedPlPct)})
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Alerts ───────────────────────────────────────────────────────────────
// Sentinel emits these; the status bar only ever showed a COUNT, so there was
// no way to actually read them. This lists them newest-first.

interface AlertRow { id: number; symbol: string; ts: string; kind: string; message: string }

function AlertsPage({ onSelect }: { onSelect: (symbol: string) => void }) {
  const [days, setDays] = useState(7);
  const since = new Date(Date.now() - days * 86_400_000).toISOString();

  const alertsQ = useQuery<AlertRow[]>({
    queryKey: ['nexus-alerts', days],
    queryFn: () => get(`${BASE}/alerts?since=${encodeURIComponent(since)}&limit=200`),
    refetchInterval: 60_000,
  });

  if (alertsQ.isError) {
    const msg = friendlyError(alertsQ.error);
    if (msg.startsWith("Can't reach")) return <ApiUnreachable name="Nexus" port={5700} mc={MC} onRetry={() => alertsQ.refetch()} />;
    return (
      <div className="module-empty" style={style}>
        <div className="module-empty-icon">⚠️</div>
        <h2>Sentinel isn't ready</h2>
        <p>{msg}</p>
        <button className="module-retry-btn" onClick={() => alertsQ.refetch()}>Retry</button>
      </div>
    );
  }

  if (alertsQ.isPending) return <div className="module-empty" style={style}><p>Loading alerts…</p></div>;

  const alerts = alertsQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Sentinel Alerts</h3>
        <select value={days} onChange={e => setDays(Number(e.target.value))}>
          <option value={1}>Last 24 hours</option>
          <option value={7}>Last 7 days</option>
          <option value={30}>Last 30 days</option>
        </select>
      </div>

      {alerts.length === 0 ? (
        <div className="module-empty" style={style}>
          <div className="module-empty-icon">🔔</div>
          <h2>No alerts</h2>
          <p>Sentinel hasn't raised anything in this window.</p>
        </div>
      ) : (
        <div className="nexus-board-wrap">
          <table className="nexus-board">
            <thead>
              <tr>
                <th>When</th>
                <th>Symbol</th>
                <th>Kind</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {alerts.map(a => (
                <tr key={a.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>{formatInTz(a.ts)}</td>
                  <td className="nexus-sym-cell" style={{ cursor: 'pointer' }} onClick={() => onSelect(a.symbol)}>{a.symbol}</td>
                  <td>{a.kind}</td>
                  <td>{a.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Root ─────────────────────────────────────────────────────────────────

function NexusInner() {
  const [page, setPage] = useState<Page>('watchlist');
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);

  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      <StatusBar />
      {page === 'watchlist' && <WatchlistPage onSelect={setSelectedSymbol} />}
      {page === 'portfolio' && <PortfolioPage />}
      {page === 'alerts' && <AlertsPage onSelect={setSelectedSymbol} />}
      {selectedSymbol && <NexusDetailPanel symbol={selectedSymbol} onClose={() => setSelectedSymbol(null)} />}
    </div>
  );
}

export default function NexusModule() {
  return (
    <QueryClientProvider client={qc}>
      <NexusInner />
    </QueryClientProvider>
  );
}

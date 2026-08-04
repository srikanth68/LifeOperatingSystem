import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  BASE, get, friendlyError, fmtMoney, fmtCap, fmtPct, relTime,
  FreshnessBadge,
} from '../pages/NexusModule';
import type { TickerDetail, Report, Signal, TradePlan } from '../pages/NexusModule';

interface Props {
  symbol: string;
  onClose: () => void;
}

function Gauge({ score }: { score: number }) {
  const pct = Math.min(Math.abs(score), 1) * 50;
  const isPos = score >= 0;
  return (
    <div className="nexus-gauge">
      <span
        className={`nexus-gauge-fill ${isPos ? 'pos' : 'neg'}`}
        style={{ left: isPos ? '50%' : `${50 - pct}%`, width: `${pct}%` }}
      />
    </div>
  );
}

function SignalRow({ s }: { s: Signal }) {
  return (
    <div className="nexus-signal">
      <div className="nexus-signal-top">
        <span>{s.name}</span>
        <span>{s.score >= 0 ? '+' : ''}{s.score.toFixed(2)}</span>
      </div>
      <div className="nexus-signal-det">{s.detail}</div>
      <Gauge score={s.score} />
    </div>
  );
}

function ReportCard({ r }: { r: Report }) {
  return (
    <div className="nexus-report-card">
      <div className="nexus-report-head">
        <h5>{r.analyst}</h5>
        <span className={`nexus-pill ${r.stance}`}>{r.stance}</span>
      </div>
      {r.summary && <div className="nexus-report-summary">{r.summary}</div>}
      {r.signals.map((s, i) => <SignalRow key={i} s={s} />)}
    </div>
  );
}

function FitCard({ label, score, notes }: { label: string; score: number; notes: string[] }) {
  return (
    <div className="nexus-fit">
      <div className="nexus-fit-label"><span>{label}</span><span>{score}/100</span></div>
      <div className="nexus-fit-bar"><span className="nexus-fit-fill" style={{ width: `${score}%` }} /></div>
      {notes.length > 0 && <ul>{notes.map((n, i) => <li key={i}>{n}</li>)}</ul>}
    </div>
  );
}

function RangeMeter({ price, stop, target }: { price: number; stop: number; target: number }) {
  const lo = Math.min(price, stop, target) * 0.98;
  const hi = Math.max(price, stop, target) * 1.02;
  const pct = (v: number) => ((v - lo) / (hi - lo)) * 100;
  return (
    <div className="nexus-meter">
      <div className="nexus-meter-track">
        <span className="nexus-meter-pin stop" style={{ left: `${pct(stop)}%` }}>
          <span className="nexus-meter-flag" style={{ color: 'var(--debt-l)' }}>stop {stop.toFixed(2)}</span>
        </span>
        <span className="nexus-meter-pin" style={{ left: `${pct(price)}%` }}>
          <span className="nexus-meter-flag">now {price.toFixed(2)}</span>
        </span>
        <span className="nexus-meter-pin target" style={{ left: `${pct(target)}%` }}>
          <span className="nexus-meter-flag" style={{ color: 'var(--cash-l)' }}>target {target.toFixed(2)}</span>
        </span>
      </div>
    </div>
  );
}

function TradePlanBlock({ plan }: { plan: TradePlan }) {
  return (
    <div className="nexus-style-box">
      <div className="nexus-style-head">
        <span className="nexus-style-rec">{plan.recommendedStyle}</span>
      </div>
      <div className="nexus-fits">
        <FitCard label="Day-trade fit" score={plan.dayTradeScore} notes={plan.dayNotes} />
        <FitCard label="Swing-trade fit" score={plan.swingTradeScore} notes={plan.swingNotes} />
      </div>

      {(plan.swingEntryLow != null || plan.swingStop != null) && (
        <div className="nexus-day-block">
          <div className="nexus-section-label" style={{ margin: '0 0 0.5rem' }}>Swing entry zone</div>
          <div className="nexus-day-grid">
            {plan.swingSide && <div className="nexus-day-stat">Side<b>{plan.swingSide}</b></div>}
            {plan.swingEntryLow != null && plan.swingEntryHigh != null && (
              <div className="nexus-day-stat">Entry zone<b>{fmtMoney(plan.swingEntryLow)} – {fmtMoney(plan.swingEntryHigh)}</b></div>
            )}
            {plan.swingStop != null && <div className="nexus-day-stat">Stop<b>{fmtMoney(plan.swingStop)}</b></div>}
            {plan.swingTarget != null && <div className="nexus-day-stat">Target<b>{fmtMoney(plan.swingTarget)}</b></div>}
          </div>
        </div>
      )}

      {plan.dayTrade ? (
        <div className="nexus-day-block">
          <div className="nexus-section-label" style={{ margin: '0 0 0.5rem' }}>Intraday tape · {plan.dayTrade.interval}</div>
          <div className="nexus-day-grid">
            <div className="nexus-day-stat">VWAP<b>{fmtMoney(plan.dayTrade.vwap)}</b></div>
            <div className="nexus-day-stat">vs VWAP<b>{fmtPct(plan.dayTrade.priceVsVwapPct)}</b></div>
            <div className="nexus-day-stat">OR range<b>{fmtMoney(plan.dayTrade.orLow, 0)}–{fmtMoney(plan.dayTrade.orHigh, 0)}</b></div>
            <div className="nexus-day-stat">OR state<b>{plan.dayTrade.orState}</b></div>
            <div className="nexus-day-stat">RVOL<b>{plan.dayTrade.rvol.toFixed(2)}x</b></div>
            <div className="nexus-day-stat">Bias<b>{plan.dayTrade.bias}</b></div>
          </div>
          {plan.dayTrade.notes.map((n, i) => <div key={i} className="nexus-signal-det">{n}</div>)}
        </div>
      ) : (
        <div className="nexus-day-block">
          <div className="nexus-signal-det" style={{ color: 'var(--text3)', fontStyle: 'italic' }}>
            Not enough intraday bars yet for a day-trade read.
          </div>
        </div>
      )}
    </div>
  );
}

export function NexusDetailPanel({ symbol, onClose }: Props) {
  const q = useQuery<TickerDetail>({
    queryKey: ['nexus-detail', symbol],
    queryFn: () => get<TickerDetail>(`${BASE}/tickers/${symbol}`),
  });

  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  const d = q.data;

  return (
    <div className="nexus-overlay" onClick={onClose}>
      <div className="nexus-panel" onClick={e => e.stopPropagation()}>
        <div className="nexus-panel-actions">
          {/* Native print-to-PDF: keeps the panel's styling, no extra dependency.
              Print CSS (nexus.css) hides everything except this panel. */}
          <button className="nexus-panel-pdf" onClick={() => window.print()} title="Save this analysis as a PDF">
            ⤓ PDF
          </button>
          <button className="nexus-panel-close" onClick={onClose} aria-label="Close">×</button>
        </div>

        {q.isPending && <p style={{ color: 'var(--text2)' }}>Loading {symbol}…</p>}

        {q.isError && (
          <div className="module-empty" style={{ '--mc': 'var(--nexus)' } as React.CSSProperties}>
            <div className="module-empty-icon">⚠️</div>
            <h2>Can't load {symbol}</h2>
            <p>{friendlyError(q.error)}</p>
          </div>
        )}

        {d && (
          <>
            {/* Hero band — everything needed to size up the ticker at a glance, laid
                out across the panel's full width rather than stacked. */}
            <header className="nexus-hero">
              <div className="nexus-hero-id">
                <div className="nexus-panel-sym">{d.symbol}</div>
                {d.meta.company && <div className="nexus-panel-co">{d.meta.company}</div>}
                <FreshnessBadge freshness={d.meta.freshness ?? 'RECORDED'} />
              </div>

              <div className="nexus-hero-price">
                <span className="nexus-price-big">{fmtMoney(d.price)}</span>
                <span className={`nexus-hero-chg ${(d.meta.changePct ?? 0) >= 0 ? 'up' : 'down'}`}>
                  {fmtPct(d.meta.changePct)}
                </span>
              </div>

              <div className="nexus-hero-stats">
                <div className="nexus-stat"><div className="k">P/E</div><div className="v">{d.meta.pe != null ? d.meta.pe.toFixed(1) : '—'}</div></div>
                <div className="nexus-stat"><div className="k">Mkt Cap</div><div className="v">{fmtCap(d.meta.cap)}</div></div>
                <div className="nexus-stat"><div className="k">As Of</div><div className="v">{relTime(d.asOf)}</div></div>
              </div>

              <div className="nexus-verdict-tag" style={{ color: verdictColor(d.action) }}>
                <span className="act">{d.action}</span>
                <span className="conv">conviction {d.conviction}/10</span>
                <span className="score">{d.composite >= 0 ? '+' : ''}{d.composite.toFixed(2)}</span>
              </div>
            </header>

            <div className="nexus-hero-meter">
              <RangeMeter price={d.price} stop={d.risk.stop} target={d.risk.target} />
            </div>

            {/* Two columns: the reasoning reads down the left, while the actionable
                ticket stays pinned on the right instead of being buried below it. */}
            <div className="nexus-cols">
              <section className="nexus-col-main">
                <div className="nexus-section-label">Committee Thesis</div>
                <div className="nexus-thesis">{d.thesis}</div>

                <div className="nexus-section-label">Debate</div>
                <div className="nexus-debate">
                  <div className="nexus-debate-side bull">
                    <h4>Bull</h4>
                    <div className="nexus-debate-strength">strength {(d.debate.bullStrength * 100).toFixed(0)}%</div>
                    <ul>{d.debate.bullPoints.map((p, i) => <li key={i}>{p}</li>)}</ul>
                  </div>
                  <div className="nexus-debate-side bear">
                    <h4>Bear</h4>
                    <div className="nexus-debate-strength">strength {(d.debate.bearStrength * 100).toFixed(0)}%</div>
                    <ul>{d.debate.bearPoints.map((p, i) => <li key={i}>{p}</li>)}</ul>
                  </div>
                  <div className="nexus-synth"><b>Ruling —</b> {d.debate.synthesis}</div>
                </div>

                {d.reports.length > 0 && (
                  <>
                    <div className="nexus-section-label">Analyst Desks</div>
                    <div className="nexus-report-grid">
                      {d.reports.map((r, i) => <ReportCard key={i} r={r} />)}
                    </div>
                  </>
                )}
              </section>

              <aside className="nexus-col-side">
                <div className="nexus-section-label">Risk Sign-off</div>
                <div className="nexus-ticket">
                  <div className="nexus-ticket-head">
                    <span className="nexus-ticket-title">Trade ticket</span>
                    <span className={`nexus-ticket-status ${d.risk.approved ? 'ok' : 'no'}`}>{d.risk.approved ? 'Approved' : 'Withheld'}</span>
                  </div>
                  <div className="nexus-ticket-fields">
                    <div className="nexus-ticket-field key entry"><div className="k">Entry</div><div className="v">{fmtMoney(d.risk.entry)}</div></div>
                    <div className="nexus-ticket-field key stop"><div className="k">Stop</div><div className="v">{fmtMoney(d.risk.stop)}</div></div>
                    <div className="nexus-ticket-field key target"><div className="k">Target</div><div className="v">{fmtMoney(d.risk.target)}</div></div>
                    <div className="nexus-ticket-field"><div className="k">Size</div><div className="v">{d.risk.positionPct.toFixed(2)}%</div></div>
                    <div className="nexus-ticket-field"><div className="k">R : R</div><div className="v">{d.risk.rr.toFixed(2)}</div></div>
                    <div className="nexus-ticket-field"><div className="k">Risk @ stop</div><div className="v">{d.risk.maxRiskPct.toFixed(2)}%</div></div>
                  </div>
                  {(d.risk.checks.length > 0 || d.risk.flags.length > 0) && (
                    <div className="nexus-ticket-notes">
                      {d.risk.checks.map((c, i) => <div key={`c${i}`}>· {c}</div>)}
                      {d.risk.flags.map((f, i) => <div key={`f${i}`} className="flag">! {f}</div>)}
                    </div>
                  )}
                </div>

                {(d.meta.setups?.length || d.meta.patterns?.length) ? (
                  <>
                    <div className="nexus-section-label">Setups & Patterns</div>
                    <div className="nexus-badge-row">
                      {d.meta.setups?.map((s, i) => <span key={`s${i}`} className="nexus-mini-badge">{s}</span>)}
                      {d.meta.patterns?.map((p, i) => <span key={`p${i}`} className="nexus-mini-badge">{p}</span>)}
                    </div>
                  </>
                ) : null}
              </aside>
            </div>

            {d.tradePlan && (
              <>
                <div className="nexus-section-label">Trade Style & Entry</div>
                <TradePlanBlock plan={d.tradePlan} />
              </>
            )}

            <div className="nexus-source-line">
              {d.meta.freshness ?? 'RECORDED'} · {d.source} · as of {new Date(d.asOf).toLocaleString()}
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function verdictColor(action: string) {
  const a = action.toLowerCase();
  if (a === 'buy' || a === 'accumulate') return 'var(--cash-l)';
  if (a === 'trim' || a === 'avoid') return 'var(--debt-l)';
  return 'var(--gold-l)';
}

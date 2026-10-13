import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { vitaraHeaders } from '../../services/profile';
import { moduleApi } from '../../services/apiHost';
import '../../styles/insight.css';

// What the daily data looked like before one blood draw, beside the previous draw
// (Vitara ADR 0020). Fetched only when opened: most people look at a draw's results and
// never this, and it reads two years of daily data to answer.
//
// Co-occurrence only. The server writes the sentences and a test keeps causal words out
// of them; nothing here adds an interpretation of its own.
const API = moduleApi(5110);

interface WindowMetric {
  metric: string;
  label: string;
  unit: string | null;
  median: number | null;
  meanZ: number | null;
  coverage: number;
  summarised: boolean;
  refusal: string | null;
}

interface LabWindow {
  days: number;
  from: string;
  to: string;
  analytes: string[];
  analyteLabels?: string[];
  metrics: WindowMetric[];
  sleepDebtAtDraw: number | null;
  meanLoadRatio: number | null;
  findings: string[];
  interventions: string[];
}

interface DrawContext {
  drawnOn: string;
  windows: LabWindow[];
  previous: { drawnOn: string; windows: LabWindow[] } | null;
  sentences: string[];
  note: string;
}

function fmt(v: number | null, unit: string | null): string {
  if (v == null) return '—';
  if (unit === 'min') return `${Math.floor(v / 60)} h ${String(Math.round(v % 60)).padStart(2, '0')}`;
  const n = Math.abs(v) >= 1000 ? Math.round(v).toLocaleString() : Math.abs(v) >= 10 ? v.toFixed(0) : v.toFixed(1);
  return unit && unit !== 'steps' ? `${n} ${unit}` : n;
}

export function LabContextPanel({ panelId, phone = false }: { panelId: string; phone?: boolean }) {
  const [open, setOpen] = useState(false);
  const { data, error, isLoading } = useQuery({
    queryKey: ['lab-context', panelId],
    enabled: open,
    queryFn: async () => {
      const r = await fetch(`${API}/api/health/labs/${panelId}/context`, { headers: vitaraHeaders() });
      if (!r.ok) throw new Error(String(r.status));
      return r.json() as Promise<DrawContext>;
    },
  });

  return (
    <details className={phone ? 'vm-more-box' : 'ix-labctx-details'} onToggle={e => setOpen((e.target as HTMLDetailsElement).open)}>
      <summary className={phone ? undefined : 'hx-eyebrow'} style={phone ? undefined : { cursor: 'pointer', marginTop: '1rem' }}>
        What your days looked like before this draw
      </summary>

      {isLoading && <p className="insight-muted">Reading your daily data…</p>}
      {error && <p className="insight-muted">Could not reach the analysis service.</p>}

      {data && (
        <div className="ix-labctx">
          {data.sentences.length > 0 && (
            <ul>{data.sentences.map(s => <li key={s}>{s}</li>)}</ul>
          )}

          {data.windows.map(w => {
            const before = data.previous?.windows.find(p => p.days === w.days);
            return (
              <div key={w.days} className="ix-labctx-window">
                <p style={{ margin: '0 0 0.4rem', fontWeight: 600 }}>
                  {w.days} days before ({(w.analyteLabels ?? w.analytes).join(', ')})
                </p>
                <table>
                  <thead>
                    <tr><th>Daily measure</th><th>This draw</th>{before && <th>Previous draw</th>}</tr>
                  </thead>
                  <tbody>
                    {w.metrics.map(m => {
                      const then = before?.metrics.find(p => p.metric === m.metric);
                      return (
                        <tr key={m.metric}>
                          <td>{m.label}</td>
                          {m.summarised
                            ? <td>{fmt(m.median, m.unit)}</td>
                            : <td className="refused">{m.refusal}</td>}
                          {before && (then?.summarised
                            ? <td>{fmt(then.median, then.unit)}</td>
                            : <td className="refused">{then?.refusal ?? '—'}</td>)}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
                {(w.interventions.length > 0 || w.findings.length > 0) && (
                  <p className="insight-muted" style={{ marginTop: '0.4rem' }}>
                    {w.interventions.length > 0 && <>Running then: {w.interventions.join('; ')}. </>}
                    {w.findings.length > 0 && <>Also going on: {w.findings.join(' ')}</>}
                  </p>
                )}
              </div>
            );
          })}

          <p className="insight-muted" style={{ fontSize: '0.78rem' }}>{data.note}</p>
        </div>
      )}
    </details>
  );
}

import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { Panel, Chip, Empty, Info, Row } from './health/HealthKit';

const API = moduleApi(5100);

// Blood work, entered as a draw rather than as readings.
//
// Twelve analytes off one needle share a date, a lab and a fasting state. A form that
// asks for those twelve times is a form nobody fills in twice, which is why this is a
// panel with rows rather than the single-measurement form on the Record tab.

interface Analyte {
  key: string;
  label: string;
  unit: string;
  group: string;
  what: string;
  decimals: number;
  range: { low: number | null; high: number | null; band: string; notes: string | null } | null;
  grade: 'A' | 'B' | 'C' | 'D' | null;
  gradeLabel: string | null;
  caveat: string | null;
}

interface LabResult {
  metric: string;
  label: string;
  value: number;
  unit: string;
  note: string | null;
  standing: 'below' | 'within' | 'above' | 'unknown';
  standingText: string;
  range: { low: number | null; high: number | null; band: string; notes: string | null; labName: string | null } | null;
  previous: number | null;
  change: number | null;
}

// Worked out from the draw rather than measured in it.
interface Derived {
  metric: string;
  label: string;
  value: number;
  unit: string;
  method: string;
  caveat: string;
  grade: 'A' | 'B' | 'C' | 'D';
  gradeLabel: string;
  standing: 'below' | 'within' | 'above' | 'unknown';
  standingText: string;
}

// And what could not be, with the reason — which for a panel missing fasting insulin
// is the most useful line on the page.
interface Gap {
  metric: string;
  label: string;
  reason: string;
  missing: { key: string; label: string }[];
}

interface LabPanelRow {
  id: string;
  drawnOn: string;
  daysAgo: number;
  labName: string | null;
  notes: string | null;
  fasting: boolean | null;
  results: LabResult[];
  derived: Derived[];
  gaps: Gap[];
}

const get = async <T,>(url: string): Promise<T> => {
  const r = await fetch(url, { headers: authHeaders() });
  if (!r.ok) throw new Error(`${r.status}`);
  return r.json();
};

export function VitaraLabs() {
  const qc = useQueryClient();
  const { data: panels } = useQuery<LabPanelRow[]>({ queryKey: ['labs'], queryFn: () => get(`${API}/api/labs`) });
  const { data: analytes } = useQuery<Analyte[]>({ queryKey: ['analytes'], queryFn: () => get(`${API}/api/labs/analytes`) });

  const [drawnOn, setDrawnOn] = useState(new Date().toISOString().slice(0, 10));
  const [labName, setLabName] = useState('');

  // Three states, not a checkbox. "Nobody recorded it" is a real answer and the one
  // most draws have — and an unticked box claiming the draw was not fasting would
  // change how the glucose on it reads.
  const [fasting, setFasting] = useState<'' | 'yes' | 'no'>('');
  const [notes, setNotes] = useState('');
  const [values, setValues] = useState<Record<string, string>>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [savedNote, setSavedNote] = useState<string | null>(null);

  const entered = useMemo(
    () => Object.entries(values).filter(([, v]) => v.trim() !== '' && !Number.isNaN(Number(v))),
    [values],
  );

  const save = useMutation({
    mutationFn: async () => {
      const res = await fetch(`${API}/api/labs`, {
        method: 'POST',
        headers: { ...authHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
          drawnOn,
          labName: labName.trim() || null,
          notes: notes.trim() || null,
          fasting: fasting === '' ? null : fasting === 'yes',
          results: entered.map(([metric, v]) => ({ metric, value: Number(v) })),
        }),
      });
      const json = await res.json();
      if (!res.ok) throw new Error(json?.error ?? `${res.status}`);
      return json as { stored: number; note: string | null };
    },
    onSuccess: json => {
      setValues({});
      setNotes('');
      setSaveError(null);
      setSavedNote(json.note ?? `Saved ${json.stored} result${json.stored === 1 ? '' : 's'}.`);
      qc.invalidateQueries({ queryKey: ['labs'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
    onError: (e: Error) => { setSaveError(e.message); setSavedNote(null); },
  });

  const remove = useMutation({
    mutationFn: (id: string) => fetch(`${API}/api/labs/${id}`, { method: 'DELETE', headers: authHeaders() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['labs'] }),
  });

  const labs = (analytes ?? []).filter(a => a.group === 'Labs');
  const vitals = (analytes ?? []).filter(a => a.group === 'Vitals');

  return (
    <div className="hx-col">
      <Panel
        title="Enter a draw"
        icon="🧪"
        note={entered.length > 0 ? `${entered.length} result${entered.length === 1 ? '' : 's'} ready` : 'fill in what you have'}
        info="One blood draw, however many analytes it covered. Leave anything the panel did not measure blank — a blank is not a zero, and nothing is stored for it."
      >
        <div className="hx-form" style={{ marginBottom: '0.9rem' }}>
          <label className="hx-check" style={{ gap: '0.5rem' }}>
            Drawn
            <input type="date" value={drawnOn} onChange={e => setDrawnOn(e.target.value)} />
          </label>
          <input placeholder="Lab (optional)" value={labName} onChange={e => setLabName(e.target.value)} style={{ width: '10rem' }} />
          <label className="hx-check" style={{ gap: '0.5rem' }}>
            Fasting
            <select value={fasting} onChange={e => setFasting(e.target.value as '' | 'yes' | 'no')}>
              <option value="">not recorded</option>
              <option value="yes">yes</option>
              <option value="no">no</option>
            </select>
            <Info label="Why fasting matters">
              Glucose and triglycerides mean different things after food, and HOMA-IR — the
              insulin-resistance estimate — is only defined on a fasting sample. Left unrecorded it
              is not assumed either way: the value is skipped rather than guessed.
            </Info>
          </label>
          <input placeholder="Note — time of day, anything unusual…" value={notes} onChange={e => setNotes(e.target.value)} style={{ flex: 1, minWidth: '11rem' }} />
        </div>

        <AnalyteGrid title="Blood work" analytes={labs} values={values} onChange={setValues} />
        {vitals.length > 0 && (
          <AnalyteGrid title="Also on a report sometimes" analytes={vitals} values={values} onChange={setValues} />
        )}

        <div className="hx-form" style={{ marginTop: '1rem' }}>
          <button className="hx-btn" disabled={entered.length === 0 || save.isPending} onClick={() => save.mutate()}>
            {save.isPending ? 'Saving…' : `Save ${entered.length || ''} result${entered.length === 1 ? '' : 's'}`.trim()}
          </button>
          {saveError && <span className="hx-error">{saveError}</span>}
          {savedNote && <span className="hx-ok">{savedNote}</span>}
        </div>
      </Panel>

      {panels && panels.length > 0 ? (
        panels.map(p => (
          <Panel
            key={p.id}
            title={new Date(p.drawnOn + 'T12:00:00').toLocaleDateString('en-US', { day: 'numeric', month: 'long', year: 'numeric' })}
            icon="🩸"
            note={[
              p.labName,
              p.fasting === true ? 'fasting' : p.fasting === false ? 'not fasting' : 'fasting state not recorded',
              p.notes,
              p.daysAgo === 0 ? 'today' : `${p.daysAgo} days ago`,
            ].filter(Boolean).join(' · ')}
            right={
              <button className="hx-icon-btn" onClick={() => remove.mutate(p.id)} aria-label="Delete this draw">×</button>
            }
          >
            <div className="hx-rows">
              {p.results.map(r => (
                <Row
                  key={r.metric}
                  tone={r.standing === 'within' ? 'good' : r.standing === 'unknown' ? undefined : 'warn'}
                  title={
                    <>
                      {r.label}{' '}
                      <b style={{ fontVariantNumeric: 'tabular-nums' }}>{r.value}</b>
                      <span style={{ fontWeight: 400, color: 'var(--text3)' }}> {r.unit}</span>
                    </>
                  }
                  note={
                    <>
                      {r.standingText}
                      {r.change != null && (
                        <> · {r.change > 0 ? '+' : ''}{r.change} since the previous draw</>
                      )}
                      {r.range?.notes && <Info label={`About ${r.label}`}>{r.range.notes}</Info>}
                    </>
                  }
                  right={
                    r.standing === 'within' ? <Chip tone="good">in range</Chip>
                    : r.standing === 'above' ? <Chip tone="warn">above</Chip>
                    : r.standing === 'below' ? <Chip tone="warn">below</Chip>
                    : <Chip>no range</Chip>
                  }
                />
              ))}
            </div>

            {p.derived.length > 0 && (
              <>
                <p className="hx-eyebrow" style={{ marginTop: '1rem' }}>Worked out from this draw</p>
                <div className="hx-rows">
                  {p.derived.map(d => (
                    <Row
                      key={d.metric}
                      tone={d.standing === 'within' ? 'good' : d.standing === 'unknown' ? undefined : 'warn'}
                      title={
                        <>
                          {d.label}{' '}
                          <b style={{ fontVariantNumeric: 'tabular-nums' }}>{d.value}</b>
                          <span style={{ fontWeight: 400, color: 'var(--text3)' }}> {d.unit}</span>
                        </>
                      }
                      note={
                        <>
                          {d.standing === 'unknown' ? d.method : d.standingText}
                          <Info label={`About ${d.label}`}>
                            {d.method} {d.caveat}
                          </Info>
                        </>
                      }
                      right={<Chip tone={d.grade === 'A' ? 'good' : d.grade === 'D' ? 'warn' : undefined}>{d.gradeLabel}</Chip>}
                    />
                  ))}
                </div>
              </>
            )}

            {p.gaps.length > 0 && (
              <>
                {/* The absences, named. "No HOMA-IR" looks like a missing feature;
                    "this panel has glucose but no insulin" is a sentence to take to
                    an appointment. */}
                <p className="hx-eyebrow" style={{ marginTop: '1rem' }}>What this draw could not answer</p>
                <div className="hx-rows">
                  {p.gaps.map(g => (
                    <Row
                      key={g.metric}
                      title={g.label}
                      note={g.reason}
                      right={g.missing.length > 0 ? <Chip>needs {g.missing.map(m => m.label).join(', ')}</Chip> : undefined}
                    />
                  ))}
                </div>
              </>
            )}
          </Panel>
        ))
      ) : (
        <Panel title="Your draws" icon="🩸">
          <Empty title="No blood work entered yet.">
            Labs are the one place this compares you against a published range rather than against your own
            history — two readings a year is far too few to learn a personal normal from.
          </Empty>
        </Panel>
      )}
    </div>
  );
}

// A grid of inputs, each showing the range it will be read against. Showing the band
// beside the box is the difference between typing a number and understanding it.
function AnalyteGrid({ title, analytes, values, onChange }: {
  title: string;
  analytes: Analyte[];
  values: Record<string, string>;
  onChange: (next: Record<string, string>) => void;
}) {
  if (analytes.length === 0) return null;

  return (
    <>
      <p className="hx-eyebrow" style={{ marginTop: '0.6rem' }}>{title}</p>
      <div className="hx-grid hx-grid-3">
        {analytes.map(a => (
          <label key={a.key} className="hx-analyte">
            <span className="hx-analyte-name">
              {a.label}
              <Info label={`About ${a.label}`}>
                {a.what}
                {a.range?.notes ? <> {a.range.notes}</> : null}
                {a.caveat ? <> <b>Evidence: {a.gradeLabel}.</b> {a.caveat}</> : null}
              </Info>
            </span>
            <span className="hx-analyte-input">
              <input
                type="number"
                step="any"
                inputMode="decimal"
                placeholder="—"
                value={values[a.key] ?? ''}
                onChange={e => onChange({ ...values, [a.key]: e.target.value })}
              />
              <em>{a.unit}</em>
            </span>
            <span className="hx-analyte-range">{a.range ? a.range.band : 'no reference range'}</span>
          </label>
        ))}
      </div>
    </>
  );
}

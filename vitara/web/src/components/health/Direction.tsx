import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Chip, Info } from './HealthKit';
import { vitaraHeaders } from '../../services/profile';
import { moduleApi } from '../../services/apiHost';

// The slower question: is this person getting better or worse (Vitara ADRs 0017-0024).
//
// Four parts, in reading order: each area of health with what moved and what is not
// measured (never a score), any normal that is being HELD because a harmful step has no
// explanation, "what if I..." with every answer labelled by how it was reached, and, per
// moving measure, where it would be if the recent pattern continued.
const API = moduleApi(5110);
const VITARA = moduleApi(5100);

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url, { headers: vitaraHeaders() });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

// ── Types ────────────────────────────────────────────────────────────────────

interface DomainMetric {
  metric: string;
  label: string;
  unit: string | null;
  latest: number | null;
  daysAgo: number | null;
  baseline: string;
  trajectory: string;
  trajectoryReason: string;
  grade: string | null;
}

interface Domain {
  key: string;
  label: string;
  question: string;
  status: string;
  counts: Record<string, number>;
  metrics: DomainMetric[];
  findings: { key: string; severity: string; summary: string }[];
  patterns: string[];
  gaps: { unmeasured: string[]; stale: string[]; missingKeyTests: string[] };
  readWith: string[];
}

interface DomainScan { today: string; note: string; domains: Domain[] }

interface Hold {
  metric: string;
  label: string;
  unit: string | null;
  signature: string;
  changePoint: string;
  direction: string;
  before: number;
  after: number;
  openedOn: string;
  open: boolean;
  heldForDays: number;
  releasedOn: string | null;
  releaseReason: string | null;
  explanation: string | null;
}

interface ScenarioAnswer {
  mode: string;
  question: string;
  answer: string;
  from: number | null;
  to: number | null;
  unit: string | null;
  supported: boolean;
}

interface ScenarioResult {
  change: string;
  title: string;
  mode: string;
  answers: ScenarioAnswer[];
  experiment: { suggestedTargets: string[]; howItIsJudged: string; daysUntilFirstVerdict: number; doctorNote: string | null } | null;
  caveat: string;
}

interface ProjectionResult {
  label: string;
  projected: boolean;
  horizonDays: number;
  currentLevel: number | null;
  projectedLow: number | null;
  projectedHigh: number | null;
  basis: string;
  grade: string;
  caveat: string;
  refusal: string | null;
  horizonNote: string | null;
}

// ── Words ────────────────────────────────────────────────────────────────────

// Plain words for each state. Worsening is amber, not red: nothing in this app is red for
// moving away from your usual -- coral is kept for lab results outside a printed range.
const STATE: Record<string, { word: string; tone: 'neutral' | 'good' | 'warn' }> = {
  improving: { word: 'Improving', tone: 'good' },
  worsening: { word: 'Worsening', tone: 'warn' },
  rising: { word: 'Rising', tone: 'neutral' },
  falling: { word: 'Falling', tone: 'neutral' },
  stable: { word: 'Steady', tone: 'neutral' },
  uncertain: { word: 'Unclear', tone: 'neutral' },
  insufficient_data: { word: 'Not enough data', tone: 'neutral' },
};

const MODE: Record<string, { word: string; note: string }> = {
  arithmetic: { word: 'Calculated', note: 'Vitara’s own rules applied to your numbers. Exact about those rules.' },
  learned: { word: 'From your history', note: 'What your own days did together before. An association, next morning only.' },
  experiment: { word: 'An experiment', note: 'Your data has no answer to this yet. Here is how to get one.' },
};

const MOVED = ['improving', 'worsening', 'rising', 'falling'];

function fmt(v: number | null, unit?: string | null): string {
  if (v == null) return '—';
  const n = Math.abs(v) >= 100 ? v.toFixed(0) : Math.abs(v) >= 10 ? v.toFixed(1) : v.toFixed(2);
  return unit && unit !== 'steps' && unit !== 'ratio' ? `${n} ${unit}` : n;
}

// ── Domains ──────────────────────────────────────────────────────────────────

function DomainsSection() {
  const { data } = useQuery({ queryKey: ['domains'], queryFn: () => get<DomainScan>(`${API}/api/health/domains`) });
  const [open, setOpen] = useState<string | null>(null);
  if (!data) return <div className="insight-card is-loading" />;

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        How each area is doing
        <Info label="Why there is no score">
          Each area says what moved, which way, and what is not measured. There is no number for an area
          and no overall score: a score hides which measure moved and weighs a blood test and a ring
          estimate as if they were the same kind of evidence.
        </Info>
      </h2>
      <p className="insight-muted insight-lede">{data.note}</p>

      <div className="ix-dir-grid">
        {data.domains.map(d => (
          <div key={d.key} className={`insight-card ix-dir-domain ${open === d.key ? 'is-open' : ''}`}>
            <button className="ix-dir-head" onClick={() => setOpen(open === d.key ? null : d.key)} aria-expanded={open === d.key}>
              <span className="insight-eyebrow">{d.label}</span>
              <span className="ix-dir-status">{d.status}</span>
              <span className="ix-dir-chips">
                {d.counts.improving > 0 && <Chip tone="good">{d.counts.improving} improving</Chip>}
                {d.counts.worsening > 0 && <Chip tone="warn">{d.counts.worsening} worsening</Chip>}
                {d.findings.length > 0 && <Chip>{d.findings.length} finding{d.findings.length === 1 ? '' : 's'}</Chip>}
                {d.gaps.missingKeyTests.length > 0 && <Chip>{d.gaps.missingKeyTests.length} key test missing</Chip>}
              </span>
            </button>

            {open === d.key && (
              <div className="ix-dir-body">
                <p className="insight-muted">{d.question}</p>

                {d.gaps.missingKeyTests.length > 0 && (
                  <div className="ix-dir-gaps">
                    <p className="insight-eyebrow">What would make this answerable</p>
                    <ul>{d.gaps.missingKeyTests.map(g => <li key={g}>{g}</li>)}</ul>
                  </div>
                )}

                <ul className="ix-dir-metrics">
                  {d.metrics.map(m => (
                    <MetricRow key={m.metric} m={m} />
                  ))}
                </ul>

                {d.findings.length > 0 && (
                  <>
                    <p className="insight-eyebrow">Active findings</p>
                    <ul className="insight-asks">{d.findings.map(f => <li key={f.key}>{f.summary}</li>)}</ul>
                  </>
                )}
                {d.patterns.length > 0 && (
                  <p className="insight-muted">Moving together here: {d.patterns.join('; ')}.</p>
                )}
                {d.gaps.stale.length > 0 && (
                  <p className="insight-muted">Not measured lately: {d.gaps.stale.join(', ')}.</p>
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </section>
  );
}

function MetricRow({ m }: { m: DomainMetric }) {
  const [project, setProject] = useState(false);
  const s = STATE[m.trajectory] ?? STATE.insufficient_data;

  return (
    <li className="ix-dir-metric">
      <div className="ix-dir-metric-line">
        <span className="ix-dir-metric-name">{m.label}</span>
        <span className="ix-dir-metric-value">
          {m.latest == null ? 'never measured' : fmt(m.latest, m.unit)}
          {m.daysAgo != null && m.daysAgo > 0 && <span className="insight-muted"> · {m.daysAgo}d ago</span>}
        </span>
        <Chip tone={s.tone}>{s.word}</Chip>
      </div>
      <p className="ix-dir-reason">{m.trajectoryReason}</p>
      {MOVED.includes(m.trajectory) && (
        project
          ? <ProjectionLine metric={m.metric} unit={m.unit} />
          : <button className="ix-dir-link" onClick={() => setProject(true)}>If this continues…</button>
      )}
    </li>
  );
}

function ProjectionLine({ metric, unit }: { metric: string; unit: string | null }) {
  const { data, isLoading } = useQuery({
    queryKey: ['projection', metric],
    queryFn: () => get<ProjectionResult>(`${API}/api/health/projection?metric=${encodeURIComponent(metric)}&horizonDays=90`),
  });
  if (isLoading || !data) return <p className="ix-dir-reason">Working it out…</p>;

  if (!data.projected)
    return <p className="ix-dir-projection is-refused">{data.refusal}</p>;

  return (
    <div className="ix-dir-projection">
      <p>
        {data.basis.replace(/\.$/, '')}, it would be between <b>{fmt(data.projectedLow, unit)}</b> and <b>{fmt(data.projectedHigh, unit)}</b>,
        from {fmt(data.currentLevel, unit)} now.
      </p>
      <p className="insight-muted">
        Evidence grade {data.grade} (emerging). {data.caveat}
        {data.horizonNote && <> {data.horizonNote}</>}
      </p>
    </div>
  );
}

// ── Held normals ─────────────────────────────────────────────────────────────

export function HoldsSection({ compact = false }: { compact?: boolean }) {
  const qc = useQueryClient();
  const { data } = useQuery({ queryKey: ['regime-holds'], queryFn: () => get<Hold[]>(`${API}/api/health/regime-holds`) });

  const accept = useMutation({
    mutationFn: async (h: Hold) => {
      const r = await fetch(`${VITARA}/api/regime-acceptances`, {
        method: 'POST',
        headers: { ...vitaraHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ metric: h.metric, signature: h.signature, changePoint: h.changePoint }),
      });
      const json = await r.json();
      if (!r.ok) throw new Error(json?.error ?? `${r.status}`);
      return json as { note: string };
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['regime-holds'] }),
  });

  const open = (data ?? []).filter(h => h.open);
  if (compact && open.length === 0) return null;
  if (!data) return null;

  return (
    <section className={compact ? 'ix-dir-holds is-compact' : 'insight-section'}>
      {!compact && (
        <h2 className="insight-h2">
          Normals being held
          <Info label="What a held normal is">
            When a measure steps to a worse level and nothing you have recorded explains it, Vitara keeps
            measuring you against your level from before the step instead of quietly calling the worse number
            normal. It lets go when you record a reason (a medication, a new ring, an illness), when you say the
            new level is your normal, or when it comes back on its own.
          </Info>
        </h2>
      )}

      {open.length === 0 && !compact && (
        <p className="insight-muted insight-lede">Nothing is being held: every step so far was explained, harmless, or came back.</p>
      )}

      {open.map(h => (
        <div key={`${h.metric}-${h.changePoint}`} className="insight-card ix-dir-hold">
          <p className="insight-eyebrow">{h.label} · your normal is being held</p>
          <p className="insight-summary">
            Moved from {fmt(h.before, h.unit)} to {fmt(h.after, h.unit)} around {h.changePoint}, and nothing recorded
            explains it. Your normal has stayed at {fmt(h.before, h.unit)} for {h.heldForDays} days.
          </p>
          {!compact && (
            <div className="hx-form" style={{ marginTop: '0.6rem' }}>
              <button className="hx-btn" disabled={accept.isPending} onClick={() => accept.mutate(h)}>
                This is my new normal
              </button>
              <span className="insight-muted">or record what changed (a medication, a device, an illness) and it is released on its own.</span>
            </div>
          )}
          {accept.data && <p className="insight-muted">{accept.data.note}</p>}
          {accept.error && <p className="hx-error">{(accept.error as Error).message}</p>}
        </div>
      ))}

      {!compact && data.filter(h => !h.open).length > 0 && (
        <details className="insight-card">
          <summary className="insight-eyebrow">Released</summary>
          <ul className="insight-asks insight-asks-muted">
            {data.filter(h => !h.open).map(h => (
              <li key={`${h.metric}-${h.changePoint}`}>
                {h.label}, step around {h.changePoint}: {h.releaseReason}
                {h.explanation ? ` (${h.explanation})` : ''} on {h.releasedOn}.
              </li>
            ))}
          </ul>
        </details>
      )}
    </section>
  );
}

// ── What if ──────────────────────────────────────────────────────────────────

const CHANGES: { id: string; label: string; amount?: { label: string; unit: string; value: number; step: number } }[] = [
  { id: 'short_sleep', label: 'Sleep less', amount: { label: 'per night', unit: 'min', value: 360, step: 15 } },
  { id: 'late_bedtime', label: 'Go to bed later', amount: { label: 'later', unit: 'min', value: 90, step: 15 } },
  { id: 'more_training', label: 'Train more', amount: { label: 'extra a day', unit: 'kcal', value: 400, step: 50 } },
  { id: 'alcohol', label: 'Drink more or less' },
  { id: 'new_diet', label: 'Try a new diet' },
  { id: 'new_supplement', label: 'Start a supplement' },
  { id: 'new_medication', label: 'Start a medication' },
];

export function ScenarioSection() {
  const [change, setChange] = useState(CHANGES[0].id);
  const spec = CHANGES.find(c => c.id === change)!;
  const [amount, setAmount] = useState<number>(spec.amount?.value ?? 0);
  const [days, setDays] = useState(5);

  const pick = (id: string) => {
    setChange(id);
    setAmount(CHANGES.find(c => c.id === id)?.amount?.value ?? 0);
  };

  const { data, isFetching } = useQuery({
    queryKey: ['scenario', change, amount, days],
    queryFn: () => get<ScenarioResult>(
      `${API}/api/health/scenario?change=${change}&days=${days}${spec.amount ? `&amount=${amount}` : ''}`),
  });

  return (
    <section className="insight-section">
      <h2 className="insight-h2">
        What if I…
        <Info label="How these are answered">
          Each answer says how it was reached. Calculated: Vitara’s own rules on your numbers. From your history:
          what your own days did when you did this before. An experiment: your data cannot answer it yet, so it
          shows how to find out. A medication is never given an expected effect.
        </Info>
      </h2>

      <div className="insight-card">
        <div className="ix-dir-picks" role="radiogroup" aria-label="What would change">
          {CHANGES.map(c => (
            <button key={c.id} role="radio" aria-checked={c.id === change}
                    className={`ix-dir-pick ${c.id === change ? 'is-on' : ''}`} onClick={() => pick(c.id)}>
              {c.label}
            </button>
          ))}
        </div>

        {spec.amount && (
          <div className="hx-form" style={{ marginTop: '0.75rem' }}>
            <label className="insight-muted">
              <input type="number" value={amount} step={spec.amount.step}
                     onChange={e => setAmount(Number(e.target.value))} style={{ width: '6rem' }} /> {spec.amount.unit} {spec.amount.label}
            </label>
            <label className="insight-muted">
              for <input type="number" min={1} max={14} value={days}
                         onChange={e => setDays(Math.min(14, Math.max(1, Number(e.target.value))))} style={{ width: '4rem' }} /> days
            </label>
          </div>
        )}

        {data && (
          <div className={`ix-dir-answers ${isFetching ? 'is-loading' : ''}`}>
            {data.answers.map((a, i) => (
              <div key={i} className={`ix-dir-answer mode-${a.mode}`}>
                <span className="ix-dir-mode" title={MODE[a.mode]?.note}>{MODE[a.mode]?.word ?? a.mode}</span>
                <p className="ix-dir-q">{a.question}</p>
                <p className="ix-dir-a">{a.answer}</p>
              </div>
            ))}

            {data.experiment && (
              <div className="ix-dir-answer mode-experiment">
                <span className="ix-dir-mode">How to find out</span>
                {data.experiment.doctorNote && <p className="ix-dir-a"><b>{data.experiment.doctorNote}</b></p>}
                <p className="ix-dir-a">{data.experiment.howItIsJudged}</p>
                <p className="ix-dir-a">
                  Good targets: {data.experiment.suggestedTargets.join(', ')}. For a daily measure, the first answer comes
                  about {data.experiment.daysUntilFirstVerdict} days after you start. Record it under “Did it work” on the
                  Patterns tab.
                </p>
              </div>
            )}

            <p className="insight-muted" style={{ marginTop: '0.6rem', fontSize: '0.8rem' }}>{data.caveat}</p>
          </div>
        )}
      </div>
    </section>
  );
}

// ── The tab ──────────────────────────────────────────────────────────────────

export function DirectionTab() {
  return (
    <>
      <HoldsSection />
      <DomainsSection />
      <ScenarioSection />
    </>
  );
}

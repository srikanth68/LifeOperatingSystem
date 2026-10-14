import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { API, get, useNamer, WhyLink, f1 } from './insightKit';
import type { Why } from './insightKit';

// Insight › You and Insight › Check on the phone (canvas boards You and Check).
//
// You: how this particular body runs. When it sleeps, which day of the week goes worst, how
// long it takes to come back from a hard day, whether early warnings have caught real
// illness, and a sheet to take to the doctor.
// Check: an audit of the software, not the body. What each detector can see, what it cannot
// yet, and the one thing that would let it.

// ── Types ───────────────────────────────────────────────────────────────────

interface BioSignature {
  confidence: { settled: number; learning: number; daysOfHistory: number; note: string };
  sleepClock: { bedtime: string; wake: string; bedtimeVariabilityMinutes: number; socialJetlagMinutes: number; nights: number; note: string } | null;
  week: { byDay: Record<string, number>; best: string; worst: string; spread: number; weeks: number; note: string } | null;
  recovery: { days: number | null; episodes: number; note: string };
}
interface VisitBrief {
  generatedOn: string; scope: string; verdict: string;
  bring: { topic: string; what: string; since: string | null; severity: string; ask: string | null }[];
  questions: string[]; notLookedAt: string[]; coverage: string; disclaimer: string;
}
interface IllnessEval {
  windowDays: number; episodeCount: number; caught: number; missed: number; falseAlarmCount: number;
  medianLeadDays: number | null; verdict: string;
  episodes: { start: string; end: string; caught: boolean; leadDays: number | null; notes: string | null }[];
}
interface CapabilityRow { key: string; group: string; label: string; state: 'speaking' | 'quiet' | 'blind'; says: string; needs: string[]; everSaid: number; grade: string | null }
interface SelfCheck {
  asOf: string; speaking: number; quiet: number; blind: number; headline: string[];
  capabilities: CapabilityRow[];
  coverage: { metric: string; label: string; baseline: 'ready' | 'learning' | 'none'; readings: number; daysSinceLast: number | null; state: string }[];
}

const useSignature = () => useQuery({ queryKey: ['signature'], queryFn: () => get<BioSignature>(`${API}/api/health/signature`) });

// ── You ─────────────────────────────────────────────────────────────────────

const mins = (hhmm: string) => { const [h, m] = hhmm.split(':').map(Number); return h * 60 + m; };
const clock = (m: number) => { const x = ((Math.round(m) % 1440) + 1440) % 1440; return `${String(Math.floor(x / 60)).padStart(2, '0')}:${String(x % 60).padStart(2, '0')}`; };

// An arc on a 24-hour dial, midnight at the top, running clockwise from a to b.
function arc(cx: number, cy: number, r: number, a: number, b: number): string {
  const ang = (m: number) => (m / 1440) * 2 * Math.PI - Math.PI / 2;
  const span = ((b - a) % 1440 + 1440) % 1440;
  const p = (m: number) => `${f1(cx + r * Math.cos(ang(m)))} ${f1(cy + r * Math.sin(ang(m)))}`;
  return `M ${p(a)} A ${r} ${r} 0 ${span > 720 ? 1 : 0} 1 ${p(b)}`;
}

function SleepClock({ why }: { why: (w: Why) => void }) {
  const q = useSignature();
  const s = q.data?.sleepClock;
  if (!q.data) return <section className="vm-tile vmi-skel" aria-busy="true" />;
  if (!s) return <section className="vm-tile vm-msg"><b>When you sleep</b><p>Needs about two weeks of nights before your sleep times mean anything.</p></section>;

  const bed = mins(s.bedtime), wake = mins(s.wake), lag = s.socialJetlagMinutes;
  return (
    <section className="vm-tile vmi-clock" aria-label="When you sleep">
      <svg width="150" height="150" viewBox="0 0 160 160" role="img"
           aria-label={`Usually asleep ${s.bedtime} to ${s.wake}${lag >= 15 ? `; free nights about ${Math.round(lag)} minutes later` : ''}`}>
        <circle cx="80" cy="80" r="54" className="vmi-dial" />
        <path d={arc(80, 80, 54, bed, wake)} className="vmi-dial-work" />
        {lag >= 15 && <path d={arc(80, 80, 40, bed + lag, wake + lag)} className="vmi-dial-free" />}
        <text x="80" y="16" textAnchor="middle" className="vmi-t">00</text>
        <text x="152" y="84" textAnchor="middle" className="vmi-t">06</text>
        <text x="80" y="156" textAnchor="middle" className="vmi-t">12</text>
        <text x="8" y="84" textAnchor="middle" className="vmi-t">18</text>
        <text x="80" y="78" textAnchor="middle" className="vmi-clock-num">{s.bedtime}</text>
        <text x="80" y="96" textAnchor="middle" className="vmi-t">to {s.wake}</text>
      </svg>
      <div className="vmi-clock-body">
        <p className="vmi-eyebrow vmi-violet">When you sleep</p>
        <span className="vmi-keys"><i className="k-work" />usual nights</span>
        {lag >= 15
          ? <span className="vmi-keys"><i className="k-free" />free nights, {Math.round(lag)} min later</span>
          : <span className="vmi-muted vmi-small">Same times on free nights</span>}
        <span className="vmi-muted vmi-small">bedtime varies ±{Math.round(s.bedtimeVariabilityMinutes)} min</span>
        <WhyLink onClick={() => why({
          title: 'When you sleep',
          body: <>
            <p>{s.note}</p>
            <p className="vmi-muted">From {s.nights} nights. The difference between your free nights and the rest ({Math.round(lag)} min) is often called social jet lag: the body pays for it the way it pays for a time-zone change.</p>
            <p className="vmi-muted">Free nights asleep about {clock(bed + lag)} to {clock(wake + lag)}.</p>
          </>,
        })} />
      </div>
    </section>
  );
}

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function Week({ why }: { why: (w: Why) => void }) {
  const w = useSignature().data?.week;
  if (!w) return null;
  const vals = DAYS.map(d => w.byDay[d] ?? null);
  const nums = vals.filter((v): v is number => v != null);
  if (nums.length < 5) return null;
  const lo = Math.min(...nums) - 6, hi = Math.max(...nums);
  return (
    <section className="vm-tile" aria-label="Your week">
      <h2 className="vmi-h2"><span>Your week</span><em>average readiness by day</em></h2>
      <div className="vmi-week" role="img" aria-label={`${w.worst} is your lowest day, about ${Math.round(w.spread)} points under ${w.best}`}>
        {DAYS.map((d, i) => {
          const v = vals[i];
          const h = v == null ? 0 : 30 + ((v - lo) / (hi - lo || 1)) * 70;
          return (
            <div key={d} className="vmi-week-col">
              <div className={`vmi-week-bar ${d === w.worst ? 'is-worst' : d === w.best ? 'is-best' : ''}`} style={{ height: `${h}%` }}>
                {v != null && <span>{Math.round(v)}</span>}
              </div>
              <small>{d[0]}</small>
            </div>
          );
        })}
      </div>
      <div className="vmi-row">
        <span className="vmi-muted vmi-small">{w.worst}s run {Math.round(w.spread)} points under {w.best}s, over {w.weeks} weeks</span>
        <WhyLink onClick={() => why({ title: 'Your week', body: <><p>{w.note}</p><p className="vmi-muted">Readiness is the ring's own score. A gap of a few points can be chance; it is shown once it has held over {w.weeks} weeks.</p></> })} />
      </div>
    </section>
  );
}

function BounceBack({ why }: { why: (w: Why) => void }) {
  const r = useSignature().data?.recovery;
  if (!r) return null;
  const days = r.days;
  return (
    <section className="vm-tile vmi-bounce" aria-label="Bounce-back">
      <div className="vmi-bounce-head">
        <p className="vmi-eyebrow vmi-teal">Bounce-back</p>
        <b className="vmi-bounce-big">{days == null ? '—' : `${days} ${days === 1 ? 'day' : 'days'}`}</b>
        <span className="vmi-muted vmi-small">{days == null ? 'not enough hard days yet' : `after a hard day · seen ${r.episodes} times`}</span>
      </div>
      {days != null && (
        <div className="vmi-bounce-track" role="img" aria-label={`Back to your usual about ${days} days after a hard day`}>
          <span className="is-hard"><i />hard day</span>
          {Array.from({ length: Math.min(days, 6) }, (_, i) => (
            <span key={i} className={i === Math.min(days, 6) - 1 ? 'is-back' : ''}><i />{i === Math.min(days, 6) - 1 ? 'back' : `day ${i + 1}`}</span>
          ))}
        </div>
      )}
      <WhyLink onClick={() => why({ title: 'Bounce-back', body: <><p>{r.note}</p><p className="vmi-muted">A hard day is one of your own, measured against your own load; "back" is readiness inside your usual range again. The middle of {r.episodes} times.</p></> })} />
    </section>
  );
}

function CaughtEarly({ why }: { why: (w: Why) => void }) {
  const q = useQuery({ queryKey: ['illness-eval'], queryFn: () => get<IllnessEval>(`${API}/api/health/illness-eval`) });
  const d = q.data;
  if (!d || d.episodeCount === 0) return null;
  return (
    <section className="vm-tile" aria-label="Caught early">
      <h2 className="vmi-h2"><span>Caught early</span><em>last {d.windowDays} days</em></h2>
      <div className="vmi-caught" role="img" aria-label={`${d.caught} of ${d.episodeCount} times you were ill were flagged first; ${d.falseAlarmCount} false alarms`}>
        {d.episodes.map(e => <i key={e.start} className={e.caught ? 'is-caught' : ''} title={`${e.start}${e.caught ? `, flagged ${e.leadDays} days ahead` : ', missed'}`} />)}
      </div>
      <p className="vmi-caught-line">
        <b>{d.caught} of {d.episodeCount}</b> illnesses flagged first
        {d.medianLeadDays != null && <>, usually <b>{d.medianLeadDays} days</b> ahead</>}
        <span className="vmi-muted"> · {d.falseAlarmCount} false alarm{d.falseAlarmCount === 1 ? '' : 's'}</span>
      </p>
      <WhyLink onClick={() => why({ title: 'Caught early', body: <><p>{d.verdict}</p><p className="vmi-muted">Scored only against illnesses you recorded. A warning with no illness after it counts as a false alarm, so this is the system marking its own homework, not a guarantee.</p></> })} />
    </section>
  );
}

function briefText(b: VisitBrief): string {
  const lines = [`Health summary for my doctor (${b.generatedOn})`, '', b.verdict, ''];
  if (b.bring.length) {
    lines.push('Worth discussing:');
    for (const x of b.bring) lines.push(`- ${x.topic}: ${x.what}${x.since ? ` (since ${x.since})` : ''}${x.ask ? ` Ask: ${x.ask}` : ''}`);
    lines.push('');
  }
  if (b.questions.length) { lines.push('Questions:'); b.questions.forEach(q => lines.push(`- ${q}`)); lines.push(''); }
  if (b.notLookedAt.length) lines.push(`Not covered: ${b.notLookedAt.join(', ')}.`);
  lines.push('', b.disclaimer);
  return lines.join('\n');
}

function DoctorSheet() {
  const q = useQuery({ queryKey: ['visit-brief'], queryFn: () => get<VisitBrief>(`${API}/api/health/visit-brief`) });
  const [open, setOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const b = q.data;
  if (!b) return null;

  const share = async () => {
    const text = briefText(b);
    try {
      if (navigator.share) { await navigator.share({ title: 'Health summary', text }); return; }
      await navigator.clipboard.writeText(text); setCopied(true);
    } catch { /* cancelled */ }
  };

  return (
    <>
      <section className="vm-tile vmi-doctor" aria-label="Doctor sheet">
        <div>
          <p className="vmi-eyebrow vmi-blue">Doctor sheet</p>
          <span className="vmi-muted vmi-small">{b.bring.length} to bring · {b.questions.length} questions</span>
        </div>
        <button type="button" className="vmi-btn is-primary" onClick={() => setOpen(true)}>Make it</button>
      </section>

      <div className={`vm-scrim ${open ? 'is-on' : ''}`} onClick={() => setOpen(false)} aria-hidden="true" />
      <div className={`vm-sheet vmi-why ${open ? 'is-on' : ''}`} role="dialog" aria-label="Doctor sheet" aria-hidden={!open}>
        <span className="vm-grab" aria-hidden="true" />
        <div className="vmi-why-head">
          <h2>For your doctor</h2>
          <button className="vmi-close" onClick={() => setOpen(false)} aria-label="Close" tabIndex={open ? 0 : -1}>×</button>
        </div>
        <div className="vmi-why-body">
          <p>{b.verdict}</p>
          {b.bring.map(x => (
            <div key={x.topic} className={`vmi-bring sev-${x.severity}`}>
              <b>{x.topic}</b>
              <span>{x.what}{x.since ? ` Since ${x.since}.` : ''}</span>
              {x.ask && <em>Ask: {x.ask}</em>}
            </div>
          ))}
          {b.questions.length > 0 && <>
            <p className="vmi-eyebrow">Questions to ask</p>
            <ol className="vmi-steps">{b.questions.map(x => <li key={x}>{x}</li>)}</ol>
          </>}
          {b.notLookedAt.length > 0 && <p className="vmi-muted vmi-small">Not covered: {b.notLookedAt.join(', ')}.</p>}
          <p className="vmi-muted vmi-small">{b.disclaimer}</p>
          <button type="button" className="vm-cta" onClick={() => { void share(); }} tabIndex={open ? 0 : -1}>{copied ? 'Copied' : 'Share or copy'}</button>
        </div>
      </div>
    </>
  );
}

export function YouView({ why }: { why: (w: Why) => void }) {
  return (
    <>
      <SleepClock why={why} />
      <Week why={why} />
      <BounceBack why={why} />
      <CaughtEarly why={why} />
      <DoctorSheet />
    </>
  );
}

// ── Check ───────────────────────────────────────────────────────────────────

const STATE_WORD = { speaking: 'speaking', quiet: 'quiet', blind: "can't see" } as const;
const STATE_ORDER = { speaking: 0, quiet: 1, blind: 2 } as const;

export function CheckView({ why }: { why: (w: Why) => void }) {
  const q = useQuery({ queryKey: ['self-check'], queryFn: () => get<SelfCheck>(`${API}/api/health/self-check`) });
  const { name } = useNamer();
  const d = q.data;
  if (q.isLoading) return <section className="vm-tile vmi-skel" aria-busy="true" />;
  if (!d) return <section className="vm-tile vm-msg"><b>Check isn't reachable</b><p>Pull down to try again.</p></section>;

  const caps = [...d.capabilities].sort((a, b) => STATE_ORDER[a.state] - STATE_ORDER[b.state] || a.label.localeCompare(b.label));
  const ready = d.coverage.filter(c => c.baseline === 'ready').length;
  const learning = d.coverage.filter(c => c.baseline === 'learning').length;
  const none = d.coverage.filter(c => c.baseline === 'none').length;
  const total = ready + learning + none || 1;

  return (
    <>
      <p className="vmi-lede">What Vitara can see in you, and what it can't yet.</p>
      <section className="vmi-counts" aria-label="Detectors">
        {(['speaking', 'quiet', 'blind'] as const).map(s => (
          <div key={s} className={`vmi-count c-${s}`}>
            <i /><b>{d[s]}</b><span>{STATE_WORD[s]}</span>
          </div>
        ))}
      </section>

      <section className="vm-tile" aria-label="Each detector">
        <h2 className="vmi-h2"><span>Each detector</span><em>as of {d.asOf}</em></h2>
        <div className="vmi-caps">
          {caps.map(c => (
            <button key={c.key} type="button" className={`vmi-cap c-${c.state}`} onClick={() => why({
              title: c.label,
              body: <>
                <p>{c.says}</p>
                {c.needs.length > 0 && <>
                  <p className="vmi-eyebrow">What would let it see</p>
                  <ul className="vmi-comp">{c.needs.map(n => <li key={n}><em>+</em><span>{n}</span></li>)}</ul>
                </>}
                <p className="vmi-muted vmi-small">Has spoken {c.everSaid} {c.everSaid === 1 ? 'time' : 'times'} so far. Quiet means it is looking and found nothing; can't see means it has nothing to look at.</p>
                {c.grade && <p className="vmi-grade"><b>{c.grade}</b><span>Evidence behind what it looks for.</span></p>}
              </>,
            })}>
              <i />
              <span>{c.label}</span>
              <em>{c.state === 'blind' && c.needs[0] ? `+ ${c.needs[0]}` : STATE_WORD[c.state]}</em>
            </button>
          ))}
        </div>
      </section>

      <section className="vm-tile" aria-label="Normals learned">
        <h2 className="vmi-h2"><span>Your normal is known for</span><em>{ready} of {total}</em></h2>
        <div className="vmi-split" role="img" aria-label={`${ready} measures with a normal, ${learning} still learning, ${none} with no readings`}>
          {ready > 0 && <div className="is-ready" style={{ flex: ready }}>{ready}</div>}
          {learning > 0 && <div className="is-learning" style={{ flex: learning }}>{learning}</div>}
          {none > 0 && <div className="is-none" style={{ flex: none }}>{none}</div>}
        </div>
        <div className="vmi-keys vmi-keys-wrap">
          <span><i className="k-ok" />known</span><span><i className="k-learn" />still learning</span><span><i className="k-none" />no readings</span>
        </div>
        <WhyLink onClick={() => why({
          title: 'How a normal is learned',
          body: <>
            <p>A measure needs 21 readings before its normal is trusted. Until then it is recorded and shown, but never flagged and never called fine.</p>
            {learning > 0 && <p className="vmi-muted">Still learning: {d.coverage.filter(c => c.baseline === 'learning').map(c => c.label || name(c.metric)).join(', ')}.</p>}
          </>,
        })} />
      </section>
    </>
  );
}

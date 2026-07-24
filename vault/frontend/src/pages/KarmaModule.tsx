import { useState, useEffect, useCallback } from 'react';
import '../styles/modules.css';
import '../styles/karma.css';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { ApiUnreachable } from '../components/ApiUnreachable';

const API = moduleApi(5600);
type Page = 'habits' | 'goals' | 'progress';
const TABS: { id: Page; label: string }[] = [
  { id: 'habits', label: 'Habits' },
  { id: 'goals', label: 'Goals' },
  { id: 'progress', label: 'Progress' },
];
const MC = 'var(--karma)';
const style = { '--mc': MC } as React.CSSProperties;

const CATEGORIES = ['health', 'learning', 'personal', 'productivity', 'fitness', 'mindfulness'];
const GOAL_CATS = ['study', 'project', 'personal', 'health', 'financial', 'career', 'other'];
const CAT_EMOJI: Record<string, string> = {
  health: '❤️', learning: '📚', personal: '🌱', productivity: '⚡',
  fitness: '🏃', mindfulness: '🧘',
  study: '📖', project: '🚀', financial: '💰', career: '🎯', other: '✨',
};
const EMOJI_PRESETS = ['✅', '💧', '🏋️', '📖', '🧘', '🍎', '😴', '💊', '📝', '🎯', '🚶', '🔥'];
const DAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];

interface Habit {
  id: string; name: string; description?: string; emoji: string;
  category: string; notifyTime?: string; notifyMessage?: string;
  notifyChannel: string; notifyDays: number[];
  isActive: boolean; currentStreak: number; bestStreak: number;
  todayCompleted?: boolean; createdAt: string;
}

interface GoalLink { label: string; url: string; }
interface Milestone { id: string; title: string; targetDate?: string; completed: boolean; completedAt?: string; }
interface Goal {
  id: string; title: string; description?: string; category: string;
  status: string; progress: number; targetDate?: string;
  links: GoalLink[]; resources?: string; tags?: string;
  milestones: Milestone[]; createdAt: string; completedAt?: string;
}

interface HabitLog { date: string; completed: boolean; }

// ── API helpers ───────────────────────────────────────────────
async function apiFetch(path: string, opts?: RequestInit) {
  const r = await fetch(`${API}${path}`, {
    ...opts,
    headers: { 'Content-Type': 'application/json', ...authHeaders(), ...(opts?.headers ?? {}) },
  });
  if (!r.ok) throw new Error(await r.text());
  if (r.status === 204) return null;
  return r.json();
}

// ── Check icon ────────────────────────────────────────────────
function CheckIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  );
}
function ChevronIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ width: 16, height: 16 }}>
      <polyline points="6 9 12 15 18 9" />
    </svg>
  );
}
function LinkIcon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6" />
      <polyline points="15 3 21 3 21 9" /><line x1="10" y1="14" x2="21" y2="3" />
    </svg>
  );
}

interface HabitStats {
  habitId: string; totalLogged: number; totalCompleted: number;
  completionRate: number; currentStreak: number; bestStreak: number;
  dayOfWeekCompletions: number[]; logs: HabitLog[];
}
interface LinkedHabit { id: string; name: string; emoji: string; currentStreak: number; last7Rate: number; }
interface GoalRef { id: string; title: string; }

// GitHub-style completion heatmap: last ~17 weeks of days, colored by completion.
function HabitHeatmap({ stats }: { stats: HabitStats }) {
  const completedDays = new Set(stats.logs.filter(l => l.completed).map(l => l.date));
  const loggedDays = new Set(stats.logs.map(l => l.date));

  const weeks = 17;
  const today = new Date();
  const start = new Date(today);
  start.setDate(start.getDate() - (weeks * 7 - 1));
  // align to Sunday
  start.setDate(start.getDate() - start.getDay());

  const cells: { date: string; state: 'done' | 'miss' | 'none' | 'future' }[] = [];
  for (let i = 0; i < weeks * 7; i++) {
    const d = new Date(start);
    d.setDate(d.getDate() + i);
    const iso = d.toISOString().slice(0, 10);
    let state: 'done' | 'miss' | 'none' | 'future' = 'none';
    if (d > today) state = 'future';
    else if (completedDays.has(iso)) state = 'done';
    else if (loggedDays.has(iso)) state = 'miss';
    cells.push({ date: iso, state });
  }

  const DOW = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
  const maxDow = Math.max(1, ...stats.dayOfWeekCompletions);

  return (
    <div className="karma-heatmap-wrap">
      <div className="karma-stats-row">
        <div className="karma-stat-pill"><b>{Math.round(stats.completionRate * 100)}%</b><span>completion</span></div>
        <div className="karma-stat-pill"><b>{stats.currentStreak}</b><span>current 🔥</span></div>
        <div className="karma-stat-pill"><b>{stats.bestStreak}</b><span>best</span></div>
        <div className="karma-stat-pill"><b>{stats.totalCompleted}</b><span>total done</span></div>
      </div>

      <div className="karma-heatmap">
        {Array.from({ length: weeks }, (_, w) => (
          <div key={w} className="karma-heat-col">
            {Array.from({ length: 7 }, (_, d) => {
              const cell = cells[w * 7 + d];
              return <span key={d} className={`karma-heat-cell ${cell.state}`} title={`${cell.date}${cell.state === 'done' ? ' ✓' : cell.state === 'miss' ? ' ✕' : ''}`} />;
            })}
          </div>
        ))}
      </div>

      <div className="karma-dow">
        {DOW.map((label, i) => (
          <div key={i} className="karma-dow-item">
            <div className="karma-dow-bar" style={{ height: `${(stats.dayOfWeekCompletions[i] / maxDow) * 32 + 2}px` }} />
            <span>{label}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Habits Page ───────────────────────────────────────────────
function HabitsPage() {
  const [habits, setHabits] = useState<Habit[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [popping, setPopping] = useState<string | null>(null);

  // form state
  const [fName, setFName] = useState('');
  const [fEmoji, setFEmoji] = useState('✅');
  const [fCat, setFCat] = useState('personal');
  const [fNotifyTime, setFNotifyTime] = useState('');
  const [fNotifyMsg, setFNotifyMsg] = useState('');
  const [fDays, setFDays] = useState<number[]>([0, 1, 2, 3, 4, 5, 6]);
  const [fGoalId, setFGoalId] = useState('');
  const [fSaving, setFSaving] = useState(false);
  const [goalOptions, setGoalOptions] = useState<GoalRef[]>([]);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [statsCache, setStatsCache] = useState<Record<string, HabitStats>>({});

  const load = useCallback(async () => {
    try {
      const data = await apiFetch('/api/habits/today');
      setHabits(data);
      setErr(false);
    } catch { setErr(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiFetch('/api/goals?status=active').then((g: Goal[]) => setGoalOptions(g.map(x => ({ id: x.id, title: x.title })))).catch(() => {}); }, []);

  const openStats = async (habitId: string) => {
    if (expandedId === habitId) { setExpandedId(null); return; }
    setExpandedId(habitId);
    if (!statsCache[habitId]) {
      try {
        const s = await apiFetch(`/api/habits/${habitId}/stats?days=180`);
        setStatsCache(prev => ({ ...prev, [habitId]: s }));
      } catch { /* ignore */ }
    }
  };

  if (!loading && err) return <ApiUnreachable name="Karma" port={5600} mc="var(--karma)" onRetry={() => { setLoading(true); load(); }} />;

  const toggle = async (h: Habit) => {
    const newVal = !h.todayCompleted;
    setPopping(h.id);
    setTimeout(() => setPopping(null), 300);
    setHabits(prev => prev.map(x => x.id === h.id ? { ...x, todayCompleted: newVal, currentStreak: newVal ? x.currentStreak + 1 : Math.max(0, x.currentStreak - 1) } : x));
    try {
      await apiFetch(`/api/habits/${h.id}/log`, {
        method: 'POST',
        body: JSON.stringify({ completed: newVal }),
      });
      await load();
    } catch { await load(); }
  };

  const toggleDay = (d: number) =>
    setFDays(prev => prev.includes(d) ? prev.filter(x => x !== d) : [...prev, d].sort());

  const saveHabit = async () => {
    if (!fName.trim()) return;
    setFSaving(true);
    try {
      await apiFetch('/api/habits', {
        method: 'POST',
        body: JSON.stringify({
          name: fName, emoji: fEmoji, category: fCat,
          notifyTime: fNotifyTime || null, notifyMessage: fNotifyMsg || null,
          notifyDays: fDays, goalId: fGoalId || null,
        }),
      });
      setShowForm(false);
      setFName(''); setFEmoji('✅'); setFCat('personal');
      setFNotifyTime(''); setFNotifyMsg(''); setFDays([0, 1, 2, 3, 4, 5, 6]); setFGoalId('');
      await load();
    } catch { /* ignore */ }
    finally { setFSaving(false); }
  };

  const done = habits.filter(h => h.todayCompleted).length;
  const total = habits.length;
  const pct = total > 0 ? done / total : 0;
  const r = 22; const circ = 2 * Math.PI * r;

  const today = new Date();
  const dayName = today.toLocaleDateString('en-US', { weekday: 'long' });
  const dateFmt = today.toLocaleDateString('en-US', { month: 'long', day: 'numeric' });

  return (
    <div style={style}>
      {/* Header */}
      <div className="karma-header">
        <div>
          <div className="karma-date-label">{dayName}</div>
          <div className="karma-title">{dateFmt}</div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          {total > 0 && (
            <div className="karma-progress-ring">
              <div className="karma-ring-wrap">
                <svg viewBox="0 0 52 52">
                  <circle className="karma-ring-bg" cx="26" cy="26" r={r} />
                  <circle className="karma-ring-fg" cx="26" cy="26" r={r}
                    strokeDasharray={circ}
                    strokeDashoffset={circ * (1 - pct)} />
                </svg>
                <div className="karma-ring-count">{done}/{total}</div>
              </div>
              <div className="karma-ring-label">today</div>
            </div>
          )}
          <button className="btn-primary" style={{ fontSize: '0.78rem' }} onClick={() => setShowForm(v => !v)}>
            {showForm ? '✕ Cancel' : '+ Add Habit'}
          </button>
        </div>
      </div>

      {/* Add Habit Form */}
      {showForm && (
        <div className="karma-form-panel">
          <h4>New Habit</h4>
          <div className="karma-form-grid">
            <div className="full">
              <label className="karma-form-label">Name</label>
              <input className="karma-input" placeholder="e.g. Drink 2L water" value={fName} onChange={e => setFName(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && saveHabit()} autoFocus />
            </div>
            <div>
              <label className="karma-form-label">Emoji</label>
              <div className="karma-emoji-options">
                {EMOJI_PRESETS.map(em => (
                  <button key={em} className={`karma-emoji-btn ${fEmoji === em ? 'active' : ''}`} onClick={() => setFEmoji(em)}>{em}</button>
                ))}
              </div>
            </div>
            <div>
              <label className="karma-form-label">Category</label>
              <select className="karma-select" value={fCat} onChange={e => setFCat(e.target.value)}>
                {CATEGORIES.map(c => <option key={c} value={c}>{c.charAt(0).toUpperCase() + c.slice(1)}</option>)}
              </select>
            </div>
            <div>
              <label className="karma-form-label">Notify Time (Telegram)</label>
              <input className="karma-input" type="time" value={fNotifyTime} onChange={e => setFNotifyTime(e.target.value)} />
            </div>
            <div>
              <label className="karma-form-label">Notification Message</label>
              <input className="karma-input" placeholder="Leave blank for default" value={fNotifyMsg} onChange={e => setFNotifyMsg(e.target.value)} />
            </div>
            <div>
              <label className="karma-form-label">Link to Goal (optional)</label>
              <select className="karma-select" value={fGoalId} onChange={e => setFGoalId(e.target.value)}>
                <option value="">None</option>
                {goalOptions.map(g => <option key={g.id} value={g.id}>{g.title}</option>)}
              </select>
            </div>
            <div className="full">
              <label className="karma-form-label">Remind on days</label>
              <div className="karma-day-row">
                {DAYS.map((d, i) => (
                  <button key={i} className={`karma-day-btn ${fDays.includes(i) ? 'on' : ''}`} onClick={() => toggleDay(i)}>{d}</button>
                ))}
              </div>
            </div>
          </div>
          <div className="karma-form-actions">
            <button className="btn-secondary" onClick={() => setShowForm(false)}>Cancel</button>
            <button className="btn-primary" onClick={saveHabit} disabled={!fName.trim() || fSaving}>
              {fSaving ? 'Saving…' : 'Add Habit'}
            </button>
          </div>
        </div>
      )}

      {/* Habit List */}
      {loading ? (
        <div style={{ color: 'var(--text3)', textAlign: 'center', padding: '2rem' }}>Loading habits…</div>
      ) : habits.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '3rem 1rem', color: 'var(--text3)' }}>
          <div style={{ fontSize: '2rem', marginBottom: '0.75rem' }}>🌱</div>
          <div style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--text2)', marginBottom: '0.35rem' }}>No habits yet</div>
          <div style={{ fontSize: '0.8rem' }}>Add your first habit to start building streaks.</div>
        </div>
      ) : (
        <div className="habit-list">
          {habits.map(h => (
            <div key={h.id}>
              <div className={`habit-card ${h.todayCompleted ? 'completed' : ''}`}>
                <span className="habit-emoji">{h.emoji}</span>
                <div className="habit-info" style={{ cursor: 'pointer' }} onClick={() => openStats(h.id)}>
                  <div className="habit-name">{h.name}</div>
                  <div className="habit-meta">
                    <span className="habit-cat">{h.category}</span>
                    <span className="habit-streak">
                      <span className="habit-streak-fire">{h.currentStreak}</span>
                      &nbsp;🔥
                      {h.currentStreak !== h.bestStreak && (
                        <span style={{ opacity: 0.55 }}>&nbsp;/ {h.bestStreak} best</span>
                      )}
                    </span>
                    <span className="karma-stats-toggle">{expandedId === h.id ? '▲ stats' : '▾ stats'}</span>
                  </div>
                </div>
                <button
                  className={`habit-check ${h.todayCompleted ? 'checked' : ''} ${popping === h.id ? 'pop' : ''}`}
                  onClick={() => toggle(h)}
                  title={h.todayCompleted ? 'Mark incomplete' : 'Mark complete'}
                >
                  {h.todayCompleted && <CheckIcon />}
                </button>
              </div>
              {expandedId === h.id && (
                statsCache[h.id]
                  ? <HabitHeatmap stats={statsCache[h.id]} />
                  : <div className="karma-heatmap-wrap" style={{ color: 'var(--text3)', fontSize: '0.8rem' }}>Loading analytics…</div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Goals Page ────────────────────────────────────────────────
function GoalsPage() {
  const [goals, setGoals] = useState<Goal[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState(false);
  const [catFilter, setCatFilter] = useState('all');
  const [expanded, setExpanded] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);

  // form state
  const [fTitle, setFTitle] = useState('');
  const [fDesc, setFDesc] = useState('');
  const [fCat, setFCat] = useState('personal');
  const [fTargetDate, setFTargetDate] = useState('');
  const [fResources, setFResources] = useState('');
  const [fTags, setFTags] = useState('');
  const [fLinks, setFLinks] = useState<{ label: string; url: string }[]>([]);
  const [fSaving, setFSaving] = useState(false);

  // inline state per goal
  const [editProgress, setEditProgress] = useState<Record<string, number>>({});
  const [newMilestone, setNewMilestone] = useState<Record<string, string>>({});
  const [linkedCache, setLinkedCache] = useState<Record<string, LinkedHabit[]>>({});

  useEffect(() => {
    if (expanded && !linkedCache[expanded]) {
      apiFetch(`/api/goals/${expanded}/habits`).then((h: LinkedHabit[]) => setLinkedCache(prev => ({ ...prev, [expanded]: h }))).catch(() => {});
    }
  }, [expanded, linkedCache]);

  const load = useCallback(async () => {
    try { setGoals(await apiFetch('/api/goals')); setErr(false); }
    catch { setErr(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  if (!loading && err) return <ApiUnreachable name="Karma" port={5600} mc="var(--karma)" onRetry={() => { setLoading(true); load(); }} />;

  const saveGoal = async () => {
    if (!fTitle.trim()) return;
    setFSaving(true);
    try {
      await apiFetch('/api/goals', {
        method: 'POST',
        body: JSON.stringify({
          title: fTitle, description: fDesc || null, category: fCat,
          targetDate: fTargetDate || null, resources: fResources || null,
          tags: fTags || null, links: fLinks.filter(l => l.label && l.url),
        }),
      });
      setShowForm(false);
      setFTitle(''); setFDesc(''); setFCat('personal');
      setFTargetDate(''); setFResources(''); setFTags(''); setFLinks([]);
      await load();
    } catch { /* ignore */ }
    finally { setFSaving(false); }
  };

  const updateProgress = async (g: Goal, val: number) => {
    setGoals(prev => prev.map(x => x.id === g.id ? { ...x, progress: val } : x));
    try { await apiFetch(`/api/goals/${g.id}/progress`, { method: 'PATCH', body: JSON.stringify(val) }); }
    catch { await load(); }
  };

  const toggleMilestone = async (goalId: string, ms: Milestone) => {
    setGoals(prev => prev.map(g => g.id !== goalId ? g : {
      ...g, milestones: g.milestones.map(m => m.id === ms.id ? { ...m, completed: !m.completed } : m),
    }));
    try {
      await apiFetch(`/api/goals/${goalId}/milestones/${ms.id}`, {
        method: 'PATCH', body: JSON.stringify(!ms.completed),
      });
    } catch { await load(); }
  };

  const addMilestone = async (goalId: string) => {
    const title = newMilestone[goalId]?.trim();
    if (!title) return;
    try {
      await apiFetch(`/api/goals/${goalId}/milestones`, {
        method: 'POST', body: JSON.stringify({ title }),
      });
      setNewMilestone(prev => ({ ...prev, [goalId]: '' }));
      await load();
    } catch { /* ignore */ }
  };

  const deleteGoal = async (id: string) => {
    if (!confirm('Delete this goal?')) return;
    try { await apiFetch(`/api/goals/${id}`, { method: 'DELETE' }); await load(); }
    catch { /* ignore */ }
  };

  const filtered = catFilter === 'all' ? goals : goals.filter(g => g.category === catFilter);
  const statusOrder: Record<string, number> = { active: 0, paused: 1, completed: 2, archived: 3 };
  const sorted = [...filtered].sort((a, b) => (statusOrder[a.status] ?? 9) - (statusOrder[b.status] ?? 9));

  const addLinkRow = () => setFLinks(prev => [...prev, { label: '', url: '' }]);
  const updateLink = (i: number, key: 'label' | 'url', val: string) =>
    setFLinks(prev => prev.map((l, idx) => idx === i ? { ...l, [key]: val } : l));
  const removeLink = (i: number) => setFLinks(prev => prev.filter((_, idx) => idx !== i));

  return (
    <div style={style}>
      {/* Category filter + Add button */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.75rem', flexWrap: 'wrap', gap: '0.5rem' }}>
        <div className="goal-cat-tabs" style={{ marginBottom: 0 }}>
          {['all', ...GOAL_CATS].map(c => (
            <button key={c} className={`goal-cat-tab ${catFilter === c ? 'active' : ''}`} onClick={() => setCatFilter(c)}>
              {c === 'all' ? 'All' : `${CAT_EMOJI[c] || ''} ${c.charAt(0).toUpperCase() + c.slice(1)}`}
            </button>
          ))}
        </div>
        <button className="btn-primary" style={{ fontSize: '0.78rem', flexShrink: 0 }} onClick={() => setShowForm(v => !v)}>
          {showForm ? '✕ Cancel' : '+ Add Goal'}
        </button>
      </div>

      {/* Add Goal Form */}
      {showForm && (
        <div className="goal-form-panel">
          <h4>New Goal</h4>
          <div className="karma-form-grid">
            <div className="full">
              <label className="karma-form-label">Title</label>
              <input className="karma-input" placeholder="e.g. Launch Maaya OS v1.0" value={fTitle} onChange={e => setFTitle(e.target.value)} autoFocus />
            </div>
            <div className="full">
              <label className="karma-form-label">Description</label>
              <textarea className="karma-input" style={{ resize: 'vertical', minHeight: 64 }} placeholder="What does success look like?" value={fDesc} onChange={e => setFDesc(e.target.value)} />
            </div>
            <div>
              <label className="karma-form-label">Category</label>
              <select className="karma-select" value={fCat} onChange={e => setFCat(e.target.value)}>
                {GOAL_CATS.map(c => <option key={c} value={c}>{CAT_EMOJI[c]} {c.charAt(0).toUpperCase() + c.slice(1)}</option>)}
              </select>
            </div>
            <div>
              <label className="karma-form-label">Target Date</label>
              <input className="karma-input" type="date" value={fTargetDate} onChange={e => setFTargetDate(e.target.value)} />
            </div>
            <div className="full">
              <label className="karma-form-label">Tags (comma-separated)</label>
              <input className="karma-input" placeholder="e.g. 2026, side-project, priority" value={fTags} onChange={e => setFTags(e.target.value)} />
            </div>
            <div className="full">
              <label className="karma-form-label">Resources / Notes</label>
              <textarea className="karma-input" style={{ resize: 'vertical', minHeight: 60 }} placeholder="Links, books, notes, anything useful…" value={fResources} onChange={e => setFResources(e.target.value)} />
            </div>
            <div className="full">
              <label className="karma-form-label">Links</label>
              <div className="goal-links-editor">
                {fLinks.map((l, i) => (
                  <div key={i} className="goal-link-row">
                    <input className="karma-input" placeholder="Label" value={l.label} onChange={e => updateLink(i, 'label', e.target.value)} />
                    <input className="karma-input" placeholder="https://..." value={l.url} onChange={e => updateLink(i, 'url', e.target.value)} />
                    <button onClick={() => removeLink(i)}>✕</button>
                  </div>
                ))}
                <button className="btn-secondary" style={{ alignSelf: 'flex-start', fontSize: '0.75rem' }} onClick={addLinkRow}>+ Add Link</button>
              </div>
            </div>
          </div>
          <div className="karma-form-actions">
            <button className="btn-secondary" onClick={() => setShowForm(false)}>Cancel</button>
            <button className="btn-primary" onClick={saveGoal} disabled={!fTitle.trim() || fSaving}>
              {fSaving ? 'Saving…' : 'Create Goal'}
            </button>
          </div>
        </div>
      )}

      {loading ? (
        <div style={{ color: 'var(--text3)', textAlign: 'center', padding: '2rem' }}>Loading goals…</div>
      ) : sorted.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '3rem 1rem', color: 'var(--text3)' }}>
          <div style={{ fontSize: '2rem', marginBottom: '0.75rem' }}>🎯</div>
          <div style={{ fontSize: '0.9rem', fontWeight: 600, color: 'var(--text2)', marginBottom: '0.35rem' }}>
            {catFilter === 'all' ? 'No goals yet' : `No ${catFilter} goals`}
          </div>
          <div style={{ fontSize: '0.8rem' }}>Define what matters and track your progress.</div>
        </div>
      ) : (
        <div>
          {sorted.map(g => {
            const isOpen = expanded === g.id;
            const prog = editProgress[g.id] ?? g.progress;
            const completedMs = g.milestones.filter(m => m.completed).length;
            const tags = g.tags ? g.tags.split(',').map(t => t.trim()).filter(Boolean) : [];

            return (
              <div key={g.id} className={`goal-card ${isOpen ? 'expanded' : ''}`}>
                <div className="goal-card-header" onClick={() => setExpanded(isOpen ? null : g.id)}>
                  <span className="goal-cat-badge">{CAT_EMOJI[g.category] || ''} {g.category}</span>
                  <div className="goal-title-wrap">
                    <div className="goal-title">{g.title}</div>
                    <div className="goal-progress-bar-wrap">
                      <div className="goal-progress-bar" style={{ width: `${prog}%` }} />
                    </div>
                    <div className="goal-meta-row">
                      <span className="goal-pct">{prog}%</span>
                      {g.targetDate && <span className="goal-target-date">📅 {g.targetDate}</span>}
                      {g.milestones.length > 0 && (
                        <span className="goal-milestone-count">
                          {completedMs}/{g.milestones.length} milestones
                        </span>
                      )}
                      {g.status !== 'active' && (
                        <span style={{ fontSize: '0.68rem', color: g.status === 'completed' ? 'var(--vault)' : 'var(--text3)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
                          {g.status}
                        </span>
                      )}
                    </div>
                  </div>
                  <div className={`goal-chevron ${isOpen ? 'open' : ''}`}><ChevronIcon /></div>
                </div>

                {isOpen && (
                  <div className="goal-card-body">
                    {g.description && <div className="goal-desc">{g.description}</div>}

                    {/* Progress slider */}
                    <div className="goal-section-label">Progress</div>
                    <div className="goal-progress-edit">
                      <input type="range" min={0} max={100} className="karma-slider"
                        value={prog}
                        onChange={e => setEditProgress(prev => ({ ...prev, [g.id]: +e.target.value }))}
                        onMouseUp={() => updateProgress(g, prog)}
                        onTouchEnd={() => updateProgress(g, prog)}
                      />
                      <span style={{ fontSize: '0.8rem', color: 'var(--karma)', fontWeight: 700, minWidth: 36 }}>{prog}%</span>
                    </div>

                    {/* Milestones */}
                    <div className="goal-section-label">Milestones</div>
                    <div className="goal-milestones">
                      {g.milestones.map(ms => (
                        <div key={ms.id} className={`goal-milestone ${ms.completed ? 'done' : ''}`}>
                          <button className={`ms-check ${ms.completed ? 'done' : ''}`} onClick={() => toggleMilestone(g.id, ms)}>
                            {ms.completed && <CheckIcon />}
                          </button>
                          <span>{ms.title}</span>
                          {ms.targetDate && <span style={{ fontSize: '0.7rem', color: 'var(--text3)' }}>{ms.targetDate}</span>}
                        </div>
                      ))}
                      <div className="ms-add-form">
                        <input className="karma-input" placeholder="Add milestone…" style={{ fontSize: '0.8rem' }}
                          value={newMilestone[g.id] ?? ''}
                          onChange={e => setNewMilestone(prev => ({ ...prev, [g.id]: e.target.value }))}
                          onKeyDown={e => e.key === 'Enter' && addMilestone(g.id)}
                        />
                        <button className="btn-secondary" style={{ fontSize: '0.75rem', flexShrink: 0 }} onClick={() => addMilestone(g.id)}>Add</button>
                      </div>
                    </div>

                    {/* Linked habits */}
                    {linkedCache[g.id] && linkedCache[g.id].length > 0 && (
                      <>
                        <div className="goal-section-label">Linked Habits</div>
                        <div className="goal-linked-habits">
                          {linkedCache[g.id].map(lh => (
                            <div key={lh.id} className="goal-linked-habit">
                              <span className="glh-emoji">{lh.emoji}</span>
                              <span className="glh-name">{lh.name}</span>
                              <span className="glh-streak">{lh.currentStreak} 🔥</span>
                              <div className="glh-rate-bar"><div className="glh-rate-fill" style={{ width: `${Math.round(lh.last7Rate * 100)}%` }} /></div>
                              <span className="glh-rate">{Math.round(lh.last7Rate * 100)}%</span>
                            </div>
                          ))}
                        </div>
                      </>
                    )}

                    {/* Links */}
                    {g.links.length > 0 && (
                      <>
                        <div className="goal-section-label">Links</div>
                        <div className="goal-links">
                          {g.links.map((l, i) => (
                            <a key={i} href={l.url} target="_blank" rel="noopener noreferrer" className="goal-link">
                              <LinkIcon />{l.label || l.url}
                            </a>
                          ))}
                        </div>
                      </>
                    )}

                    {/* Resources */}
                    {g.resources && (
                      <>
                        <div className="goal-section-label">Resources</div>
                        <div className="goal-resources">{g.resources}</div>
                      </>
                    )}

                    {/* Tags */}
                    {tags.length > 0 && (
                      <div className="goal-tags" style={{ marginTop: '0.75rem' }}>
                        {tags.map(t => <span key={t} className="goal-tag">#{t}</span>)}
                      </div>
                    )}

                    <div className="goal-body-actions">
                      <button className="btn-secondary" style={{ fontSize: '0.75rem' }} onClick={() => deleteGoal(g.id)}>Delete</button>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

// ── Progress Page ─────────────────────────────────────────────
function ProgressPage() {
  const [habits, setHabits] = useState<Habit[]>([]);
  const [logs, setLogs] = useState<Record<string, HabitLog[]>>({});
  const [goals, setGoals] = useState<Goal[]>([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState(false);

  const load = useCallback(async () => {
    try {
      const [hs, gs] = await Promise.all([apiFetch('/api/habits'), apiFetch('/api/goals')]);
      setHabits(hs);
      setGoals(gs);
      const logMap: Record<string, HabitLog[]> = {};
      await Promise.all(hs.map(async (h: Habit) => {
        logMap[h.id] = await apiFetch(`/api/habits/${h.id}/logs?days=84`);
      }));
      setLogs(logMap);
      setErr(false);
    } catch { setErr(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); }, [load]);

  if (!loading && err) return <ApiUnreachable name="Karma" port={5600} mc="var(--karma)" onRetry={() => { setLoading(true); load(); }} />;

  const today = new Date();
  const todayStr = today.toISOString().slice(0, 10);

  // Build 84-day grid (12 weeks) ending today
  const WEEKS = 12;
  const endDay = new Date(today);
  endDay.setHours(0, 0, 0, 0);
  const startDay = new Date(endDay);
  startDay.setDate(startDay.getDate() - (WEEKS * 7 - 1));

  const buildGrid = (habitLogs: HabitLog[]) => {
    const set = new Set(habitLogs.filter(l => l.completed).map(l => l.date));
    const cols: { date: string; done: boolean }[][] = [];
    const d = new Date(startDay);
    // skip to Sunday
    const dayOfWeek = d.getDay();
    if (dayOfWeek !== 0) d.setDate(d.getDate() - dayOfWeek);
    for (let w = 0; w < WEEKS; w++) {
      const col: { date: string; done: boolean }[] = [];
      for (let day = 0; day < 7; day++) {
        const ds = d.toISOString().slice(0, 10);
        const inRange = d >= startDay && d <= endDay;
        col.push({ date: ds, done: inRange && set.has(ds) });
        d.setDate(d.getDate() + 1);
      }
      cols.push(col);
    }
    return cols;
  };

  const activeGoals = goals.filter(g => g.status === 'active');
  const completedGoals = goals.filter(g => g.status === 'completed');

  if (loading) return <div style={{ color: 'var(--text3)', textAlign: 'center', padding: '2rem' }}>Loading…</div>;

  return (
    <div style={style}>
      {/* Goal stats */}
      <div className="goal-stats-grid">
        <div className="goal-stat-card">
          <div className="goal-stat-value">{goals.filter(g => g.status === 'active').length}</div>
          <div className="goal-stat-label">Active Goals</div>
        </div>
        <div className="goal-stat-card">
          <div className="goal-stat-value">{completedGoals.length}</div>
          <div className="goal-stat-label">Completed</div>
        </div>
        <div className="goal-stat-card">
          <div className="goal-stat-value">{habits.length}</div>
          <div className="goal-stat-label">Habits</div>
        </div>
      </div>

      {/* Active goal progress */}
      {activeGoals.length > 0 && (
        <div style={{ marginBottom: '1.75rem' }}>
          <div style={{ fontSize: '0.72rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--text3)', marginBottom: '0.75rem' }}>
            Goal Progress
          </div>
          {activeGoals.map(g => (
            <div key={g.id} style={{ marginBottom: '0.75rem' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.3rem' }}>
                <span style={{ fontSize: '0.82rem', color: 'var(--text2)', fontWeight: 500 }}>{CAT_EMOJI[g.category]} {g.title}</span>
                <span style={{ fontSize: '0.78rem', color: MC, fontWeight: 700 }}>{g.progress}%</span>
              </div>
              <div className="goal-progress-bar-wrap" style={{ height: 6 }}>
                <div className="goal-progress-bar" style={{ width: `${g.progress}%` }} />
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Habit Heatmaps */}
      {habits.length > 0 && (
        <div>
          <div style={{ fontSize: '0.72rem', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.1em', color: 'var(--text3)', marginBottom: '1rem' }}>
            Habit Consistency — Last 12 Weeks
          </div>
          {habits.map(h => {
            const grid = buildGrid(logs[h.id] ?? []);
            const totalDone = (logs[h.id] ?? []).filter(l => l.completed).length;
            return (
              <div key={h.id} className="heatmap-section">
                <div className="heatmap-habit-name">
                  <span>{h.emoji}</span>
                  <span>{h.name}</span>
                  <span style={{ fontSize: '0.7rem', color: 'var(--text3)', marginLeft: 'auto' }}>
                    {totalDone} days · {h.currentStreak} 🔥
                  </span>
                </div>
                <div className="heatmap-weeks">
                  {grid.map((col, wi) => (
                    <div key={wi} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                      {col.map((cell, di) => (
                        <div key={di}
                          className={`heatmap-day ${cell.done ? 'done' : ''} ${cell.date === todayStr ? 'today' : ''}`}
                          title={cell.date}
                        />
                      ))}
                    </div>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {habits.length === 0 && goals.length === 0 && (
        <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--text3)', fontSize: '0.85rem' }}>
          Add habits and goals to see your progress here.
        </div>
      )}
    </div>
  );
}

// ── Root ──────────────────────────────────────────────────────
export default function KarmaModule() {
  const [page, setPage] = useState<Page>('habits');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.id === 'habits' ? '🌱 Habits' : t.id === 'goals' ? '🎯 Goals' : '📊 Progress'}
          </button>
        ))}
      </nav>
      {page === 'habits' && <HabitsPage />}
      {page === 'goals' && <GoalsPage />}
      {page === 'progress' && <ProgressPage />}
    </div>
  );
}

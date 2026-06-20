import { useState } from 'react';
import '../styles/modules.css';

type Page = 'goals' | 'habits' | 'progress' | 'journal';

const TABS: { id: Page; label: string }[] = [
  { id: 'goals',    label: 'Goals' },
  { id: 'habits',   label: 'Habits' },
  { id: 'progress', label: 'Progress' },
  { id: 'journal',  label: 'Journal' },
];

const MC = 'var(--karma)';
const style = { '--mc': MC } as React.CSSProperties;

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M22 11.08V12a10 10 0 11-5.93-9.14"/>
      <polyline points="22 4 12 14.01 9 11.01"/>
    </svg>
  );
}

const GOAL_CATEGORIES = [
  { label: 'Financial', color: 'var(--vault)',     icon: '💰', examples: 'Save $50K, pay off debt, 6-month emergency fund' },
  { label: 'Health',    color: 'var(--vitara)',    icon: '❤️', examples: 'Run 5K, lose 10kg, sleep 8h/night consistently' },
  { label: 'Learning',  color: 'var(--northstar)', icon: '📚', examples: 'Read 24 books, learn Spanish, complete a course' },
  { label: 'Career',    color: 'var(--nexus)',     icon: '🚀', examples: 'Launch a product, get a promotion, build in public' },
  { label: 'Personal',  color: 'var(--san)',       icon: '🌱', examples: 'Meditate daily, call family weekly, travel to 3 countries' },
];

const SAMPLE_HABITS = [
  { label: 'Morning workout', streak: 0, icon: '🏋️' },
  { label: 'Read for 30 minutes', streak: 0, icon: '📖' },
  { label: 'Track spending', streak: 0, icon: '💳' },
  { label: 'Drink 2L water', streak: 0, icon: '💧' },
  { label: 'No social media before 9am', streak: 0, icon: '📵' },
];

function Goals() {
  return (
    <div style={style}>
      <div className="module-empty" style={{ paddingTop: '0.5rem' }}>
        <div className="module-empty-icon"><Icon /></div>
        <h2>Karma</h2>
        <div className="tagline">Goals & Habits</div>
        <p>Define what matters. Build the systems to get there. Karma tracks your life goals and daily habits — with cross-module data to show you how your actions align with your intentions.</p>
      </div>

      <div style={{ maxWidth: 600, margin: '0 auto' }}>
        <h3 style={{ marginBottom: '0.875rem' }}>Goal Categories</h3>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', marginBottom: '2rem' }}>
          {GOAL_CATEGORIES.map(g => (
            <div key={g.label} className="module-feature-item" style={{ '--mc': g.color } as React.CSSProperties}>
              <span className="feat-icon">{g.icon}</span>
              <div>
                <div style={{ fontWeight: 600, color: 'var(--text)', fontSize: '0.84rem', marginBottom: '0.15rem' }}>{g.label}</div>
                <div style={{ color: 'var(--text3)', fontSize: '0.75rem' }}>{g.examples}</div>
              </div>
            </div>
          ))}
        </div>
        <button className="btn-primary" disabled style={{ opacity: 0.5 }}>+ Set a Goal — Coming Soon</button>
      </div>
    </div>
  );
}

function Habits() {
  return (
    <div style={style}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
        <h3 style={{ margin: 0 }}>Daily Habits</h3>
        <button className="btn-primary" disabled style={{ opacity: 0.5, fontSize: '0.78rem' }}>+ Add Habit</button>
      </div>
      {SAMPLE_HABITS.map((h, i) => (
        <div key={i} className="reminder-item" style={{ '--mc': MC } as React.CSSProperties}>
          <span style={{ fontSize: '1.1rem' }}>{h.icon}</span>
          <span className="reminder-text">{h.label}</span>
          <span className="reminder-due">
            <span style={{ color: MC, fontWeight: 600 }}>0</span>
            <span style={{ color: 'var(--text3)' }}> day streak</span>
          </span>
        </div>
      ))}
      <p className="text-dim" style={{ marginTop: '1rem', textAlign: 'center' }}>Habit tracking coming soon — streaks, check-ins, and visualizations</p>
    </div>
  );
}

function EmptyPage({ title, desc }: { title: string; desc: string }) {
  return (
    <div style={style}>
      <div className="module-empty">
        <div className="module-empty-icon"><Icon /></div>
        <h2>{title}</h2>
        <p>{desc}</p>
        <button className="btn-primary" disabled>Coming Soon</button>
      </div>
    </div>
  );
}

export default function KarmaModule() {
  const [page, setPage] = useState<Page>('goals');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'goals'    && <Goals />}
      {page === 'habits'   && <Habits />}
      {page === 'progress' && <EmptyPage title="Progress" desc="Visual progress toward each goal — timelines, milestone tracking, and cross-module data overlaid on your goal charts." />}
      {page === 'journal'  && <EmptyPage title="Daily Journal" desc="Reflect on your day with structured prompts. What went well? What would you change? What are you grateful for?" />}
    </div>
  );
}

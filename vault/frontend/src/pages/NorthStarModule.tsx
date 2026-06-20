import { useState } from 'react';
import '../styles/modules.css';

type Page = 'graph' | 'notes' | 'library' | 'explore';

const TABS: { id: Page; label: string }[] = [
  { id: 'graph',   label: 'Knowledge Graph' },
  { id: 'notes',   label: 'Notes' },
  { id: 'library', label: 'Library' },
  { id: 'explore', label: 'Explore' },
];

const MC = 'var(--northstar)';
const style = { '--mc': MC } as React.CSSProperties;

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10"/>
      <polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/>
    </svg>
  );
}

/* Pre-calculated node positions (center (300,185), radius 125) */
const NODES = [
  { id: 0, x: 300, y: 60,  label: 'Health',      color: '#06c8a0', r: 20 },
  { id: 1, x: 388, y: 97,  label: 'Trading',     color: '#4f9ef8', r: 17 },
  { id: 2, x: 425, y: 185, label: 'Finance',     color: '#1fc87a', r: 20 },
  { id: 3, x: 388, y: 273, label: 'Real Estate', color: '#f0a030', r: 17 },
  { id: 4, x: 300, y: 310, label: 'Goals',       color: '#f472b6', r: 20 },
  { id: 5, x: 212, y: 273, label: 'Learning',    color: '#d4a843', r: 17 },
  { id: 6, x: 175, y: 185, label: 'Work',        color: '#94a3b8', r: 17 },
  { id: 7, x: 212, y: 97,  label: 'Ideas',       color: '#a855f7', r: 20 },
];
const CX = 300, CY = 185;

const INTER_EDGES = [
  [0, 4], [1, 2], [2, 3], [3, 4], [4, 5], [5, 7], [7, 6], [6, 2], [0, 6],
];

/* Label placement offsets */
const LABEL_OFFSET: Record<number, { dx: number; dy: number; anchor: string }> = {
  0: { dx: 0,    dy: -28, anchor: 'middle' },
  1: { dx: 28,   dy: -10, anchor: 'start'  },
  2: { dx: 32,   dy: 4,   anchor: 'start'  },
  3: { dx: 28,   dy: 16,  anchor: 'start'  },
  4: { dx: 0,    dy: 32,  anchor: 'middle' },
  5: { dx: -28,  dy: 16,  anchor: 'end'    },
  6: { dx: -32,  dy: 4,   anchor: 'end'    },
  7: { dx: -28,  dy: -10, anchor: 'end'    },
};

function KnowledgeGraph() {
  const [hoveredNode, setHoveredNode] = useState<number | null>(null);

  return (
    <div style={style}>
      <div className="ns-graph-wrap">
        <svg viewBox="20 35 560 320" className="ns-graph">
          <defs>
            <filter id="glow" x="-50%" y="-50%" width="200%" height="200%">
              <feGaussianBlur stdDeviation="4" result="blur"/>
              <feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge>
            </filter>
            <radialGradient id="center-grad" cx="50%" cy="50%" r="50%">
              <stop offset="0%" stopColor="#d4a843" stopOpacity="0.6"/>
              <stop offset="100%" stopColor="#d4a843" stopOpacity="0.1"/>
            </radialGradient>
          </defs>

          {/* Center → peripheral edges */}
          {NODES.map(n => (
            <line
              key={`c-${n.id}`}
              x1={CX} y1={CY} x2={n.x} y2={n.y}
              stroke="rgba(212,168,67,0.14)"
              strokeWidth="1.5"
              strokeDasharray="5 4"
              className="ns-edge-center"
              style={{ animationDelay: `${n.id * 0.4}s` }}
            />
          ))}

          {/* Inter-node edges */}
          {INTER_EDGES.map(([a, b], i) => (
            <line
              key={`e-${i}`}
              x1={NODES[a].x} y1={NODES[a].y}
              x2={NODES[b].x} y2={NODES[b].y}
              stroke="rgba(255,255,255,0.05)"
              strokeWidth="1"
            />
          ))}

          {/* Peripheral nodes */}
          {NODES.map(n => {
            const lbl = LABEL_OFFSET[n.id];
            const hov = hoveredNode === n.id;
            return (
              <g
                key={n.id}
                transform={`translate(${n.x},${n.y})`}
                style={{ cursor: 'pointer' }}
                onMouseEnter={() => setHoveredNode(n.id)}
                onMouseLeave={() => setHoveredNode(null)}
              >
                {hov && <circle r={n.r + 8} fill={n.color} opacity="0.12" className="ns-pulse-ring"/>}
                <circle r={n.r} fill={n.color + '1a'} stroke={n.color} strokeWidth={hov ? 1.75 : 1} filter={hov ? 'url(#glow)' : undefined}/>
                <circle r={n.r * 0.42} fill={n.color} opacity={hov ? 0.9 : 0.65}/>
                <text
                  x={lbl.dx} y={lbl.dy + 4}
                  textAnchor={lbl.anchor}
                  fill={hov ? n.color : '#4a6280'}
                  fontSize="10"
                  fontWeight={hov ? '600' : '400'}
                  fontFamily="Inter, sans-serif"
                  style={{ transition: 'fill 0.15s' }}
                >
                  {n.label}
                </text>
              </g>
            );
          })}

          {/* Central YOU node */}
          <g transform={`translate(${CX},${CY})`}>
            <circle r="36" fill="url(#center-grad)" opacity="0.4"/>
            <circle r="26" fill="rgba(212,168,67,0.15)" stroke="rgba(212,168,67,0.55)" strokeWidth="1.5"/>
            <circle r="12" fill="rgba(212,168,67,0.5)"/>
            <text y="42" textAnchor="middle" fill="#d4a843" fontSize="10" fontWeight="700" fontFamily="Inter, sans-serif" letterSpacing="0.12em">MAAYA</text>
          </g>
        </svg>
      </div>

      <div className="ns-legend">
        {NODES.map(n => (
          <div key={n.id} className="ns-legend-item">
            <span className="ns-legend-dot" style={{ background: n.color }}/>
            {n.label}
          </div>
        ))}
      </div>

      <div className="card" style={{ marginTop: '0.5rem' }}>
        <h3>About NorthStar</h3>
        <p style={{ fontSize: '0.875rem', color: 'var(--text2)', lineHeight: 1.7, marginTop: '0.5rem' }}>
          NorthStar is your personal knowledge graph — a living neural network of everything you know and care about. Connect ideas, notes, books, and concepts. Discover non-obvious links between your health, finances, goals, and work.
        </p>
      </div>
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
        <div className="module-features">
          {[
            { icon: '🔗', text: 'Bi-directional links between notes and concepts' },
            { icon: '🏷️', text: 'Tags, properties, and custom attributes' },
            { icon: '🔍', text: 'Full-text search across your entire knowledge base' },
            { icon: '📌', text: 'Pin important nodes to the graph view' },
          ].map(f => (
            <div key={f.text} className="module-feature-item" style={style}>
              <span className="feat-icon">{f.icon}</span>
              <span>{f.text}</span>
            </div>
          ))}
        </div>
        <button className="btn-primary" disabled>Coming Soon</button>
      </div>
    </div>
  );
}

export default function NorthStarModule() {
  const [page, setPage] = useState<Page>('graph');
  return (
    <div>
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'graph'   && <KnowledgeGraph />}
      {page === 'notes'   && <EmptyPage title="Notes" desc="Capture ideas, insights, and knowledge. Connect them to modules, goals, and each other." />}
      {page === 'library' && <EmptyPage title="Library" desc="Books you've read, courses you've taken, and resources you've saved — with key takeaways." />}
      {page === 'explore' && <EmptyPage title="Explore" desc="Discover connections between your notes, find knowledge gaps, and get AI-suggested links." />}
    </div>
  );
}

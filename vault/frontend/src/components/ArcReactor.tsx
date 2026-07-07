export default function ArcReactor({ size = 160 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 200 200" fill="none" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <radialGradient id="core-glow" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#7df3ff" stopOpacity="0.9"/>
          <stop offset="40%" stopColor="#4ac8db" stopOpacity="0.4"/>
          <stop offset="100%" stopColor="#1a8a9e" stopOpacity="0"/>
        </radialGradient>
        <radialGradient id="core-center" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#ffffff" stopOpacity="0.9"/>
          <stop offset="50%" stopColor="#7df3ff" stopOpacity="0.6"/>
          <stop offset="100%" stopColor="#4ac8db" stopOpacity="0"/>
        </radialGradient>
      </defs>

      {/* Ambient glow */}
      <circle cx="100" cy="100" r="85" fill="url(#core-glow)" opacity="0.25"/>

      {/* Ring 1 — outermost, slow spin, segmented */}
      <g className="arc-ring-outer">
        <circle cx="100" cy="100" r="90" stroke="#4ac8db" strokeWidth="0.5" strokeOpacity="0.15" fill="none"/>
        {[0,30,60,90,120,150,180,210,240,270,300,330].map(deg => {
          const r = (deg * Math.PI) / 180;
          const x1 = 100 + 86 * Math.cos(r), y1 = 100 + 86 * Math.sin(r);
          const x2 = 100 + 92 * Math.cos(r), y2 = 100 + 92 * Math.sin(r);
          return <line key={deg} x1={x1} y1={y1} x2={x2} y2={y2} stroke="#4ac8db" strokeWidth={deg % 90 === 0 ? 2 : 1} strokeOpacity={deg % 90 === 0 ? 0.8 : 0.35}/>;
        })}
        <circle cx="100" cy="100" r="90" stroke="#4ac8db" strokeWidth="1.5" strokeOpacity="0.4" fill="none"
          strokeDasharray="12 6 4 8 20 10" strokeLinecap="round"/>
      </g>

      {/* Ring 2 — counter spin, thick arcs */}
      <g className="arc-ring-mid">
        <circle cx="100" cy="100" r="74" stroke="#4ac8db" strokeWidth="0.5" strokeOpacity="0.12" fill="none"/>
        <path d="M 100 26 A 74 74 0 0 1 174 100" stroke="#7df3ff" strokeWidth="3" strokeOpacity="0.55" fill="none" strokeLinecap="round"/>
        <path d="M 100 174 A 74 74 0 0 1 26 100" stroke="#7df3ff" strokeWidth="3" strokeOpacity="0.55" fill="none" strokeLinecap="round"/>
        <path d="M 152.3 47.7 A 74 74 0 0 1 174 100" stroke="#4ac8db" strokeWidth="1.5" strokeOpacity="0.25" fill="none" strokeLinecap="round"/>
        <path d="M 47.7 152.3 A 74 74 0 0 1 26 100" stroke="#4ac8db" strokeWidth="1.5" strokeOpacity="0.25" fill="none" strokeLinecap="round"/>
        {[0,90,180,270].map(deg => {
          const r = (deg * Math.PI) / 180;
          return <circle key={deg} cx={100 + 74 * Math.cos(r)} cy={100 + 74 * Math.sin(r)} r="2.5" fill="#7df3ff" fillOpacity="0.65"/>;
        })}
      </g>

      {/* Ring 3 — fast spin, triangle markers */}
      <g className="arc-ring-inner">
        <circle cx="100" cy="100" r="56" stroke="#4ac8db" strokeWidth="1" strokeOpacity="0.18" fill="none"/>
        <circle cx="100" cy="100" r="56" stroke="#7df3ff" strokeWidth="2" strokeOpacity="0.45" fill="none"
          strokeDasharray="6 10 16 8 4 12" strokeLinecap="round"/>
        {[0,120,240].map(deg => {
          const r = (deg * Math.PI) / 180;
          const cx = 100 + 56 * Math.cos(r), cy = 100 + 56 * Math.sin(r);
          const s = 5;
          const p = `${cx},${cy - s} ${cx + s * 0.866},${cy + s * 0.5} ${cx - s * 0.866},${cy + s * 0.5}`;
          return <polygon key={deg} points={p} fill="#7df3ff" fillOpacity="0.55"/>;
        })}
      </g>

      {/* Ring 4 — innermost halo */}
      <g className="arc-ring-inner2">
        <circle cx="100" cy="100" r="38" stroke="#7df3ff" strokeWidth="1.5" strokeOpacity="0.3" fill="none"
          strokeDasharray="8 4"/>
      </g>

      {/* Reactor core — palladium triangle + center */}
      <g className="arc-core">
        <circle cx="100" cy="100" r="26" fill="url(#core-glow)" opacity="0.45"/>
        <circle cx="100" cy="100" r="22" stroke="#7df3ff" strokeWidth="1.5" strokeOpacity="0.45" fill="none"/>
        <polygon points="100,80 117.3,110 82.7,110" stroke="#7df3ff" strokeWidth="1.5" strokeOpacity="0.5" fill="none"/>
        <polygon points="100,86 113,107 87,107" fill="#4ac8db" fillOpacity="0.12"/>
        {[0,120,240].map(deg => {
          const r1 = ((deg - 15) * Math.PI) / 180, r2 = ((deg + 15) * Math.PI) / 180;
          return <line key={deg}
            x1={100 + 30 * Math.cos(r1)} y1={100 + 30 * Math.sin(r1)}
            x2={100 + 30 * Math.cos(r2)} y2={100 + 30 * Math.sin(r2)}
            stroke="#7df3ff" strokeWidth="2" strokeOpacity="0.35" strokeLinecap="round"/>;
        })}
        <circle cx="100" cy="100" r="8" fill="url(#core-center)"/>
        <circle cx="100" cy="100" r="3" fill="#ffffff" fillOpacity="0.85"/>
      </g>
    </svg>
  );
}

export function HudCorners() {
  const corner = (
    <svg width="56" height="56" viewBox="0 0 56 56" fill="none">
      <path d="M2 20 L2 2 L20 2" stroke="#4ac8db" strokeWidth="1.5" strokeOpacity="0.4" strokeLinecap="round" fill="none"/>
      <circle cx="2" cy="2" r="2" fill="#4ac8db" fillOpacity="0.5"/>
      <path d="M2 2 L10 10" stroke="#4ac8db" strokeWidth="0.5" strokeOpacity="0.2" fill="none"/>
    </svg>
  );
  return (
    <div className="hud-corners">
      <div className="hud-corner">{corner}</div>
      <div className="hud-corner">{corner}</div>
      <div className="hud-corner">{corner}</div>
      <div className="hud-corner">{corner}</div>
    </div>
  );
}

export function HudStatus({ text }: { text: string }) {
  return (
    <div className="hud-status">
      <span className="hud-status-dot"/>
      <span>{text}</span>
    </div>
  );
}

export function HudScanlines() {
  return (
    <div className="hud-scanlines">
      <div className="hud-data-left"/>
      <div className="hud-data-right"/>
    </div>
  );
}

import { useRef, useEffect, useState, useCallback } from 'react';

interface GraphNode {
  id: string;
  label: string;
  type: 'module' | 'topic' | 'entry' | 'action' | 'fact';
  color: string;
  x: number;
  y: number;
  vx: number;
  vy: number;
  r: number;
  meta?: string;
}

interface GraphEdge {
  source: string;
  target: string;
}

interface KnowledgeGraphProps {
  modules: { name: string; healthy: boolean }[];
  knowledge: { source: string; topic: string; summary: string }[];
  actions: { source: string; title: string; priority: number }[];
  facts: Record<string, string>;
}

const MODULE_COLORS: Record<string, string> = {
  vault: '#1fc87a', vitara: '#06c8a0', aasthi: '#f0a030',
  san: '#a855f7', sutra: '#4f9ef8', manual: '#94a3b8',
};
const TOPIC_COLOR = '#d4a843';
const ACTION_COLOR = '#ef4444';
const FACT_COLOR = '#60a5fa';

export default function KnowledgeGraph({ modules, knowledge, actions, facts }: KnowledgeGraphProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const nodesRef = useRef<GraphNode[]>([]);
  const edgesRef = useRef<GraphEdge[]>([]);
  const animRef = useRef<number>(0);
  const dragRef = useRef<{ id: string; offX: number; offY: number } | null>(null);
  const hoverRef = useRef<string | null>(null);
  const [tooltip, setTooltip] = useState<{ x: number; y: number; text: string } | null>(null);
  const [dimensions, setDimensions] = useState({ w: 700, h: 450 });

  const buildGraph = useCallback(() => {
    const nodes: GraphNode[] = [];
    const edges: GraphEdge[] = [];
    const cx = dimensions.w / 2, cy = dimensions.h / 2;

    const coreId = 'core';
    nodes.push({ id: coreId, label: 'NorthStar', type: 'module', color: '#d4a843', x: cx, y: cy, vx: 0, vy: 0, r: 28, meta: 'Knowledge Brain' });

    const modNames = ['vault', 'vitara', 'aasthi', 'san', 'sutra'];
    modNames.forEach((name, i) => {
      const angle = (i / modNames.length) * Math.PI * 2 - Math.PI / 2;
      const dist = 140;
      const mod = modules.find(m => m.name === name);
      nodes.push({
        id: `mod-${name}`, label: name.charAt(0).toUpperCase() + name.slice(1),
        type: 'module', color: MODULE_COLORS[name] || '#94a3b8',
        x: cx + Math.cos(angle) * dist + (Math.random() - 0.5) * 20,
        y: cy + Math.sin(angle) * dist + (Math.random() - 0.5) * 20,
        vx: 0, vy: 0, r: 20,
        meta: mod ? (mod.healthy ? 'Online' : 'Error') : 'No data',
      });
      edges.push({ source: coreId, target: `mod-${name}` });
    });

    const topicCounts: Record<string, { count: number; sources: Set<string> }> = {};
    knowledge.forEach(k => {
      if (!topicCounts[k.topic]) topicCounts[k.topic] = { count: 0, sources: new Set() };
      topicCounts[k.topic].count++;
      topicCounts[k.topic].sources.add(k.source);
    });

    Object.entries(topicCounts).forEach(([topic, data], i) => {
      const angle = (i / Object.keys(topicCounts).length) * Math.PI * 2;
      const dist = 220 + Math.random() * 40;
      const id = `topic-${topic}`;
      nodes.push({
        id, label: topic, type: 'topic', color: TOPIC_COLOR,
        x: cx + Math.cos(angle) * dist, y: cy + Math.sin(angle) * dist,
        vx: 0, vy: 0, r: 8 + Math.min(data.count, 10) * 1.5,
        meta: `${data.count} entries`,
      });
      data.sources.forEach(src => {
        edges.push({ source: `mod-${src}`, target: id });
      });
    });

    actions.slice(0, 8).forEach((a, i) => {
      const angle = Math.PI + (i / 8) * Math.PI;
      const dist = 180 + Math.random() * 60;
      const id = `action-${i}`;
      nodes.push({
        id, label: a.title.length > 20 ? a.title.slice(0, 18) + '..' : a.title,
        type: 'action', color: a.priority <= 2 ? ACTION_COLOR : '#f59e0b',
        x: cx + Math.cos(angle) * dist, y: cy + Math.sin(angle) * dist,
        vx: 0, vy: 0, r: 7,
        meta: `P${a.priority} · ${a.source}`,
      });
      const srcNode = `mod-${a.source}`;
      if (nodes.find(n => n.id === srcNode)) edges.push({ source: srcNode, target: id });
    });

    const factEntries = Object.entries(facts);
    factEntries.slice(0, 6).forEach(([key, val], i) => {
      const angle = (i / factEntries.length) * Math.PI * 2 + 0.3;
      const dist = 80 + Math.random() * 30;
      const id = `fact-${key}`;
      nodes.push({
        id, label: key, type: 'fact', color: FACT_COLOR,
        x: cx + Math.cos(angle) * dist, y: cy + Math.sin(angle) * dist,
        vx: 0, vy: 0, r: 6,
        meta: val,
      });
      edges.push({ source: coreId, target: id });
    });

    nodesRef.current = nodes;
    edgesRef.current = edges;
  }, [modules, knowledge, actions, facts, dimensions]);

  useEffect(() => { buildGraph(); }, [buildGraph]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const simulate = () => {
      const nodes = nodesRef.current;
      const edges = edgesRef.current;
      const cx = dimensions.w / 2, cy = dimensions.h / 2;

      nodes.forEach(n => {
        if (dragRef.current?.id === n.id) return;
        const dx = cx - n.x, dy = cy - n.y;
        const dist = Math.sqrt(dx * dx + dy * dy);
        if (dist > 1) {
          n.vx += dx * 0.0003;
          n.vy += dy * 0.0003;
        }
      });

      edges.forEach(e => {
        const a = nodes.find(n => n.id === e.source);
        const b = nodes.find(n => n.id === e.target);
        if (!a || !b) return;
        const dx = b.x - a.x, dy = b.y - a.y;
        const dist = Math.sqrt(dx * dx + dy * dy) || 1;
        const targetDist = a.r + b.r + 60;
        const force = (dist - targetDist) * 0.002;
        const fx = (dx / dist) * force, fy = (dy / dist) * force;
        if (dragRef.current?.id !== a.id) { a.vx += fx; a.vy += fy; }
        if (dragRef.current?.id !== b.id) { b.vx -= fx; b.vy -= fy; }
      });

      for (let i = 0; i < nodes.length; i++) {
        for (let j = i + 1; j < nodes.length; j++) {
          const a = nodes[i], b = nodes[j];
          const dx = b.x - a.x, dy = b.y - a.y;
          const dist = Math.sqrt(dx * dx + dy * dy) || 1;
          const minDist = a.r + b.r + 15;
          if (dist < minDist) {
            const force = (minDist - dist) * 0.01;
            const fx = (dx / dist) * force, fy = (dy / dist) * force;
            if (dragRef.current?.id !== a.id) { a.vx -= fx; a.vy -= fy; }
            if (dragRef.current?.id !== b.id) { b.vx += fx; b.vy += fy; }
          }
        }
      }

      nodes.forEach(n => {
        if (dragRef.current?.id === n.id) return;
        n.vx *= 0.85;
        n.vy *= 0.85;
        n.x += n.vx;
        n.y += n.vy;
        n.x = Math.max(n.r + 5, Math.min(dimensions.w - n.r - 5, n.x));
        n.y = Math.max(n.r + 5, Math.min(dimensions.h - n.r - 5, n.y));
      });

      ctx.clearRect(0, 0, dimensions.w, dimensions.h);

      edges.forEach(e => {
        const a = nodes.find(n => n.id === e.source);
        const b = nodes.find(n => n.id === e.target);
        if (!a || !b) return;
        ctx.beginPath();
        ctx.moveTo(a.x, a.y);
        ctx.lineTo(b.x, b.y);
        const isHover = hoverRef.current === a.id || hoverRef.current === b.id;
        ctx.strokeStyle = isHover ? 'rgba(212,168,67,0.35)' : 'rgba(74,200,219,0.1)';
        ctx.lineWidth = isHover ? 1.5 : 0.8;
        ctx.stroke();
      });

      nodes.forEach(n => {
        const isHover = hoverRef.current === n.id;

        if (isHover) {
          ctx.beginPath();
          ctx.arc(n.x, n.y, n.r + 6, 0, Math.PI * 2);
          ctx.strokeStyle = n.color + '40';
          ctx.lineWidth = 1;
          ctx.stroke();
        }

        ctx.beginPath();
        ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2);
        ctx.fillStyle = isHover ? n.color : n.color + 'CC';
        ctx.fill();

        if (n.type === 'module') {
          ctx.beginPath();
          ctx.arc(n.x, n.y, n.r + 2, 0, Math.PI * 2);
          ctx.strokeStyle = n.color + '50';
          ctx.lineWidth = 1.5;
          ctx.stroke();
        }

        if (n.r >= 12) {
          ctx.fillStyle = '#ffffff';
          ctx.font = `${n.r >= 20 ? 'bold ' : ''}${Math.min(n.r * 0.6, 11)}px 'Space Grotesk', sans-serif`;
          ctx.textAlign = 'center';
          ctx.textBaseline = 'middle';
          const displayLabel = n.label.length > 10 ? n.label.slice(0, 8) + '..' : n.label;
          ctx.fillText(displayLabel, n.x, n.y);
        }

        if (n.r < 12 || isHover) {
          ctx.fillStyle = isHover ? '#dce8ff' : '#7a96c0';
          ctx.font = `${isHover ? '600 ' : ''}10px 'Space Grotesk', sans-serif`;
          ctx.textAlign = 'center';
          ctx.fillText(n.label, n.x, n.y + n.r + 12);
        }
      });

      animRef.current = requestAnimationFrame(simulate);
    };

    animRef.current = requestAnimationFrame(simulate);
    return () => cancelAnimationFrame(animRef.current);
  }, [dimensions]);

  const getNode = (e: React.MouseEvent) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return null;
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;
    return nodesRef.current.find(n => {
      const dx = n.x - mx, dy = n.y - my;
      return dx * dx + dy * dy <= (n.r + 4) * (n.r + 4);
    }) || null;
  };

  const onMouseDown = (e: React.MouseEvent) => {
    const node = getNode(e);
    if (!node) return;
    const rect = canvasRef.current!.getBoundingClientRect();
    dragRef.current = { id: node.id, offX: e.clientX - rect.left - node.x, offY: e.clientY - rect.top - node.y };
  };

  const onMouseMove = (e: React.MouseEvent) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;
    const mx = e.clientX - rect.left, my = e.clientY - rect.top;

    if (dragRef.current) {
      const node = nodesRef.current.find(n => n.id === dragRef.current!.id);
      if (node) {
        node.x = mx - dragRef.current.offX;
        node.y = my - dragRef.current.offY;
        node.vx = 0;
        node.vy = 0;
      }
      return;
    }

    const node = getNode(e);
    hoverRef.current = node?.id || null;
    if (node) {
      setTooltip({ x: mx, y: my - 20, text: `${node.label}${node.meta ? ' — ' + node.meta : ''}` });
    } else {
      setTooltip(null);
    }
  };

  const onMouseUp = () => { dragRef.current = null; };

  useEffect(() => {
    const obs = new ResizeObserver(entries => {
      const { width } = entries[0].contentRect;
      setDimensions({ w: width, h: Math.max(350, Math.min(width * 0.6, 500)) });
    });
    const parent = canvasRef.current?.parentElement;
    if (parent) obs.observe(parent);
    return () => obs.disconnect();
  }, []);

  return (
    <div className="ns-graph-wrap">
      <canvas
        ref={canvasRef}
        width={dimensions.w}
        height={dimensions.h}
        className="ns-graph-canvas"
        onMouseDown={onMouseDown}
        onMouseMove={onMouseMove}
        onMouseUp={onMouseUp}
        onMouseLeave={() => { dragRef.current = null; hoverRef.current = null; setTooltip(null); }}
      />
      {tooltip && (
        <div className="ns-graph-tooltip" style={{ left: tooltip.x, top: tooltip.y }}>
          {tooltip.text}
        </div>
      )}
      <div className="ns-graph-legend">
        <span><span className="ns-legend-dot" style={{ background: '#d4a843' }}/>Core</span>
        <span><span className="ns-legend-dot" style={{ background: '#1fc87a' }}/>Modules</span>
        <span><span className="ns-legend-dot" style={{ background: TOPIC_COLOR }}/>Topics</span>
        <span><span className="ns-legend-dot" style={{ background: ACTION_COLOR }}/>Actions</span>
        <span><span className="ns-legend-dot" style={{ background: FACT_COLOR }}/>Facts</span>
      </div>
    </div>
  );
}

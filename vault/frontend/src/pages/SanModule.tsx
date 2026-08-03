import { useState, useRef, useEffect } from 'react';
import { QueryClientProvider, useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import { useTimezone, formatInTz, localInputToUtcIso, utcIsoToLocalInput } from '../services/timezone';
import { getVoiceStatus, startRecording, speak, stopSpeaking, type Recorder, type VoiceStatus } from '../services/voice';
import { useVoiceCallContext } from '../services/voiceCallContext';
import '../styles/modules.css';
import '../styles/san.css';

const API = moduleApi(5300);
const MC = 'var(--san)';
const style = { '--mc': MC } as React.CSSProperties;

const qc = makeModuleQueryClient(30_000);

const get = (url: string) => fetch(url, { headers: authHeaders() }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.json(); });
const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: { ...authHeaders(), ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.status === 204 ? null : r.json(); });

/* ── Types ── */
interface ChatMsg { id: string; role: 'user' | 'assistant'; content: string; createdAt: string }
interface Reminder { id: string; text: string; dueAt: string; done: boolean; notifyTelegram: boolean; notifiedAt: string | null; createdAt: string }
interface AlertItem {
  id: string; type: string; title: string; description: string;
  thresholdValue: number | null; triggerAt: string | null; active: boolean;
  notifyTelegram: boolean; triggeredAt: string | null; createdAt: string;
}
interface FeedEntry { module: string; title: string; description: string; occurredAt: string }
interface ModuleStatus { module: string; reachable: boolean; error: string | null }
interface FeedResult { entries: FeedEntry[]; modules: ModuleStatus[] }
interface CalendarEvent {
  id: string; title: string; description?: string; startTime: string; endTime: string;
  location?: string; source: string; calendarName?: string; allDay: boolean;
}
interface NowNextResult { current?: CalendarEvent; upcoming: CalendarEvent[]; asOf: string }
interface ContextResult { location?: { latitude: number; longitude: number; address?: string; timestamp: string }; recentActivity: unknown[] }
interface EmailAccount { id: string; provider: 'google' | 'microsoft'; emailAddress: string; active: boolean; lastCheckedAt: string | null; createdAt: string }

// Always formats/parses in the system-wide configured timezone (see
// services/timezone.ts) — NOT the viewing device's own clock, so a reminder
// means the same real-world time regardless of which browser created it.
const fmtDateTime = (d: string | null) => formatInTz(d);
const toLocalInputValue = (iso: string) => utcIsoToLocalInput(iso);

const ALERT_TYPES = [
  { id: 'spending_threshold', label: 'Spending Threshold' },
  { id: 'goal_deadline', label: 'Goal Deadline' },
  { id: 'document_expiry', label: 'Document Expiry' },
  { id: 'custom', label: 'Custom' },
];
const ALERT_TYPE_COLOR: Record<string, string> = {
  spending_threshold: '#e84444', goal_deadline: '#1fc87a', document_expiry: '#d4a843', custom: '#7a96c0',
};
const MODULE_COLOR: Record<string, string> = { Vault: '#4f9ef8', Vitara: '#e8527c', Aasthi: '#d4a843' };

type Page = 'assistant' | 'reminders' | 'alerts' | 'feed' | 'calendar' | 'people' | 'email';
const TABS: { id: Page; label: string }[] = [
  { id: 'assistant', label: 'Assistant' },
  { id: 'reminders', label: 'Reminders' },
  { id: 'alerts',    label: 'Alerts' },
  { id: 'feed',      label: 'Activity Feed' },
  { id: 'calendar',  label: 'Calendar' },
  { id: 'people',    label: 'People' },
  { id: 'email',     label: 'Email' },
];

const EMAIL_PROVIDERS: { id: 'google' | 'microsoft'; label: string; icon: string }[] = [
  { id: 'google', label: 'Gmail', icon: '✉️' },
  { id: 'microsoft', label: 'Outlook', icon: '📧' },
];

function Icon() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/>
    </svg>
  );
}
function SendIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>
    </svg>
  );
}

function ApiError({ port }: { port: number }) {
  return (
    <div className="module-empty" style={style}>
      <div className="module-empty-icon"><Icon /></div>
      <h2>Can't reach San API</h2>
      <p>Make sure the San backend is running on port {port} (<code>san/start.ps1</code>).</p>
    </div>
  );
}

/* ── System Prompt Editor (separate window) ── */
interface SystemPromptDto { prompt: string; isDefault: boolean; defaultPrompt: string }

// One-click personality presets. Each REPLACES only the persona paragraph — the
// live module snapshot, memory, and time context are appended by the backend
// regardless, so switching personality never loses San's actual capabilities.
const PERSONALITIES: { label: string; prompt: string }[] = [
  {
    label: '🎩 JARVIS',
    prompt:
      'You are San, the AI majordomo of Maaya OS — in the mold of JARVIS: unflappable, dryly witty, quietly ' +
      'brilliant. Address the user with polished, understated courtesy ("sir" sparingly, never obsequious). ' +
      'Deliver real numbers from their modules with effortless precision, anticipate the follow-up question, ' +
      'and permit yourself one wry aside when the moment invites it. Your long-term memory is NorthStar — what ' +
      'you remember about the user is surfaced below; treat it as things you genuinely know. Use your tools to ' +
      'actually do what is asked; confirm crisply once done. If a module is unreachable, report it plainly.',
  },
  {
    label: '⚡ Concise',
    prompt:
      'You are San, the assistant inside Maaya OS. Be maximally concise: answer first, no filler, no preamble, ' +
      'bullets over paragraphs. Use real numbers from the module snapshot below. Your long-term memory is ' +
      'NorthStar — treat remembered facts as things you know. Use your tools to take the actions asked of you, ' +
      'confirm in one short sentence. If a module is unreachable, say so in five words or fewer.',
  },
  {
    label: '🏋️ Coach',
    prompt:
      'You are San, the user\'s personal coach inside Maaya OS. Be direct, energetic, and accountable — track ' +
      'their health scores, habits, goals, and spending like a coach reads game film: name what improved, what ' +
      'slipped, and the single next action. Push them, kindly. Ground every claim in the real numbers from the ' +
      'module snapshot below, and use NorthStar memories to connect today to their longer arc. Use your tools ' +
      'to log, schedule, and remind without being asked twice.',
  },
  {
    label: '🧘 Warm',
    prompt:
      'You are San, the personal life-assistant inside Maaya OS — warm, calm, and genuinely attentive. Speak ' +
      'like a trusted friend who happens to have perfect recall: reference what you remember (NorthStar ' +
      'memories below) naturally, notice patterns gently, and never lecture. Use the real numbers from the ' +
      'module snapshot when they help. Use your tools to quietly take care of things the user asks for, and ' +
      'confirm softly. If a module is unreachable, mention it without drama.',
  },
];

// Shared by the chat system prompt and the email-triage prompt — both are just a
// stored instruction with a default to fall back to, so they get one editor.
function PromptEditor({ title, hint, endpoint, queryKey, presets, onClose }: {
  title: string;
  hint: string;
  endpoint: string;
  queryKey: string;
  presets?: { label: string; prompt: string }[];
  onClose: () => void;
}) {
  const queryClient = useQueryClient();
  const [text, setText] = useState<string | null>(null);
  const promptQ = useQuery<SystemPromptDto>({ queryKey: [queryKey], queryFn: () => get(`${API}${endpoint}`) });

  useEffect(() => { if (promptQ.data && text === null) setText(promptQ.data.prompt); }, [promptQ.data, text]);

  const saveMut = useMutation({
    mutationFn: (prompt: string) => send(`${API}${endpoint}`, 'PUT', { prompt }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: [queryKey] }); onClose(); },
  });

  return (
    <div className="sp-overlay" onClick={onClose}>
      <div className="sp-modal" onClick={e => e.stopPropagation()}>
        <div className="sp-head">
          <h3>{title}</h3>
          <button className="sp-x" onClick={onClose}>✕</button>
        </div>
        <p className="sp-hint">{hint}</p>
        <div className="sp-presets">
          {(presets ?? []).map(p => (
            <button key={p.label} className="sp-preset" onClick={() => setText(p.prompt)} title="Replaces the prompt below — review, then Save">
              {p.label}
            </button>
          ))}
        </div>
        {promptQ.isLoading || text === null ? (
          <div className="sp-body" style={{ color: 'var(--text3)' }}>Loading…</div>
        ) : (
          <textarea
            className="sp-body"
            value={text}
            onChange={e => setText(e.target.value)}
            placeholder="Enter the system prompt San should use…"
            spellCheck={false}
          />
        )}
        <div className="sp-foot">
          <button
            className="btn-ghost"
            onClick={() => promptQ.data && setText(promptQ.data.defaultPrompt)}
            disabled={!promptQ.data}
          >Reset to default</button>
          <div style={{ flex: 1 }} />
          <button className="btn-ghost" onClick={onClose}>Cancel</button>
          <button
            className="btn-primary"
            onClick={() => text !== null && saveMut.mutate(text)}
            disabled={text === null || saveMut.isPending}
          >{saveMut.isPending ? 'Saving…' : 'Save'}</button>
        </div>
        {saveMut.isError && <div className="sp-err">Couldn't save — is San (5300) running?</div>}
      </div>
    </div>
  );
}

function SystemPromptEditor({ onClose }: { onClose: () => void }) {
  return (
    <PromptEditor
      title="San — System Prompt"
      hint="This is the base instruction San runs with. A live snapshot of your modules is appended automatically after it."
      endpoint="/api/chat/system-prompt"
      queryKey="san-system-prompt"
      presets={PERSONALITIES}
      onClose={onClose}
    />
  );
}

function AuditPromptEditor({ onClose }: { onClose: () => void }) {
  return (
    <PromptEditor
      title="San — Audit Rules"
      hint="Every 15 minutes San reviews your whole system on its own and can act on what it finds. This is what it looks for. It's shown its previous findings each run so it won't repeat itself — keep the NOTHING_IMPORTANT instruction intact, that's what keeps it silent when all is well."
      endpoint="/api/audit/prompt"
      queryKey="san-audit-prompt"
      onClose={onClose}
    />
  );
}

function TriagePromptEditor({ onClose }: { onClose: () => void }) {
  return (
    <PromptEditor
      title="Email — Triage Rules"
      hint="San decides entirely from this what counts as important, what to ignore, and when to create a reminder, alert, calendar event, or property task. Keep the NOTHING_IMPORTANT instruction intact — it's what keeps a quiet inbox quiet."
      endpoint="/api/email/triage-prompt"
      queryKey="san-triage-prompt"
      onClose={onClose}
    />
  );
}

/* ── Assistant ── */
function Assistant() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState('');
  const [showPrompt, setShowPrompt] = useState(false);
  const [showAudit, setShowAudit] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  // Voice: mic (STT) + speaker (TTS). Buttons only appear if the respective
  // service is configured on the backend (see /api/voice/status).
  const [voice, setVoice] = useState<VoiceStatus>({ sttReady: false, ttsReady: false });
  const [recording, setRecording] = useState(false);
  const [transcribing, setTranscribing] = useState(false);
  const [voiceErr, setVoiceErr] = useState<string | null>(null);
  const [ttsOn, setTtsOn] = useState(() => localStorage.getItem('san_tts_on') === '1');
  const recorderRef = useRef<Recorder | null>(null);
  const lastSpokenId = useRef<string | null>(null);

  // Hands-free call mode now lives at the App root (services/voiceCallContext.tsx)
  // so it's reachable — and survives — switching away from this tab. This page
  // only needs to know whether one is active, to silence its own auto-speak.
  const { inCall, startCall } = useVoiceCallContext();

  useEffect(() => { getVoiceStatus().then(setVoice); }, []);

  const messagesQ = useQuery<ChatMsg[]>({ queryKey: ['san-messages'], queryFn: () => get(`${API}/api/chat/messages`) });
  const sendMut = useMutation({
    mutationFn: (content: string) => send(`${API}/api/chat/messages`, 'POST', { content }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['san-messages'] }),
  });
  const clearMut = useMutation({
    mutationFn: () => send(`${API}/api/chat/messages`, 'DELETE'),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['san-messages'] }),
  });

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight });
  }, [messagesQ.data, sendMut.isPending]);

  // Speak newly-arrived assistant replies when TTS is on. Guards against
  // re-speaking on every re-render by tracking the last spoken message id.
  useEffect(() => {
    if (!ttsOn || !voice.ttsReady) return;
    const msgs = messagesQ.data ?? [];
    const last = msgs[msgs.length - 1];
    // In a call the session speaks replies itself — mark them seen so hanging
    // up doesn't make this effect suddenly re-speak the last one.
    if (inCall) { if (last) lastSpokenId.current = last.id; return; }
    if (last && last.role === 'assistant' && last.id !== lastSpokenId.current) {
      lastSpokenId.current = last.id;
      speak(last.content).catch(e => setVoiceErr(e.message));
    }
  }, [messagesQ.data, ttsOn, voice.ttsReady, inCall]);

  const toggleTts = () => {
    const next = !ttsOn;
    setTtsOn(next);
    localStorage.setItem('san_tts_on', next ? '1' : '0');
    if (!next) stopSpeaking();
  };

  const toggleMic = async () => {
    setVoiceErr(null);
    if (recording) {
      // The mic itself stops instantly on click — flip the button back right
      // away and show a separate "transcribing" state for the Whisper
      // round-trip, instead of leaving the red stop button stuck for it.
      const recorder = recorderRef.current;
      recorderRef.current = null;
      setRecording(false);
      setTranscribing(true);
      try {
        const text = await recorder!.stop();
        if (text) sendMut.mutate(text); // dictate → send straight to San
      } catch (e) { setVoiceErr((e as Error).message); }
      finally { setTranscribing(false); }
    } else {
      try {
        recorderRef.current = await startRecording();
        setRecording(true);
      } catch (e) { setVoiceErr('Microphone unavailable: ' + (e as Error).message); }
    }
  };

  if (messagesQ.isError) return <ApiError port={5300} />;

  const messages = messagesQ.data ?? [];
  const submit = () => {
    const text = draft.trim();
    if (!text || sendMut.isPending) return;
    setDraft('');
    sendMut.mutate(text);
  };

  return (
    <div style={style}>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.5rem', marginBottom: '0.6rem' }}>
        <button
          className="btn-ghost"
          disabled={clearMut.isPending || messages.length === 0}
          onClick={() => { if (confirm('Clear San\'s entire chat history? This cannot be undone.')) clearMut.mutate(); }}
        >
          🗑 Clear Chat
        </button>
        <button className="btn-ghost" onClick={() => setShowPrompt(true)}>⚙ Edit System Prompt</button>
        <button className="btn-ghost" onClick={() => setShowAudit(true)}>🔎 Audit Rules</button>
      </div>
      {showPrompt && <SystemPromptEditor onClose={() => setShowPrompt(false)} />}
      {showAudit && <AuditPromptEditor onClose={() => setShowAudit(false)} />}
      <div className="chat-shell" style={{ marginBottom: '1.5rem' }}>
        <div className="chat-messages" ref={scrollRef}>
          {messages.length === 0 && !messagesQ.isLoading && (
            <div className="chat-bubble">
              <div className="chat-avatar">S</div>
              <div className="chat-text">
                Hello! I'm San. Ask me anything about your finances, health, or properties — I can see live data
                across Vault, Vitara, and Aasthi.
              </div>
            </div>
          )}
          {messages.map(m => (
            <div key={m.id} className="chat-bubble">
              <div className="chat-avatar">{m.role === 'user' ? 'U' : 'S'}</div>
              <div className="chat-text">{m.content}</div>
            </div>
          ))}
          {sendMut.isPending && (
            <>
              {/* The real message list only refetches once the whole round trip
                  (save → LLM call → save reply) completes server-side, so without
                  this, the user's own message would be invisible for the entire
                  wait — echo it immediately from the mutation's own variables. */}
              <div className="chat-bubble">
                <div className="chat-avatar">U</div>
                <div className="chat-text">{sendMut.variables}</div>
              </div>
              <div className="chat-bubble">
                <div className="chat-avatar">S</div>
                <div className="chat-text" style={{ color: 'var(--text3)', fontStyle: 'italic' }}>San is thinking…</div>
              </div>
            </>
          )}
        </div>
        <div className="chat-bar">
          {voice.ttsReady && (
            <button
              className="chat-voice-btn"
              onClick={toggleTts}
              title={ttsOn ? 'San speaks replies aloud (on)' : 'Speak replies aloud'}
              style={ttsOn ? { color: 'var(--san)' } : undefined}
            >{ttsOn ? '🔊' : '🔈'}</button>
          )}
          <input
            placeholder={recording ? 'Listening…' : transcribing ? 'Transcribing…' : 'Ask San anything about your life data…'}
            value={draft}
            onChange={e => setDraft(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') submit(); }}
            disabled={sendMut.isPending || recording || transcribing}
          />
          {voice.sttReady && (
            <button
              className="chat-voice-btn"
              onClick={toggleMic}
              disabled={sendMut.isPending || transcribing}
              title={recording ? 'Stop & send' : transcribing ? 'Transcribing…' : 'Speak to San'}
              style={recording ? { color: 'var(--debt, #e5484d)' } : undefined}
            >{recording ? '⏹' : transcribing ? '⏳' : '🎤'}</button>
          )}
          {/* Hands-free call needs both directions — mic to hear, speaker to answer. */}
          {voice.sttReady && voice.ttsReady && (
            <button
              className="chat-voice-btn"
              onClick={startCall}
              disabled={sendMut.isPending || recording || transcribing}
              title="Start a hands-free voice call with San"
            >📞</button>
          )}
          <button className="chat-send" onClick={submit} disabled={sendMut.isPending || !draft.trim()}><SendIcon /></button>
        </div>
        {voiceErr && <div style={{ color: 'var(--debt, #e5484d)', fontSize: '0.75rem', padding: '0.4rem 0.6rem' }}>{voiceErr}</div>}
      </div>
      {/* The call overlay itself now renders at the App root (services/voiceCallContext.tsx
          + components/VoiceCallUI.tsx) so it's reachable from every module, not just here. */}
      <div className="card">
        <h3>What San can do</h3>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.45rem', marginTop: '0.5rem' }}>
          {[
            { icon: '💬', text: 'Answer natural language questions about your finances, health, and properties' },
            { icon: '🔗', text: 'Cross-module insights pulled live from Vault, Vitara, and Aasthi' },
            { icon: '📅', text: 'Reminders and alerts, delivered to Telegram' },
            { icon: '📊', text: 'A unified activity feed across all connected modules' },
          ].map(f => (
            <div key={f.text} className="module-feature-item" style={{ '--mc': MC } as React.CSSProperties}>
              <span className="feat-icon">{f.icon}</span>
              <span>{f.text}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ── Reminders ── */
type ReminderFormState = { text: string; dueAt: string; notifyTelegram: boolean };
const emptyReminderForm: ReminderFormState = { text: '', dueAt: '', notifyTelegram: true };

function ReminderForm({ initial, onSubmit, onCancel, submitting }: {
  initial: ReminderFormState; onSubmit: (f: ReminderFormState) => void; onCancel: () => void; submitting: boolean;
}) {
  const [f, setF] = useState(initial);
  return (
    <div className="san-inline-form">
      <input placeholder="Reminder text…" value={f.text} onChange={e => setF({ ...f, text: e.target.value })} style={{ flex: 2 }} />
      <input type="datetime-local" value={f.dueAt} onChange={e => setF({ ...f, dueAt: e.target.value })} />
      <label className="san-checkbox-label">
        <input type="checkbox" checked={f.notifyTelegram} onChange={e => setF({ ...f, notifyTelegram: e.target.checked })} />
        Telegram
      </label>
      <button className="btn-primary" disabled={submitting || !f.text.trim() || !f.dueAt} onClick={() => onSubmit(f)}>Save</button>
      <button className="btn-ghost" onClick={onCancel}>Cancel</button>
    </div>
  );
}

function Reminders() {
  useTimezone(); // populates the shared timezone cache used by the helpers below
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const remindersQ = useQuery<Reminder[]>({ queryKey: ['san-reminders'], queryFn: () => get(`${API}/api/reminders`) });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['san-reminders'] });
  const createMut = useMutation({
    mutationFn: (f: ReminderFormState) => send(`${API}/api/reminders`, 'POST', { text: f.text, dueAt: localInputToUtcIso(f.dueAt), notifyTelegram: f.notifyTelegram }),
    onSuccess: () => { invalidate(); setAdding(false); },
  });
  const updateMut = useMutation({
    mutationFn: ({ id, f }: { id: string; f: ReminderFormState }) =>
      send(`${API}/api/reminders/${id}`, 'PUT', { text: f.text, dueAt: localInputToUtcIso(f.dueAt), notifyTelegram: f.notifyTelegram }),
    onSuccess: () => { invalidate(); setEditingId(null); },
  });
  const toggleDoneMut = useMutation({
    mutationFn: ({ id, done }: { id: string; done: boolean }) => send(`${API}/api/reminders/${id}/done`, 'PATCH', done),
    onSuccess: invalidate,
  });
  const deleteMut = useMutation({
    mutationFn: (id: string) => send(`${API}/api/reminders/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  if (remindersQ.isError) return <ApiError port={5300} />;
  const reminders = remindersQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Reminders</h3>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Close' : '+ Add Reminder'}</button>
      </div>
      {adding && (
        <ReminderForm initial={emptyReminderForm} submitting={createMut.isPending} onCancel={() => setAdding(false)} onSubmit={f => createMut.mutate(f)} />
      )}
      {reminders.length === 0 && !remindersQ.isLoading && <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No reminders yet.</p>}
      {reminders.map(r => (
        <div key={r.id}>
          {editingId === r.id ? (
            <ReminderForm
              initial={{ text: r.text, dueAt: toLocalInputValue(r.dueAt), notifyTelegram: r.notifyTelegram }}
              submitting={updateMut.isPending}
              onCancel={() => setEditingId(null)}
              onSubmit={f => updateMut.mutate({ id: r.id, f })}
            />
          ) : (
            <div className="reminder-item">
              <div
                className="reminder-check"
                style={r.done ? { background: 'var(--san)', borderColor: 'var(--san)' } : {}}
                onClick={() => toggleDoneMut.mutate({ id: r.id, done: !r.done })}
              />
              <span className="reminder-text" style={r.done ? { textDecoration: 'line-through', color: 'var(--text3)' } : {}}>{r.text}</span>
              {r.notifyTelegram && <span className="san-tg-badge" title={r.notifiedAt ? `Notified ${fmtDateTime(r.notifiedAt)}` : 'Will notify via Telegram'}>✈ {r.notifiedAt ? 'sent' : 'armed'}</span>}
              <span className="reminder-due">{fmtDateTime(r.dueAt)}</span>
              <button className="btn-ghost" style={{ fontSize: '0.72rem' }} onClick={() => setEditingId(r.id)}>Edit</button>
              <button className="btn-danger-ghost" style={{ fontSize: '0.72rem' }} onClick={() => deleteMut.mutate(r.id)}>Delete</button>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

/* ── Alerts ── */
type AlertFormState = {
  type: string; title: string; description: string;
  thresholdValue: string; triggerAt: string; active: boolean; notifyTelegram: boolean;
};
const emptyAlertForm: AlertFormState = { type: 'spending_threshold', title: '', description: '', thresholdValue: '', triggerAt: '', active: true, notifyTelegram: true };

function AlertForm({ initial, onSubmit, onCancel, submitting }: {
  initial: AlertFormState; onSubmit: (f: AlertFormState) => void; onCancel: () => void; submitting: boolean;
}) {
  const [f, setF] = useState(initial);
  const isThreshold = f.type === 'spending_threshold';
  return (
    <div className="san-form-grid">
      <div className="san-form-group">
        <label>Type</label>
        <select value={f.type} onChange={e => setF({ ...f, type: e.target.value })}>
          {ALERT_TYPES.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
        </select>
      </div>
      <div className="san-form-group san-form-span2">
        <label>Title</label>
        <input value={f.title} onChange={e => setF({ ...f, title: e.target.value })} />
      </div>
      <div className="san-form-group san-form-span2">
        <label>Description</label>
        <input value={f.description} onChange={e => setF({ ...f, description: e.target.value })} />
      </div>
      {isThreshold ? (
        <div className="san-form-group">
          <label>Threshold ($, 30-day spend)</label>
          <input type="number" value={f.thresholdValue} onChange={e => setF({ ...f, thresholdValue: e.target.value })} />
        </div>
      ) : (
        <div className="san-form-group">
          <label>Trigger At</label>
          <input type="datetime-local" value={f.triggerAt} onChange={e => setF({ ...f, triggerAt: e.target.value })} />
        </div>
      )}
      <div className="san-form-group" style={{ flexDirection: 'row', alignItems: 'center', gap: '1.25rem' }}>
        <label className="san-checkbox-label">
          <input type="checkbox" checked={f.active} onChange={e => setF({ ...f, active: e.target.checked })} /> Active
        </label>
        <label className="san-checkbox-label">
          <input type="checkbox" checked={f.notifyTelegram} onChange={e => setF({ ...f, notifyTelegram: e.target.checked })} /> Notify via Telegram
        </label>
      </div>
      <div className="san-form-actions san-form-span2">
        <button className="btn-primary" disabled={submitting || !f.title.trim()} onClick={() => onSubmit(f)}>Save</button>
        <button className="btn-ghost" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}

function Alerts() {
  useTimezone(); // populates the shared timezone cache used by the helpers below
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);

  const alertsQ = useQuery<AlertItem[]>({ queryKey: ['san-alerts'], queryFn: () => get(`${API}/api/alerts`) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['san-alerts'] });

  const toBody = (f: AlertFormState) => ({
    type: f.type, title: f.title, description: f.description,
    thresholdValue: f.type === 'spending_threshold' && f.thresholdValue ? Number(f.thresholdValue) : null,
    triggerAt: f.type !== 'spending_threshold' && f.triggerAt ? localInputToUtcIso(f.triggerAt) : null,
    active: f.active, notifyTelegram: f.notifyTelegram,
  });

  const createMut = useMutation({ mutationFn: (f: AlertFormState) => send(`${API}/api/alerts`, 'POST', toBody(f)), onSuccess: () => { invalidate(); setAdding(false); } });
  const updateMut = useMutation({ mutationFn: ({ id, f }: { id: string; f: AlertFormState }) => send(`${API}/api/alerts/${id}`, 'PUT', toBody(f)), onSuccess: () => { invalidate(); setEditingId(null); } });
  const deleteMut = useMutation({ mutationFn: (id: string) => send(`${API}/api/alerts/${id}`, 'DELETE'), onSuccess: invalidate });

  if (alertsQ.isError) return <ApiError port={5300} />;
  const alerts = alertsQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Custom Alerts</h3>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Close' : '+ Add Alert'}</button>
      </div>
      {adding && <AlertForm initial={emptyAlertForm} submitting={createMut.isPending} onCancel={() => setAdding(false)} onSubmit={f => createMut.mutate(f)} />}
      {alerts.length === 0 && !alertsQ.isLoading && <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No alerts configured.</p>}
      <div className="san-alert-list">
        {alerts.map(a => (
          <div key={a.id} className="card san-alert-card">
            {editingId === a.id ? (
              <AlertForm
                initial={{
                  type: a.type, title: a.title, description: a.description,
                  thresholdValue: a.thresholdValue?.toString() ?? '', triggerAt: a.triggerAt ? toLocalInputValue(a.triggerAt) : '',
                  active: a.active, notifyTelegram: a.notifyTelegram,
                }}
                submitting={updateMut.isPending}
                onCancel={() => setEditingId(null)}
                onSubmit={f => updateMut.mutate({ id: a.id, f })}
              />
            ) : (
              <>
                <div className="san-alert-top">
                  <div>
                    <span className="san-type-badge" style={{ background: `color-mix(in srgb, ${ALERT_TYPE_COLOR[a.type]} 18%, transparent)`, color: ALERT_TYPE_COLOR[a.type] }}>
                      {ALERT_TYPES.find(t => t.id === a.type)?.label ?? a.type}
                    </span>
                    <div className="san-alert-title">{a.title}</div>
                    {a.description && <div className="text-dim" style={{ fontSize: '0.78rem' }}>{a.description}</div>}
                  </div>
                  <div className="san-alert-actions">
                    {!a.active && <span className="text-dim" style={{ fontSize: '0.7rem' }}>inactive</span>}
                    <button className="btn-ghost" style={{ fontSize: '0.72rem' }} onClick={() => setEditingId(a.id)}>Edit</button>
                    <button className="btn-danger-ghost" style={{ fontSize: '0.72rem' }} onClick={() => deleteMut.mutate(a.id)}>Delete</button>
                  </div>
                </div>
                <div className="san-alert-meta">
                  {a.type === 'spending_threshold'
                    ? <span>Threshold: ${a.thresholdValue?.toLocaleString()}</span>
                    : <span>Triggers: {fmtDateTime(a.triggerAt)}</span>}
                  {a.triggeredAt && <span className="text-debt">• Triggered {fmtDateTime(a.triggeredAt)}</span>}
                  {a.notifyTelegram && <span className="san-tg-badge">✈ Telegram</span>}
                </div>
              </>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Email ── */
function Email() {
  useTimezone();
  const queryClient = useQueryClient();
  const [connecting, setConnecting] = useState<'google' | 'microsoft' | null>(null);
  const [showRules, setShowRules] = useState(false);

  const accountsQ = useQuery<EmailAccount[]>({ queryKey: ['san-email-accounts'], queryFn: () => get(`${API}/api/email/accounts`) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['san-email-accounts'] });

  const deleteMut = useMutation({ mutationFn: (id: string) => send(`${API}/api/email/accounts/${id}`, 'DELETE'), onSuccess: invalidate });

  const connect = async (provider: 'google' | 'microsoft') => {
    setConnecting(provider);
    try {
      const { url } = await get(`${API}/api/email/auth/${provider}`);
      // Full-page navigation, not fetch — OAuth consent has to happen in a real
      // top-level browser navigation to Google/Microsoft's own login page.
      window.location.href = url;
    } catch {
      setConnecting(null);
    }
  };

  if (accountsQ.isError) return <ApiError port={5300} />;
  const accounts = accountsQ.data ?? [];

  return (
    <div style={style}>
      <div className="san-toolbar">
        <h3 style={{ margin: 0 }}>Email Triage</h3>
        <button className="btn-ghost" onClick={() => setShowRules(true)}>⚙ Triage Rules</button>
      </div>
      <p className="text-dim" style={{ fontSize: '0.82rem', marginTop: '-0.5rem' }}>
        San checks connected inboxes every 15 minutes, summarizes what's worth your attention, and can
        create reminders, alerts, calendar events, or property tasks from what it finds. Findings are
        also written to NorthStar, so San can refer back to them in chat.
      </p>
      {showRules && <TriagePromptEditor onClose={() => setShowRules(false)} />}

      <div className="san-toolbar" style={{ marginTop: '1rem' }}>
        {EMAIL_PROVIDERS.map(p => (
          <button key={p.id} className="btn-primary" disabled={connecting === p.id} onClick={() => connect(p.id)}>
            {connecting === p.id ? 'Redirecting…' : `${p.icon} Connect ${p.label}`}
          </button>
        ))}
      </div>

      {accounts.length === 0 && !accountsQ.isLoading && (
        <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No email accounts connected yet.</p>
      )}
      <div className="san-alert-list" style={{ marginTop: '1rem' }}>
        {accounts.map(a => {
          const meta = EMAIL_PROVIDERS.find(p => p.id === a.provider);
          return (
            <div key={a.id} className="card san-alert-card">
              <div className="san-alert-top">
                <div>
                  <span className="san-type-badge" style={{ background: 'color-mix(in srgb, var(--san) 18%, transparent)', color: 'var(--san)' }}>
                    {meta?.icon} {meta?.label ?? a.provider}
                  </span>
                  <div className="san-alert-title">{a.emailAddress}</div>
                </div>
                <div className="san-alert-actions">
                  {!a.active && <span className="text-dim" style={{ fontSize: '0.7rem' }}>inactive</span>}
                  <button className="btn-danger-ghost" style={{ fontSize: '0.72rem' }} onClick={() => deleteMut.mutate(a.id)}>Disconnect</button>
                </div>
              </div>
              <div className="san-alert-meta">
                <span>{a.lastCheckedAt ? `Last checked ${fmtDateTime(a.lastCheckedAt)}` : 'Not checked yet'}</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ── Activity Feed ── */
function Feed() {
  const feedQ = useQuery<FeedResult>({ queryKey: ['san-feed'], queryFn: () => get(`${API}/api/feed`) });
  if (feedQ.isError) return <ApiError port={5300} />;
  const data = feedQ.data;

  return (
    <div style={style}>
      <div className="san-module-status">
        {(data?.modules ?? []).map(m => (
          <span key={m.module} className={`san-module-pill ${m.reachable ? '' : 'down'}`} style={{ '--mc': MODULE_COLOR[m.module] ?? MC } as React.CSSProperties}>
            ● {m.module} {m.reachable ? 'online' : 'unreachable'}
          </span>
        ))}
      </div>
      {(!data || data.entries.length === 0) && !feedQ.isLoading && (
        <p className="text-dim" style={{ textAlign: 'center', marginTop: '1.5rem' }}>No recent activity from connected modules.</p>
      )}
      <div className="san-feed-list">
        {(data?.entries ?? []).map((e, i) => (
          <div key={i} className="san-feed-item">
            <span className="san-feed-module" style={{ color: MODULE_COLOR[e.module] ?? MC }}>{e.module}</span>
            <div className="san-feed-body">
              <div className="san-feed-title">{e.title}</div>
              {e.description && <div className="text-dim" style={{ fontSize: '0.78rem' }}>{e.description}</div>}
            </div>
            <span className="san-feed-time">{fmtDateTime(e.occurredAt)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* ── Helpers ── */
const fmtTime = (d: string) => new Date(d).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
const fmtTimeRange = (s: string, e: string) => `${fmtTime(s)} – ${fmtTime(e)}`;
const relativeMinutes = (target: string, from: Date) => {
  const diff = Math.round((new Date(target).getTime() - from.getTime()) / 60_000);
  if (diff <= 0) return 'now';
  if (diff < 60) return `in ${diff} min`;
  const h = Math.floor(diff / 60); const m = diff % 60;
  return m > 0 ? `in ${h}h ${m}m` : `in ${h}h`;
};
const endsIn = (endTime: string, from: Date) => {
  const diff = Math.round((new Date(endTime).getTime() - from.getTime()) / 60_000);
  if (diff <= 0) return 'ending now';
  if (diff < 60) return `ends in ${diff} min`;
  const h = Math.floor(diff / 60); const m = diff % 60;
  return m > 0 ? `ends in ${h}h ${m}m` : `ends in ${h}h`;
};

const SOURCE_BADGE_CLASS: Record<string, string> = { google: 'san-google', ical: 'san-ical', manual: 'san-manual' };
const SOURCE_LABEL: Record<string, string> = { google: 'Google', ical: 'iCal', manual: 'Manual' };

/* ── Now & Next Widget ── */
function NowNext() {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 60_000);
    return () => clearInterval(id);
  }, []);

  const nowNextQ = useQuery<NowNextResult>({
    queryKey: ['san-now-next'],
    queryFn: () => get(`${API}/api/calendar/now-next?hours=3`),
    refetchInterval: 60_000,
  });
  const contextQ = useQuery<ContextResult>({
    queryKey: ['san-context'],
    queryFn: () => get(`${API}/api/context/latest`),
    refetchInterval: 60_000,
  });

  const data = nowNextQ.data;
  const location = contextQ.data?.location;
  const currentTime = now.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });

  if (nowNextQ.isError) return <ApiError port={5300} />;

  return (
    <div className="san-now-next" style={style}>
      <div className="san-now-next-header">
        <span>{currentTime}</span>
        {location?.address && <span className="san-location-badge">{'\u{1F4CD}'} {location.address}</span>}
      </div>
      {data ? (
        <>
          <div className="san-now-section">
            <span className="san-now-label">NOW</span>
            {data.current ? (
              <div className="san-now-event">
                <span className="san-now-event-title">{data.current.title}</span>
                <span className="san-time-remaining">{endsIn(data.current.endTime, now)}</span>
                {data.current.location && <span className="san-location-badge">{'\u{1F4CD}'} {data.current.location}</span>}
              </div>
            ) : (
              <span className="san-free-time">Free time</span>
            )}
          </div>
          {data.upcoming.length > 0 ? (
            <div className="san-next-section">
              <span className="san-next-label">NEXT</span>
              {data.upcoming.map((ev, i) => (
                <div key={ev.id ?? i} className="san-next-event">
                  <span className="san-next-event-title">{ev.title}</span>
                  <span className="san-next-event-time">{relativeMinutes(ev.startTime, now)} · {fmtTime(ev.startTime)}</span>
                  {ev.location && <span className="san-location-badge">{'\u{1F4CD}'} {ev.location}</span>}
                </div>
              ))}
            </div>
          ) : !data.current ? (
            <div className="san-free-time">Clear schedule for the next 3 hours</div>
          ) : null}
        </>
      ) : nowNextQ.isLoading ? (
        <span className="text-dim" style={{ fontSize: '0.78rem' }}>Loading schedule…</span>
      ) : null}
    </div>
  );
}

/* ── Calendar Tab ── */
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

type EventFormState = { title: string; startTime: string; endTime: string; location: string; allDay: boolean };
const emptyEventForm: EventFormState = { title: '', startTime: '', endTime: '', location: '', allDay: false };

function Calendar() {
  const queryClient = useQueryClient();
  const today = new Date();
  const [viewYear, setViewYear] = useState(today.getFullYear());
  const [viewMonth, setViewMonth] = useState(today.getMonth());
  const [selectedDay, setSelectedDay] = useState<number | null>(today.getDate());
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<EventFormState>(emptyEventForm);

  const monthStart = new Date(viewYear, viewMonth, 1);
  const monthEnd = new Date(viewYear, viewMonth + 1, 0, 23, 59, 59);

  const eventsQ = useQuery<CalendarEvent[]>({
    queryKey: ['san-calendar-events', viewYear, viewMonth],
    queryFn: () => get(`${API}/api/calendar/events?from=${monthStart.toISOString()}&to=${monthEnd.toISOString()}`),
  });

  const createMut = useMutation({
    mutationFn: (f: EventFormState) => send(`${API}/api/calendar/events`, 'POST', {
      title: f.title,
      startTime: new Date(f.startTime).toISOString(),
      endTime: new Date(f.endTime).toISOString(),
      location: f.location || undefined,
      allDay: f.allDay,
    }),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['san-calendar-events'] }); setAdding(false); setForm(emptyEventForm); },
  });

  const syncMut = useMutation({ mutationFn: () => get(`${API}/api/calendar/sync`) });
  const authMut = useMutation({
    mutationFn: () => get(`${API}/api/calendar/auth`),
    onSuccess: (data: { url: string }) => { if (data?.url) window.open(data.url, '_blank'); },
  });

  const events = eventsQ.data ?? [];

  // Build calendar grid
  const firstDayOfWeek = monthStart.getDay();
  const daysInMonth = monthEnd.getDate();
  const prevMonthEnd = new Date(viewYear, viewMonth, 0).getDate();

  const cells: { day: number; month: 'prev' | 'current' | 'next' }[] = [];
  for (let i = firstDayOfWeek - 1; i >= 0; i--) cells.push({ day: prevMonthEnd - i, month: 'prev' });
  for (let d = 1; d <= daysInMonth; d++) cells.push({ day: d, month: 'current' });
  const remaining = 7 - (cells.length % 7);
  if (remaining < 7) for (let d = 1; d <= remaining; d++) cells.push({ day: d, month: 'next' });

  // Events by day
  const eventsByDay = new Map<number, CalendarEvent[]>();
  for (const ev of events) {
    const d = new Date(ev.startTime).getDate();
    const m = new Date(ev.startTime).getMonth();
    if (m === viewMonth) {
      if (!eventsByDay.has(d)) eventsByDay.set(d, []);
      eventsByDay.get(d)!.push(ev);
    }
  }

  const isToday = (day: number) => day === today.getDate() && viewMonth === today.getMonth() && viewYear === today.getFullYear();
  const monthName = monthStart.toLocaleString('en-US', { month: 'long', year: 'numeric' });

  const prevMonth = () => { if (viewMonth === 0) { setViewMonth(11); setViewYear(y => y - 1); } else setViewMonth(m => m - 1); setSelectedDay(null); };
  const nextMonth = () => { if (viewMonth === 11) { setViewMonth(0); setViewYear(y => y + 1); } else setViewMonth(m => m + 1); setSelectedDay(null); };
  const goToday = () => { setViewYear(today.getFullYear()); setViewMonth(today.getMonth()); setSelectedDay(today.getDate()); };

  const selectedEvents = selectedDay ? (eventsByDay.get(selectedDay) ?? []) : [];

  if (eventsQ.isError) return <ApiError port={5300} />;

  return (
    <div className="san-calendar" style={style}>
      {/* Actions bar */}
      <div className="san-calendar-actions">
        <button className="btn-primary" onClick={() => authMut.mutate()} disabled={authMut.isPending}>Connect Google Calendar</button>
        <button className="btn-ghost" onClick={() => syncMut.mutate()} disabled={syncMut.isPending}>{syncMut.isPending ? 'Syncing…' : 'Sync'}</button>
        <button className="btn-primary" onClick={() => setAdding(a => !a)}>{adding ? 'Close' : '+ Add Event'}</button>
      </div>

      {/* Add event form */}
      {adding && (
        <div className="san-inline-form san-add-event-form">
          <input placeholder="Event title…" value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} style={{ flex: 2 }} />
          <input type="datetime-local" value={form.startTime} onChange={e => setForm({ ...form, startTime: e.target.value })} />
          <input type="datetime-local" value={form.endTime} onChange={e => setForm({ ...form, endTime: e.target.value })} />
          <input placeholder="Location (optional)" value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} />
          <label className="san-checkbox-label">
            <input type="checkbox" checked={form.allDay} onChange={e => setForm({ ...form, allDay: e.target.checked })} />
            All Day
          </label>
          <button className="btn-primary" disabled={createMut.isPending || !form.title.trim() || !form.startTime || !form.endTime} onClick={() => createMut.mutate(form)}>Save</button>
          <button className="btn-ghost" onClick={() => { setAdding(false); setForm(emptyEventForm); }}>Cancel</button>
        </div>
      )}

      {/* Calendar header */}
      <div className="san-calendar-header">
        <button className="btn-ghost" onClick={prevMonth}>&larr;</button>
        <span className="san-calendar-month-title">{monthName}</span>
        <button className="btn-ghost" onClick={goToday}>Today</button>
        <button className="btn-ghost" onClick={nextMonth}>&rarr;</button>
      </div>

      {/* Weekday headers */}
      <div className="san-calendar-weekdays">
        {WEEKDAYS.map(d => <div key={d} className="san-calendar-weekday">{d}</div>)}
      </div>

      {/* Day grid */}
      <div className="san-calendar-grid">
        {cells.map((c, i) => {
          const isCurrent = c.month === 'current';
          const hasEvents = isCurrent && eventsByDay.has(c.day);
          const cls = [
            'san-calendar-day',
            !isCurrent && 'san-other-month',
            isCurrent && isToday(c.day) && 'san-today',
            hasEvents && 'san-has-events',
            isCurrent && selectedDay === c.day && 'san-selected',
          ].filter(Boolean).join(' ');
          return (
            <div
              key={i}
              className={cls}
              onClick={() => isCurrent && setSelectedDay(c.day === selectedDay ? null : c.day)}
            >
              {c.day}
            </div>
          );
        })}
      </div>

      {/* Selected day events */}
      {selectedDay !== null && (
        <div className="san-day-events">
          <h4 style={{ margin: '0 0 0.5rem' }}>
            {new Date(viewYear, viewMonth, selectedDay).toLocaleDateString('en-US', { weekday: 'long', month: 'short', day: 'numeric' })}
          </h4>
          {selectedEvents.length === 0 && <p className="text-dim" style={{ fontSize: '0.82rem' }}>No events this day.</p>}
          {selectedEvents.map(ev => (
            <div key={ev.id} className="san-event-card">
              <div className="san-event-card-top">
                <span className="san-event-card-title">{ev.title}</span>
                <span className={`san-source-badge ${SOURCE_BADGE_CLASS[ev.source] ?? ''}`}>{SOURCE_LABEL[ev.source] ?? ev.source}</span>
              </div>
              <div className="san-event-card-meta">
                <span>{ev.allDay ? 'All day' : fmtTimeRange(ev.startTime, ev.endTime)}</span>
                {ev.location && <span className="san-location-badge">{'\u{1F4CD}'} {ev.location}</span>}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/* ── People ── */
interface PersonItem {
  id: string; name: string; phone: string | null; email: string | null;
  birthday: string | null; relationship: string; notes: string | null;
  tags: string | null; createdAt: string; updatedAt: string;
}

const REL_TYPES = ['family', 'friend', 'professional', 'neighbor', 'other'] as const;
const REL_COLOR: Record<string, string> = { family: '#f472b6', friend: '#4f9ef8', professional: '#f0a030', neighbor: '#1fc87a', other: '#94a3b8' };
const REL_ICON: Record<string, string> = { family: '👨‍👩‍👧', friend: '🤝', professional: '💼', neighbor: '🏠', other: '👤' };

function People() {
  const queryClient = useQueryClient();
  const peopleQ = useQuery<PersonItem[]>({ queryKey: ['people'], queryFn: () => get(`${API}/api/people`) });
  const { data: people } = peopleQ;
  const { data: birthdays } = useQuery<PersonItem[]>({ queryKey: ['birthdays'], queryFn: () => get(`${API}/api/people/birthdays?days=30`) });
  const [adding, setAdding] = useState(false);
  const [search, setSearch] = useState('');
  const [relFilter, setRelFilter] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [form, setForm] = useState({ name: '', phone: '', email: '', birthday: '', relationship: 'friend', notes: '', tags: '' });
  const invalidate = () => { queryClient.invalidateQueries({ queryKey: ['people'] }); queryClient.invalidateQueries({ queryKey: ['birthdays'] }); };

  const addPerson = useMutation({
    mutationFn: () => send(`${API}/api/people`, 'POST', form),
    onSuccess: () => { setForm({ name: '', phone: '', email: '', birthday: '', relationship: 'friend', notes: '', tags: '' }); setAdding(false); invalidate(); },
  });
  const deletePerson = useMutation({
    mutationFn: (id: string) => send(`${API}/api/people/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  const filtered = people?.filter(p => {
    if (relFilter && p.relationship !== relFilter) return false;
    if (!search) return true;
    const q = search.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.tags?.toLowerCase().includes(q) || p.email?.toLowerCase().includes(q) || p.phone?.includes(q);
  });

  const bdayStr = (b: string | null) => {
    if (!b) return null;
    try {
      const d = new Date(b + 'T00:00:00');
      const today = new Date();
      let next = new Date(today.getFullYear(), d.getMonth(), d.getDate());
      if (next < today) next = new Date(today.getFullYear() + 1, d.getMonth(), d.getDate());
      const days = Math.ceil((next.getTime() - today.getTime()) / 86400000);
      const label = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
      if (days === 0) return `🎉 Today!`;
      if (days <= 7) return `in ${days}d`;
      if (days <= 30) return `in ${days}d`;
      return label;
    } catch { return b; }
  };

  const relCounts = people?.reduce((acc, p) => { acc[p.relationship] = (acc[p.relationship] ?? 0) + 1; return acc; }, {} as Record<string, number>) ?? {};

  if (peopleQ.isError) return <ApiError port={5300} />;

  return (
    <div className="sp" style={style}>
      {/* ── Stats row ── */}
      <div className="sp-stats">
        <div className="sp-stat">
          <div className="sp-stat-val">{people?.length ?? 0}</div>
          <div className="sp-stat-label">Contacts</div>
        </div>
        <div className="sp-stat">
          <div className="sp-stat-val">{birthdays?.length ?? 0}</div>
          <div className="sp-stat-label">Birthdays (30d)</div>
        </div>
        {REL_TYPES.slice(0, 3).map(r => (
          <div className="sp-stat" key={r}>
            <div className="sp-stat-val" style={{ color: REL_COLOR[r] }}>{relCounts[r] ?? 0}</div>
            <div className="sp-stat-label">{r}</div>
          </div>
        ))}
      </div>

      {/* ── Upcoming birthdays ── */}
      {birthdays && birthdays.length > 0 && (
        <>
          <div className="sp-section-header">
            <span>🎂 Upcoming Birthdays</span>
          </div>
          <div className="sp-bday-row">
            {birthdays.map(p => (
              <div key={p.id} className="sp-bday-card">
                <div className="sp-avatar" style={{ '--av-color': REL_COLOR[p.relationship] || '#94a3b8' } as React.CSSProperties}>
                  {p.name.charAt(0).toUpperCase()}
                </div>
                <div className="sp-bday-name">{p.name.split(' ')[0]}</div>
                <div className="sp-bday-date">{bdayStr(p.birthday)}</div>
              </div>
            ))}
          </div>
        </>
      )}

      {/* ── Search + filter ── */}
      <div className="sp-controls">
        <div className="sp-search-row">
          <input className="sp-search" placeholder="Search contacts..." value={search} onChange={e => setSearch(e.target.value)} />
          <button className="sp-add-btn" onClick={() => setAdding(!adding)}>{adding ? 'Cancel' : '+ Add'}</button>
        </div>
        <div className="sp-filter-pills">
          <button className={`sp-pill ${relFilter === null ? 'active' : ''}`} onClick={() => setRelFilter(null)}>
            All ({people?.length ?? 0})
          </button>
          {REL_TYPES.map(r => (
            <button key={r} className={`sp-pill ${relFilter === r ? 'active' : ''}`} onClick={() => setRelFilter(relFilter === r ? null : r)}
              style={{ '--pill-color': REL_COLOR[r] } as React.CSSProperties}>
              {REL_ICON[r]} {r} {relCounts[r] ? `(${relCounts[r]})` : ''}
            </button>
          ))}
        </div>
      </div>

      {/* ── Add form ── */}
      {adding && (
        <div className="sp-add-form">
          <div className="sp-form-row">
            <input className="sp-input sp-input--wide" placeholder="Name *" value={form.name} onChange={e => setForm(f => ({ ...f, name: e.target.value }))} />
            <select className="sp-input" value={form.relationship} onChange={e => setForm(f => ({ ...f, relationship: e.target.value }))}>
              {REL_TYPES.map(r => <option key={r} value={r}>{r}</option>)}
            </select>
          </div>
          <div className="sp-form-row">
            <input className="sp-input" placeholder="Phone" value={form.phone} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} />
            <input className="sp-input" placeholder="Email" value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} />
            <input className="sp-input" type="date" placeholder="Birthday" value={form.birthday} onChange={e => setForm(f => ({ ...f, birthday: e.target.value }))} />
          </div>
          <div className="sp-form-row">
            <input className="sp-input" placeholder="Tags (comma-sep)" value={form.tags} onChange={e => setForm(f => ({ ...f, tags: e.target.value }))} />
            <input className="sp-input sp-input--wide" placeholder="Notes" value={form.notes} onChange={e => setForm(f => ({ ...f, notes: e.target.value }))} />
            <button className="sp-save-btn" disabled={!form.name || addPerson.isPending} onClick={() => addPerson.mutate()}>
              {addPerson.isPending ? 'Adding...' : 'Save Contact'}
            </button>
          </div>
        </div>
      )}

      {/* ── Contact list ── */}
      <div className="sp-list">
        {filtered?.length === 0 && <div className="sp-empty">{search || relFilter ? 'No matches found.' : 'No contacts yet.'}</div>}

        {filtered?.map(p => {
          const expanded = expandedId === p.id;
          const color = REL_COLOR[p.relationship] || '#94a3b8';
          return (
            <div key={p.id} className={`sp-card ${expanded ? 'expanded' : ''}`} onClick={() => setExpandedId(expanded ? null : p.id)}>
              <div className="sp-card-main">
                <div className="sp-avatar" style={{ '--av-color': color } as React.CSSProperties}>
                  {p.name.charAt(0).toUpperCase()}
                </div>
                <div className="sp-card-info">
                  <div className="sp-card-name">{p.name}</div>
                  <div className="sp-card-meta">
                    {p.phone && <span className="sp-meta-item">📞 {p.phone}</span>}
                    {p.email && <span className="sp-meta-item">✉ {p.email}</span>}
                  </div>
                </div>
                <span className="sp-rel-badge" style={{ '--rel-color': color } as React.CSSProperties}>{p.relationship}</span>
              </div>
              {expanded && (
                <div className="sp-card-detail" onClick={e => e.stopPropagation()}>
                  {p.birthday && <div className="sp-detail-row"><span className="sp-detail-label">Birthday</span><span>{new Date(p.birthday + 'T00:00:00').toLocaleDateString('en-US', { month: 'long', day: 'numeric' })}</span></div>}
                  {p.tags && <div className="sp-detail-row"><span className="sp-detail-label">Tags</span><span className="sp-tags">{p.tags.split(',').map(t => <span key={t} className="sp-tag">{t.trim()}</span>)}</span></div>}
                  {p.notes && <div className="sp-detail-row"><span className="sp-detail-label">Notes</span><span className="sp-notes">{p.notes}</span></div>}
                  <div className="sp-detail-actions">
                    {p.phone && <a href={`tel:${p.phone}`} className="sp-action-btn" onClick={e => e.stopPropagation()}>📞 Call</a>}
                    {p.email && <a href={`mailto:${p.email}`} className="sp-action-btn" onClick={e => e.stopPropagation()}>✉ Email</a>}
                    <button className="sp-action-btn sp-action-btn--danger" onClick={e => { e.stopPropagation(); deletePerson.mutate(p.id); }}>Delete</button>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function SanModuleInner() {
  const [page, setPage] = useState<Page>('assistant');
  return (
    <div>
      <NowNext />
      <nav className="module-subnav" style={style}>
        {TABS.map(t => (
          <button key={t.id} className={`module-tab ${page === t.id ? 'active' : ''}`} onClick={() => setPage(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>
      {page === 'assistant' && <Assistant />}
      {page === 'reminders' && <Reminders />}
      {page === 'alerts'    && <Alerts />}
      {page === 'feed'      && <Feed />}
      {page === 'calendar'  && <Calendar />}
      {page === 'people'    && <People />}
      {page === 'email'     && <Email />}
    </div>
  );
}

export default function SanModule() {
  return (
    <QueryClientProvider client={qc}>
      <SanModuleInner />
    </QueryClientProvider>
  );
}

import { useEffect, useState } from 'react';
import { useVoiceCallContext } from '../services/voiceCallContext';
import { CALL_STATE_LABEL } from '../services/voiceSession';
import '../styles/san.css'; // call-* classes live here alongside San's other voice styles

// Rendered once at the App root (outside the per-module switch) so a call
// survives switching tabs, and so it can be reached from anywhere in the
// dashboard — not just San's own page.

export function CallFab() {
  const { voice, inCall, startCall } = useVoiceCallContext();
  // Needs both directions — mic to hear, speaker to answer — same gate as
  // San's own 📞 button.
  if (!voice.sttReady || !voice.ttsReady || inCall) return null;
  return (
    <button className="call-fab" onClick={startCall} title="Call San">
      📞
    </button>
  );
}

export function CallOverlay() {
  const { inCall, callState, callUser, callSan, callError, endCall } = useVoiceCallContext();

  // The call has always run at the App root and survived tab switches — it was only
  // the full-screen backdrop that made it feel modal, since inset:0 swallows every
  // click. Docked mode drops the backdrop and shrinks to a corner bar, so you can
  // keep talking while actually using the thing you're talking about.
  const [docked, setDocked] = useState(false);

  // A new call always starts expanded: docking is a choice made during a call, not a
  // preference that silently persists into the next one.
  useEffect(() => { if (inCall) setDocked(false); }, [inCall]);

  // Esc hangs up — a call holds the mic, so there must be an obvious way out. Only
  // while expanded: docked, Esc belongs to whatever module you're actually using, and
  // the dock keeps its own Hang up button visible so the exit is still one click.
  useEffect(() => {
    if (!inCall || docked) return;
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') endCall(); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [inCall, docked, endCall]);

  if (!inCall) return null;

  if (docked) {
    return (
      <div className="call-dock" role="status" aria-live="polite">
        <div className={`call-orb call-orb--sm ${callState}`}><span>S</span></div>
        <div className="call-dock-text">
          <div className="call-dock-state">{CALL_STATE_LABEL[callState]}</div>
          {/* The last thing said, so the dock still shows the call is alive without
              needing to be reopened. */}
          {(callSan || callUser) && (
            <div className="call-dock-said">{callSan || callUser}</div>
          )}
          {callError && <div className="call-err call-err--sm">{callError}</div>}
        </div>
        <button className="call-dock-btn" onClick={() => setDocked(false)} title="Expand call">⤢</button>
        <button className="call-dock-hangup" onClick={endCall} title="Hang up">Hang up</button>
      </div>
    );
  }

  return (
    <div className="call-overlay">
      <div className="call-panel">
        <button className="call-min" onClick={() => setDocked(true)} title="Keep talking while you browse">–</button>
        <div className={`call-orb ${callState}`}><span>S</span></div>
        <div className="call-state">{CALL_STATE_LABEL[callState]}</div>

        {callUser && (
          <div className="call-line">
            <span className="call-who">You</span>
            <span className="call-said">{callUser}</span>
          </div>
        )}
        {callSan && (
          <div className="call-line">
            <span className="call-who san">San</span>
            <span className="call-said">{callSan}</span>
          </div>
        )}
        {callError && <div className="call-err">{callError}</div>}

        <p className="call-hint">
          Just talk — San answers when you pause. Speak over them to cut in. Esc or Hang up to end.
          Minimise to keep the call going while you use the dashboard.
        </p>
        <button className="call-hangup" onClick={endCall}>Hang up</button>
      </div>
    </div>
  );
}

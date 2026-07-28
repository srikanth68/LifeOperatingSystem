import { useEffect } from 'react';
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

  // Esc hangs up — a call holds the mic, so there must be an obvious way out.
  useEffect(() => {
    if (!inCall) return;
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') endCall(); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [inCall, endCall]);

  if (!inCall) return null;

  return (
    <div className="call-overlay">
      <div className="call-panel">
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
        </p>
        <button className="call-hangup" onClick={endCall}>Hang up</button>
      </div>
    </div>
  );
}

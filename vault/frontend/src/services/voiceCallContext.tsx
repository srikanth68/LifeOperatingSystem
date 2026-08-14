import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { authHeaders } from './auth';
import { moduleApi } from './apiHost';
import { getVoiceStatus, stopSpeaking, type VoiceStatus } from './voice';
import { VoiceCall, type CallState } from './voiceSession';

// Lifts San's hands-free call (voiceSession.ts) out of SanModule so it's reachable
// from anywhere in the dashboard, not just San's own tab — a floating call button
// (CallFab) and the in-call overlay both live at the App root, outside the
// per-module switch, so switching tabs mid-call doesn't drop it.

const SAN = moduleApi(5300);
const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: { ...authHeaders(), ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  }).then(r => { if (!r.ok) throw new Error(r.status.toString()); return r.status === 204 ? null : r.json(); });

interface VoiceCallContextValue {
  voice: VoiceStatus;
  callState: CallState;
  callUser: string;
  callSan: string;
  callError: string | null;
  inCall: boolean;
  startCall: () => Promise<void>;
  endCall: () => void;
}

const VoiceCallContext = createContext<VoiceCallContextValue | null>(null);

export function useVoiceCallContext(): VoiceCallContextValue {
  const ctx = useContext(VoiceCallContext);
  if (!ctx) throw new Error('useVoiceCallContext must be used within VoiceCallProvider');
  return ctx;
}

export function VoiceCallProvider({ children }: { children: ReactNode }) {
  const [voice, setVoice] = useState<VoiceStatus>({ sttReady: false, ttsReady: false });
  const [callState, setCallState] = useState<CallState>('idle');
  const [callUser, setCallUser] = useState('');
  const [callSan, setCallSan] = useState('');
  const [callError, setCallError] = useState<string | null>(null);
  const callRef = useRef<VoiceCall | null>(null);

  useEffect(() => { getVoiceStatus().then(setVoice); }, []);

  // A call holds the mic and an AudioContext for the whole app's lifetime —
  // tear it down on unmount rather than leaving the mic light on.
  useEffect(() => () => callRef.current?.hangUp(), []);

  const startCall = async () => {
    if (callRef.current) return; // already in a call
    setCallError(null);
    setCallUser(''); setCallSan('');
    stopSpeaking(); // don't let a push-to-talk reply overlap the call
    const call = new VoiceCall({
      onState: setCallState,
      onUserText: setCallUser,
      onSanText: setCallSan,
      onError: setCallError,
      sendToSan: async (text: string) => {
        // Always spoken here by definition — the whole turn is voice in, voice out.
        const res = await send(`${SAN}/api/chat/messages`, 'POST', { content: text, mode: 'voice' });
        return res?.assistantMessage?.content ?? '';
      },
    });
    callRef.current = call;
    try {
      await call.start();
    } catch (e) {
      callRef.current = null;
      setCallState('idle');
      setCallError((e as Error).message);
    }
  };

  const endCall = () => {
    callRef.current?.hangUp();
    callRef.current = null;
    setCallState('idle');
  };

  // Deep link support: a PWA home-screen shortcut (manifest.webmanifest) or any
  // link ending ?call=san opens straight into a call once voice is confirmed
  // ready, then cleans the URL so a refresh or hang-up doesn't re-trigger it.
  const autoStartTried = useRef(false);
  useEffect(() => {
    if (autoStartTried.current) return;
    if (!voice.sttReady || !voice.ttsReady) return;
    const params = new URLSearchParams(window.location.search);
    if (params.get('call') !== 'san') return;
    autoStartTried.current = true;
    params.delete('call');
    const rest = params.toString();
    window.history.replaceState(null, '', window.location.pathname + (rest ? `?${rest}` : ''));
    void startCall();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [voice.sttReady, voice.ttsReady]);

  const value: VoiceCallContextValue = {
    voice, callState, callUser, callSan, callError,
    inCall: callState !== 'idle',
    startCall, endCall,
  };

  return <VoiceCallContext.Provider value={value}>{children}</VoiceCallContext.Provider>;
}

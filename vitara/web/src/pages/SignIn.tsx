import { useCallback, useEffect, useState } from 'react';
import { auth } from '../services/auth';

// Signing in, in one of the two ways the server offers: a PIN pad on a trusted network, a
// username and password anywhere else. The server decides which (see auth.probe); this screen
// only draws it. Neither the sign-in itself nor the tokens are Vitara's: they are Maaya's,
// untouched, until Vitara has accounts of its own.

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', '⌫'];

const MARK = (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    <path d="M3 13h3l3 7 4-16 3 9h5" />
  </svg>
);

function Frame({ children, note }: { children: React.ReactNode; note: string }) {
  return (
    <main className="vx-signin">
      <div className="vx-signin-card">
        <span className="vx-signin-mark">{MARK}</span>
        <h1>Vitara</h1>
        <p className="vx-signin-sub">Your health, against your own history</p>
        {children}
      </div>
      <p className="vx-signin-note">{note}</p>
    </main>
  );
}

function Pin({ length, onSignedIn }: { length: number; onSignedIn: () => void }) {
  const [digits, setDigits] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = useCallback(async (pin: string) => {
    setBusy(true);
    setError('');
    try {
      await auth.pinLogin(pin);
      onSignedIn();
    } catch (e) {
      setDigits('');
      setError(e instanceof Error ? e.message : 'That PIN was not accepted');
    } finally {
      setBusy(false);
    }
  }, [onSignedIn]);

  const press = useCallback((key: string) => {
    if (busy || key === '') return;
    if (key === '⌫') { setDigits(d => d.slice(0, -1)); setError(''); return; }

    setDigits(d => {
      if (d.length >= length) return d;
      const next = d + key;
      if (next.length === length) setTimeout(() => submit(next), 80);
      return next;
    });
  }, [busy, length, submit]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key >= '0' && e.key <= '9') press(e.key);
      else if (e.key === 'Backspace') press('⌫');
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [press]);

  return (
    <Frame note="Trusted network · PIN">
      <p className="vx-signin-label">Enter your PIN</p>
      <div className="vx-dots" aria-label={`${digits.length} of ${length} digits entered`}>
        {Array.from({ length }, (_, i) => <span key={i} className={i < digits.length ? 'on' : ''} />)}
      </div>
      <p className="vx-signin-error" role="alert">{error || ' '}</p>
      <div className="vx-keys">
        {KEYS.map((k, i) => (
          <button
            key={i}
            type="button"
            className={k === '' ? 'blank' : ''}
            disabled={busy || k === ''}
            onClick={() => press(k)}
            aria-label={k === '⌫' ? 'Delete' : k || undefined}
          >
            {k}
          </button>
        ))}
      </div>
    </Frame>
  );
}

function Password({ onSignedIn }: { onSignedIn: () => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      await auth.login(username, password);
      onSignedIn();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in failed');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Frame note="Username and password">
      <form onSubmit={submit} className="vx-form">
        <label>
          Username
          <input value={username} onChange={e => setUsername(e.target.value)} autoComplete="username" autoFocus />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} autoComplete="current-password" />
        </label>
        <p className="vx-signin-error" role="alert">{error || ' '}</p>
        <button className="vx-primary" type="submit" disabled={busy || !username || !password}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </Frame>
  );
}

export default function SignIn({ mode, pinLength, onSignedIn }: {
  mode: 'pin' | 'password';
  pinLength: number;
  onSignedIn: () => void;
}) {
  return mode === 'pin' ? <Pin length={pinLength} onSignedIn={onSignedIn} /> : <Password onSignedIn={onSignedIn} />;
}

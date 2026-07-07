import { useState, useEffect, useCallback } from 'react';
import { auth } from '../services/auth';
import ArcReactor, { HudCorners, HudStatus, HudScanlines } from '../components/ArcReactor';
import '../styles/login.css';

const KEYS = ['1','2','3','4','5','6','7','8','9','','0','⌫'];

export default function PinPad({ length, onSuccess }: { length: number; onSuccess: () => void }) {
  const [digits, setDigits] = useState('');
  const [error, setError] = useState('');
  const [shake, setShake] = useState(false);
  const [loading, setLoading] = useState(false);

  const submit = useCallback(async (pin: string) => {
    setLoading(true);
    setError('');
    try {
      await auth.pinLogin(pin);
      onSuccess();
    } catch (e) {
      setDigits('');
      setShake(true);
      setError(e instanceof Error ? e.message : 'Access denied');
      setTimeout(() => setShake(false), 500);
    } finally {
      setLoading(false);
    }
  }, [onSuccess]);

  const press = useCallback((key: string) => {
    if (loading) return;
    if (key === '⌫') {
      setDigits(d => d.slice(0, -1));
      setError('');
      return;
    }
    if (key === '') return;
    setDigits(d => {
      const next = d + key;
      if (next.length >= length) {
        setTimeout(() => submit(next), 80);
      }
      return next.length <= length ? next : d;
    });
  }, [length, loading, submit]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key >= '0' && e.key <= '9') press(e.key);
      else if (e.key === 'Backspace') press('⌫');
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [press]);

  return (
    <div className="login-page">
      <HudScanlines />
      <HudCorners />
      <div className="pin-container">
        <div className="arc-reactor">
          <ArcReactor size={130} />
        </div>
        <p className="pin-label">Authorization Required</p>

        <div className={`pin-dots ${shake ? 'pin-shake' : ''}`}>
          {Array.from({ length }, (_, i) => (
            <span key={i} className={`pin-dot ${i < digits.length ? 'filled' : ''}`} />
          ))}
        </div>

        {error ? <p className="pin-error">{error}</p> : <p className="pin-error">&nbsp;</p>}

        <div className="pin-grid">
          {KEYS.map((k, i) => (
            <button
              key={i}
              className={`pin-key ${k === '' ? 'pin-key-blank' : ''} ${k === '⌫' ? 'pin-key-del' : ''}`}
              onClick={() => press(k)}
              disabled={loading || k === ''}
              tabIndex={-1}
            >
              {k}
            </button>
          ))}
        </div>
      </div>
      <HudStatus text="TRUSTED NETWORK · PIN AUTH" />
    </div>
  );
}

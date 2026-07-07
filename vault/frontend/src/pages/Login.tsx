import { useState } from 'react';
import { auth } from '../services/auth';
import ArcReactor, { HudCorners, HudStatus, HudScanlines } from '../components/ArcReactor';
import '../styles/login.css';

export default function Login({ onLogin }: { onLogin: () => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await auth.login(username, password);
      onLogin();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="login-page">
      <HudScanlines />
      <HudCorners />
      <form className="login-card" onSubmit={handleSubmit}>
        <div className="arc-reactor" style={{ width: 120, height: 120 }}>
          <ArcReactor size={120} />
        </div>
        <h1 className="login-title">MAAYA</h1>
        <p className="login-subtitle">Personal Operating System</p>

        {error && <div className="login-error">{error}</div>}

        <label className="login-label">
          Username
          <input
            className="login-input"
            type="text"
            value={username}
            onChange={e => setUsername(e.target.value)}
            autoFocus
            autoComplete="username"
            placeholder="Enter username"
          />
        </label>

        <label className="login-label">
          Password
          <input
            className="login-input"
            type="password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            autoComplete="current-password"
            placeholder="Enter password"
          />
        </label>

        <button className="login-btn" type="submit" disabled={loading || !username || !password}>
          {loading ? 'Authenticating...' : 'Initialize'}
        </button>
      </form>
      <HudStatus text="REMOTE ACCESS · CREDENTIALS REQUIRED" />
    </div>
  );
}

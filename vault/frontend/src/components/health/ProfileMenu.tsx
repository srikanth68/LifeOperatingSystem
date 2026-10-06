import { useCallback, useEffect, useRef, useState } from 'react';
import {
  DEFAULT_PROFILE, cmToFtIn, createPerson, deletePerson, ftInToCm, getProfile, listPeople, personLabel,
  refreshProfile, saveProfile, setProfileId, useProfileId, type MyProfile, type Person, type ProfileInput,
} from '../../services/profile';
import { todayInTz } from '../../services/timezone';

// Who this health screen is about, and the form to change who they are.
//
// One of these sits in the header of the shared shell, which both Vitara and Insight use, so
// the choice is made once and both follow it. Everything below the header is re-created when
// it changes -- see useProfileKey -- so no screen can show one person's numbers under
// another's name.

type Mode = { kind: 'closed' } | { kind: 'list' } | { kind: 'form'; target: 'new' | string };

const UNIT_KEY = 'vitara.heightUnit';

const readUnit = (): 'imperial' | 'metric' => {
  try { return localStorage.getItem(UNIT_KEY) === 'metric' ? 'metric' : 'imperial'; } catch { return 'imperial'; }
};

export function ProfileMenu() {
  const activeId = useProfileId();
  const [mode, setMode] = useState<Mode>({ kind: 'closed' });
  const [people, setPeople] = useState<Person[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);
  const root = useRef<HTMLDivElement>(null);

  const reload = useCallback(() => {
    listPeople().then(p => { setPeople(Array.isArray(p) ? p : []); setLoadError(null); })
      .catch(e => setLoadError((e as Error).message));
  }, []);

  useEffect(() => { reload(); }, [reload, activeId]);

  // Click outside, and Escape, close it. A popover that has to be hunted for is not a menu.
  useEffect(() => {
    if (mode.kind === 'closed') return;
    const away = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setMode({ kind: 'closed' }); };
    const esc = (e: KeyboardEvent) => { if (e.key === 'Escape') setMode({ kind: 'closed' }); };
    document.addEventListener('mousedown', away);
    document.addEventListener('keydown', esc);
    return () => { document.removeEventListener('mousedown', away); document.removeEventListener('keydown', esc); };
  }, [mode.kind]);

  const active = people.find(p => p.id === activeId);
  const label = active ? personLabel(active) : activeId === DEFAULT_PROFILE ? 'Me' : '…';

  return (
    <div className="hx-profile" ref={root}>
      <button
        className="hx-pill hx-profile-btn"
        aria-haspopup="true"
        aria-expanded={mode.kind !== 'closed'}
        onClick={() => setMode(m => (m.kind === 'closed' ? { kind: 'list' } : { kind: 'closed' }))}
      >
        <span className="hx-profile-dot" aria-hidden="true">{label.slice(0, 1).toUpperCase()}</span>
        {label}
      </button>

      {mode.kind === 'list' && (
        <div className="hx-profile-pop" role="menu">
          <p className="hx-profile-head">Whose health</p>

          {loadError && <p className="hx-error">Couldn’t load people: {loadError}</p>}

          <div className="hx-profile-list">
            {people.map(p => (
              <button
                key={p.id}
                role="menuitemradio"
                aria-checked={p.id === activeId}
                className={`hx-profile-row ${p.id === activeId ? 'is-active' : ''}`}
                onClick={() => { setProfileId(p.id); setMode({ kind: 'closed' }); }}
              >
                <span className="hx-profile-dot" aria-hidden="true">{personLabel(p).slice(0, 1).toUpperCase()}</span>
                <span>{personLabel(p)}</span>
                {p.id === activeId && <em>viewing</em>}
              </button>
            ))}
          </div>

          <div className="hx-profile-actions">
            <button className="hx-btn" onClick={() => setMode({ kind: 'form', target: activeId })}>Edit this profile</button>
            <button className="hx-btn" onClick={() => setMode({ kind: 'form', target: 'new' })}>Add a person</button>
          </div>
        </div>
      )}

      {mode.kind === 'form' && (
        <ProfileForm
          key={mode.target}
          target={mode.target}
          people={people}
          onBack={() => setMode({ kind: 'list' })}
          onDone={() => { reload(); setMode({ kind: 'closed' }); }}
        />
      )}
    </div>
  );
}

function ProfileForm({ target, people, onBack, onDone }: {
  target: 'new' | string;
  people: Person[];
  onBack: () => void;
  onDone: () => void;
}) {
  const creating = target === 'new';
  const person = people.find(p => p.id === target);

  const [name, setName] = useState('');
  const [sex, setSex] = useState('');
  const [dob, setDob] = useState('');
  const [unit, setUnit] = useState<'imperial' | 'metric'>(readUnit());
  const [ft, setFt] = useState('');
  const [inch, setInch] = useState('');
  const [cm, setCm] = useState('');
  const [mine, setMine] = useState<MyProfile | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState('');

  // Editing starts from what is stored, so Save never silently changes a field you did not touch.
  useEffect(() => {
    if (creating) return;
    getProfile(target).then(p => {
      setMine(p);
      setName(p.name ?? '');
      setSex(p.biologicalSex ?? '');
      setDob(p.dateOfBirth ?? '');
      if (p.heightCm) {
        const f = cmToFtIn(p.heightCm);
        setFt(String(f.ft)); setInch(String(f.inch)); setCm(String(p.heightCm));
      }
    }).catch(e => setError((e as Error).message));
  }, [target, creating]);

  const switchUnit = (next: 'imperial' | 'metric') => {
    // Carry the height across, so toggling the unit never loses what was typed.
    const current = heightCm();
    if (current != null) {
      const f = cmToFtIn(current);
      setFt(String(f.ft)); setInch(String(f.inch)); setCm(String(current));
    }
    setUnit(next);
    try { localStorage.setItem(UNIT_KEY, next); } catch { /* fine */ }
  };

  function heightCm(): number | null {
    if (unit === 'metric') return cm.trim() === '' ? null : Number(cm);
    if (ft.trim() === '' && inch.trim() === '') return null;
    return ftInToCm(Number(ft || 0), Number(inch || 0));
  }

  async function save() {
    setBusy(true); setError(null);

    const input: ProfileInput = {
      name: name.trim() || null,
      biologicalSex: sex || null,
      dateOfBirth: dob || null,
      heightCm: heightCm(),
    };

    try {
      if (creating) {
        const made = await createPerson(input);
        setProfileId(made.id);          // straight into the person just made
      } else {
        await saveProfile(target, input);
        refreshProfile();               // BMI, ranges and the rest are read against this
      }
      onDone();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true); setError(null);
    try {
      await deletePerson(target);
      setProfileId(DEFAULT_PROFILE);
      onDone();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const who = person ? personLabel(person) : 'this person';

  return (
    <div className="hx-profile-pop hx-profile-formpop">
      <p className="hx-profile-head">
        <button className="hx-icon-btn" onClick={onBack} aria-label="Back">‹</button>
        {creating ? 'Add a person' : `About ${who}`}
      </p>

      <label className="hx-field">
        <span>Name</span>
        <input id="pf-name" value={name} onChange={e => setName(e.target.value)} maxLength={60}
               placeholder={creating ? 'What should we call them?' : 'Optional'} autoFocus />
      </label>

      <label className="hx-field">
        <span>Biological sex</span>
        <select id="pf-sex" value={sex} onChange={e => setSex(e.target.value)}>
          <option value="">Prefer not to say</option>
          <option value="female">Female</option>
          <option value="male">Male</option>
        </select>
        <small>
          Used only for reference ranges and kidney function, where it changes the answer.
          {mine?.sources?.sex === 'you' && ' Set by you; the Oura sync won’t change it.'}
          {mine?.sources?.sex === 'sync' && ' Currently from your Oura profile; entering one here takes over.'}
        </small>
      </label>

      <label className="hx-field">
        <span>Date of birth</span>
        <input id="pf-dob" type="date" value={dob} max={todayInTz()} onChange={e => setDob(e.target.value)} />
        <small>{mine?.age != null ? `Age ${mine.age}. ` : ''}Worked out from this, so it stays right on its own.</small>
      </label>

      <div className="hx-field">
        <span>
          Height
          <button className="hx-linkbtn" type="button" onClick={() => switchUnit(unit === 'imperial' ? 'metric' : 'imperial')}>
            use {unit === 'imperial' ? 'cm' : 'ft / in'}
          </button>
        </span>

        {unit === 'imperial' ? (
          <span className="hx-height-row">
            <input id="pf-ft" type="number" inputMode="numeric" min={3} max={8} placeholder="ft" value={ft} onChange={e => setFt(e.target.value)} />
            <input id="pf-in" type="number" inputMode="decimal" min={0} max={11.9} step="0.5" placeholder="in" value={inch} onChange={e => setInch(e.target.value)} />
          </span>
        ) : (
          <span className="hx-height-row">
            <input id="pf-cm" type="number" inputMode="decimal" min={100} max={250} step="0.1" placeholder="cm" value={cm} onChange={e => setCm(e.target.value)} />
          </span>
        )}

        <small>
          {heightCm() != null && `${heightCm()} cm. `}
          Needed for BMI and waist-to-height.
          {mine?.sources?.height === 'you' && ' Set by you; the Oura sync won’t overwrite it.'}
          {mine?.sources?.height === 'sync' && ' Currently from your Oura profile; entering one here takes over.'}
        </small>
      </div>

      {error && <p className="hx-error">{error}</p>}

      <div className="hx-profile-actions">
        <button className="hx-btn" disabled={busy || (creating && !name.trim())} onClick={save}>
          {busy ? 'Saving…' : creating ? 'Add person' : 'Save'}
        </button>
      </div>

      {!creating && target !== DEFAULT_PROFILE && (
        <details className="hx-profile-danger">
          <summary>Delete {who}…</summary>
          <p>
            This permanently deletes everything recorded for {who}: readings, blood work, findings. It cannot be undone.
            Type <b>delete</b> to confirm.
          </p>
          <span className="hx-height-row">
            <input id="pf-del" value={confirmDelete} onChange={e => setConfirmDelete(e.target.value)} placeholder="delete" />
            <button className="hx-btn hx-btn-danger" disabled={busy || confirmDelete.trim().toLowerCase() !== 'delete'} onClick={remove}>
              Delete forever
            </button>
          </span>
        </details>
      )}
    </div>
  );
}

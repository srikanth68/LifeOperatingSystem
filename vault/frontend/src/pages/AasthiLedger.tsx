import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';

// The money side of Aasthi: what was owed, what actually arrived, and what San thinks
// belongs to a property but has not been agreed to yet.
//
// Three sections in the order they matter on an ordinary day. The review queue is
// first because it is the only one that asks anything of the user; everything else is
// there to be read.
//
// Nothing here notifies. The queue waits until it is looked at -- a daily ping about
// three transactions is how the reminders turned into something to ignore.

const API = moduleApi(5200);
const style = { '--mc': 'var(--aasthi)' } as React.CSSProperties;

const get = (url: string) =>
  fetch(url, { headers: authHeaders() }).then(r => {
    if (!r.ok) throw new Error(r.status.toString());
    return r.json();
  });

const send = (url: string, method: string, body?: unknown) =>
  fetch(url, {
    method,
    headers: { ...authHeaders(), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  }).then(r => {
    if (!r.ok) throw new Error(r.status.toString());
    return r.status === 204 ? null : r.json();
  });

const fmtMoney = (n: number) =>
  '$' + n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const fmtDate = (d: string | null) =>
  d ? new Date(d + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : '—';

const CATEGORIES = ['repair', 'improvement', 'supplies', 'utility', 'insurance', 'tax', 'hoa', 'mortgage', 'rent', 'other'];

// The distinction with money attached: a patched roof is deductible now, a replaced
// one depreciates over 27.5 years. Nothing in a bank feed can tell them apart, which
// is exactly why this is a question and not a default.
const TAX_TREATMENTS: { id: string; label: string }[] = [
  { id: 'unclassified', label: 'Not classified' },
  { id: 'deductible', label: 'Deductible expense' },
  { id: 'capital_improvement', label: 'Capital improvement' },
  { id: 'non_deductible', label: 'Not deductible' },
];

interface Property { id: string; address: string; city: string; state: string }
interface Entry {
  id: string; propertyId: string; category: string; amount: number; date: string;
  notes: string | null; vaultTransactionId: string | null; origin: string;
  status: string; matchConfidence: number | null; taxTreatment: string;
}
interface StatusRow {
  chargeId: string; propertyId: string; category: string; direction: string;
  dueDate: string; expected: number; actual: number | null; status: string;
}
interface Charge {
  id: string; propertyId: string; direction: string; category: string;
  amount: number; frequency: string; dueDay: number; matchHint: string | null; active: boolean;
}
interface Candidate {
  direction: string; amount: number; frequency: string; dueDay: number;
  matchHint: string; occurrences: number; variableAmount: boolean;
  sampleDescriptions: string[]; alreadyConfigured: boolean;
}

const addressOf = (props: Property[] | undefined, id: string) => {
  const p = props?.find(x => x.id === id);
  return p ? `${p.address}, ${p.city}` : 'Unknown property';
};

/* ── San's proposals, awaiting a decision ── */
function ReviewQueue({ properties }: { properties: Property[] | undefined }) {
  const qClient = useQueryClient();
  const [edits, setEdits] = useState<Record<string, { propertyId?: string; category?: string; taxTreatment?: string }>>({});

  const pendingQ = useQuery<Entry[]>({ queryKey: ['ledger-pending'], queryFn: () => get(`${API}/api/ledger/pending`) });

  const invalidate = () => {
    qClient.invalidateQueries({ queryKey: ['ledger-pending'] });
    qClient.invalidateQueries({ queryKey: ['financials'] });
  };

  const confirm = useMutation({
    mutationFn: (id: string) => send(`${API}/api/ledger/${id}/confirm`, 'POST', edits[id] ?? {}),
    onSuccess: invalidate,
  });
  const reject = useMutation({
    mutationFn: (id: string) => send(`${API}/api/ledger/${id}/reject`, 'POST'),
    onSuccess: invalidate,
  });

  const pending = pendingQ.data ?? [];
  const edit = (id: string, patch: Partial<{ propertyId: string; category: string; taxTreatment: string }>) =>
    setEdits(e => ({ ...e, [id]: { ...e[id], ...patch } }));

  if (pendingQ.isLoading) return null;

  if (pending.length === 0) {
    return (
      <div className="led-section">
        <div className="aasthi-section-label">Needs review</div>
        <div className="led-empty">Nothing waiting. San adds anything it cannot place on its own.</div>
      </div>
    );
  }

  return (
    <div className="led-section">
      <div className="aasthi-section-label">
        Needs review <span className="led-badge">{pending.length}</span>
      </div>

      <div className="led-list">
        {pending.map(e => {
          const p = edits[e.id] ?? {};
          return (
            <div key={e.id} className="led-row led-row-review">
              <div className="led-row-main">
                <div className="led-desc">{e.notes || 'Transaction'}</div>
                <div className="led-meta">
                  {fmtDate(e.date)} · {fmtMoney(e.amount)}
                  {e.origin === 'san' && <span className="led-tag">proposed by San</span>}
                  {e.origin === 'recurring' && (
                    <span className="led-tag">
                      matched{e.matchConfidence != null ? ` · ${e.matchConfidence}%` : ''}
                    </span>
                  )}
                </div>
              </div>

              {/* Correcting on confirm rather than forcing a reject-and-re-enter: San
                  often has the property right and the category wrong. */}
              <div className="led-controls">
                <select value={p.propertyId ?? e.propertyId} onChange={ev => edit(e.id, { propertyId: ev.target.value })}>
                  {properties?.map(pr => (
                    <option key={pr.id} value={pr.id}>{pr.address}</option>
                  ))}
                </select>
                <select value={p.category ?? e.category} onChange={ev => edit(e.id, { category: ev.target.value })}>
                  {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
                </select>
                <select value={p.taxTreatment ?? e.taxTreatment} onChange={ev => edit(e.id, { taxTreatment: ev.target.value })}>
                  {TAX_TREATMENTS.map(t => <option key={t.id} value={t.id}>{t.label}</option>)}
                </select>
              </div>

              <div className="led-actions">
                <button className="btn-primary" disabled={confirm.isPending} onClick={() => confirm.mutate(e.id)}>Confirm</button>
                <button className="btn-danger-ghost" disabled={reject.isPending} onClick={() => reject.mutate(e.id)}>Not this</button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ── Expected against actual ── */
function RentStatus({ properties }: { properties: Property[] | undefined }) {
  const qClient = useQueryClient();
  const statusQ = useQuery<StatusRow[]>({ queryKey: ['charge-status'], queryFn: () => get(`${API}/api/recurring-charges/status`) });

  const reconcile = useMutation({
    mutationFn: () => send(`${API}/api/ledger/reconcile`, 'POST'),
    onSuccess: () => {
      qClient.invalidateQueries({ queryKey: ['charge-status'] });
      qClient.invalidateQueries({ queryKey: ['ledger-pending'] });
    },
  });

  const rows = statusQ.data ?? [];

  return (
    <div className="led-section">
      <div className="led-section-head">
        <div className="aasthi-section-label">This month</div>
        <button className="btn-ghost" disabled={reconcile.isPending} onClick={() => reconcile.mutate()}>
          {reconcile.isPending ? 'Checking…' : 'Check the bank'}
        </button>
      </div>

      {rows.length === 0 ? (
        <div className="led-empty">
          No recurring charges set up yet. Add rent and a mortgage below and this fills in.
        </div>
      ) : (
        <div className="led-list">
          {rows.map(r => (
            <div key={`${r.chargeId}-${r.dueDate}`} className="led-row">
              <div className="led-row-main">
                <div className="led-desc">
                  {r.category} <span className="led-dim">· {addressOf(properties, r.propertyId)}</span>
                </div>
                <div className="led-meta">due {fmtDate(r.dueDate)}</div>
              </div>
              <div className="led-amounts">
                <span className="led-expected">{fmtMoney(r.expected)}</span>
                {r.actual != null && r.actual !== r.expected && (
                  <span className="led-actual">got {fmtMoney(r.actual)}</span>
                )}
              </div>
              <span className={`led-chip led-${r.status}`}>{r.status}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/* ── The rules, and finding them in the bank feed ── */
function Charges({ properties }: { properties: Property[] | undefined }) {
  const qClient = useQueryClient();
  const [detecting, setDetecting] = useState(false);
  const [picks, setPicks] = useState<Record<string, string>>({});

  const chargesQ = useQuery<Charge[]>({ queryKey: ['charges'], queryFn: () => get(`${API}/api/recurring-charges`) });
  const detectQ = useQuery<{ scanned: number; candidates: Candidate[] }>({
    queryKey: ['charge-detect'],
    queryFn: () => get(`${API}/api/recurring-charges/detect?months=12`),
    enabled: detecting,
  });

  const invalidate = () => {
    qClient.invalidateQueries({ queryKey: ['charges'] });
    qClient.invalidateQueries({ queryKey: ['charge-status'] });
    qClient.invalidateQueries({ queryKey: ['charge-detect'] });
  };

  const create = useMutation({
    mutationFn: (c: Candidate) => send(`${API}/api/recurring-charges`, 'POST', {
      propertyId: picks[c.matchHint],
      direction: c.direction,
      // The detector finds the pattern; it never claims to know what KIND of charge it
      // is, because that needs to know whose life this is. Rent is the one safe guess
      // for money coming in from a property owner's account.
      category: c.direction === 'income' ? 'rent' : 'other',
      amount: c.amount,
      frequency: c.frequency,
      dueDay: c.dueDay,
      startDate: new Date().toISOString().slice(0, 10),
      matchHint: c.matchHint,
    }),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (id: string) => send(`${API}/api/recurring-charges/${id}`, 'DELETE'),
    onSuccess: invalidate,
  });

  const charges = chargesQ.data ?? [];
  const candidates = (detectQ.data?.candidates ?? []).filter(c => !c.alreadyConfigured);

  return (
    <div className="led-section">
      <div className="led-section-head">
        <div className="aasthi-section-label">Recurring charges</div>
        <button className="btn-ghost" onClick={() => setDetecting(d => !d)}>
          {detecting ? 'Hide suggestions' : 'Find them in my bank'}
        </button>
      </div>

      <div className="led-list">
        {charges.map(c => (
          <div key={c.id} className="led-row">
            <div className="led-row-main">
              <div className="led-desc">
                {c.category} <span className="led-dim">· {addressOf(properties, c.propertyId)}</span>
              </div>
              <div className="led-meta">
                {c.frequency}, day {c.dueDay}
                {c.matchHint && <span className="led-hint">“{c.matchHint}”</span>}
              </div>
            </div>
            <span className="led-expected">{fmtMoney(c.amount)}</span>
            <button className="btn-danger-ghost" onClick={() => remove.mutate(c.id)}>x</button>
          </div>
        ))}
        {charges.length === 0 && <div className="led-empty">Nothing set up yet.</div>}
      </div>

      {detecting && (
        <div className="led-suggestions">
          {detectQ.isLoading && <div className="led-empty">Reading a year of transactions…</div>}
          {detectQ.isError && <div className="led-empty">Couldn’t reach Vault for transactions.</div>}
          {detectQ.data && candidates.length === 0 && (
            <div className="led-empty">
              Nothing repeating found that isn’t already set up (scanned {detectQ.data.scanned}).
            </div>
          )}

          {candidates.map(c => (
            <div key={c.matchHint} className="led-row led-row-suggest">
              <div className="led-row-main">
                <div className="led-desc">
                  {c.matchHint} <span className="led-dim">· {c.direction === 'income' ? 'incoming' : 'outgoing'}</span>
                </div>
                <div className="led-meta">
                  {fmtMoney(c.amount)} {c.frequency}, day {c.dueDay} · seen {c.occurrences}×
                  {c.variableAmount && <span className="led-tag">amount varies</span>}
                </div>
                <div className="led-sample">{c.sampleDescriptions[0]}</div>
              </div>

              {/* Which property this belongs to is the one thing the detector cannot
                  work out, so it is the one thing asked for here. */}
              <select
                value={picks[c.matchHint] ?? ''}
                onChange={ev => setPicks(p => ({ ...p, [c.matchHint]: ev.target.value }))}
              >
                <option value="">Which property?</option>
                {properties?.map(pr => <option key={pr.id} value={pr.id}>{pr.address}</option>)}
              </select>

              <button className="btn-primary" disabled={!picks[c.matchHint] || create.isPending} onClick={() => create.mutate(c)}>
                Add
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default function LedgerPage() {
  const propertiesQ = useQuery<Property[]>({ queryKey: ['properties'], queryFn: () => get(`${API}/api/properties`) });

  if (propertiesQ.isError) {
    return (
      <div className="module-empty" style={style}>
        <h2>Can’t reach Aasthi API</h2>
        <p>Make sure the Aasthi backend is running on port 5200.</p>
      </div>
    );
  }

  return (
    <div style={style}>
      <ReviewQueue properties={propertiesQ.data} />
      <RentStatus properties={propertiesQ.data} />
      <Charges properties={propertiesQ.data} />
    </div>
  );
}

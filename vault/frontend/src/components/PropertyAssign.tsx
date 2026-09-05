import { useCallback, useEffect, useState } from 'react';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';

// Assigning a bank transaction to a property, from the transaction list.
//
// The link is stored in Aasthi, not as a field on Vault's transaction. Vault mirrors
// Plaid and re-syncs, so a column here could be dropped underneath the user; and one
// hardware-store run can be split across two properties, which only works when several
// ledger entries point at one transaction rather than one property pointing at it.
//
// So the interaction reads as "edit this transaction" while the data lives where it
// survives a resync.

const AASTHI = moduleApi(5200);

export interface AssignedEntry {
  id: string; propertyId: string; category: string;
  amount: number; status: string; origin: string; taxTreatment: string;
}
interface Property { id: string; address: string; city: string }

const CATEGORIES = ['repair', 'improvement', 'supplies', 'utility', 'insurance', 'tax', 'hoa', 'mortgage', 'rent', 'other'];

// Properties and existing links, fetched once for the whole list rather than per row.
export function usePropertyAssignments() {
  const [properties, setProperties] = useState<Property[]>([]);
  const [assignments, setAssignments] = useState<Record<string, AssignedEntry[]>>({});
  const [available, setAvailable] = useState(true);

  const load = useCallback(async () => {
    try {
      const [props, links] = await Promise.all([
        fetch(`${AASTHI}/api/properties`, { headers: authHeaders() }).then(r => r.ok ? r.json() : Promise.reject(r.status)),
        fetch(`${AASTHI}/api/ledger/assignments`, { headers: authHeaders() }).then(r => r.ok ? r.json() : Promise.reject(r.status)),
      ]);

      setProperties(props);
      setAssignments(Object.fromEntries(
        (links as { vaultTransactionId: string; entries: AssignedEntry[] }[])
          .map(l => [l.vaultTransactionId, l.entries])));
      setAvailable(true);
    } catch {
      // Aasthi being down must not take the transaction list with it. The column
      // simply does not render.
      setAvailable(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return { properties, assignments, available, refresh: load };
}

interface Props {
  transactionId: string;
  amount: number;          // Vault convention: positive is money out
  transactionDate: string;
  entries: AssignedEntry[] | undefined;
  properties: Property[];
  onAssigned: () => void;
}

export function PropertyCell({ transactionId, amount, transactionDate, entries, properties, onAssigned }: Props) {
  const [open, setOpen] = useState(false);
  const [propertyId, setPropertyId] = useState('');
  const [category, setCategory] = useState('repair');
  const [saving, setSaving] = useState(false);

  const assigned = entries ?? [];
  const addressOf = (id: string) => properties.find(p => p.id === id)?.address ?? 'Unknown';

  const save = async () => {
    if (!propertyId) return;
    setSaving(true);
    try {
      await fetch(`${AASTHI}/api/ledger/assign`, {
        method: 'POST',
        headers: { ...authHeaders(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
          vaultTransactionId: transactionId,
          propertyId,
          amount: Math.abs(amount),
          date: transactionDate.slice(0, 10),
          type: amount > 0 ? 'expense' : 'income',
          category,
          // Left blank deliberately: a repair and a capital improvement look identical
          // from here, and the difference is depreciation. Set it in Aasthi against
          // the receipt, not from a guess in a list view.
          taxTreatment: 'unclassified',
        }),
      });
      setOpen(false);
      setPropertyId('');
      onAssigned();
    } finally {
      setSaving(false);
    }
  };

  if (assigned.length > 0 && !open) {
    const pending = assigned.some(e => e.status === 'pending');
    return (
      <button
        className={`txn-prop-tag ${pending ? 'txn-prop-pending' : ''}`}
        title={assigned.map(e => `${addressOf(e.propertyId)} · ${e.category} · ${e.status}`).join('\n')}
        onClick={() => setOpen(true)}
      >
        {assigned.length > 1 ? `${assigned.length} properties` : addressOf(assigned[0].propertyId)}
        {pending && <span className="txn-prop-dot" />}
      </button>
    );
  }

  if (!open) {
    return <button className="txn-prop-add" onClick={() => setOpen(true)}>+ property</button>;
  }

  return (
    <div className="txn-prop-editor">
      <select value={propertyId} onChange={e => setPropertyId(e.target.value)} autoFocus>
        <option value="">Property…</option>
        {properties.map(p => <option key={p.id} value={p.id}>{p.address}</option>)}
      </select>
      <select value={category} onChange={e => setCategory(e.target.value)}>
        {CATEGORIES.map(c => <option key={c} value={c}>{c}</option>)}
      </select>
      <div className="txn-prop-editor-actions">
        <button className="txn-prop-save" disabled={!propertyId || saving} onClick={save}>
          {saving ? '…' : 'Save'}
        </button>
        <button className="txn-prop-cancel" onClick={() => { setOpen(false); setPropertyId(''); }}>Cancel</button>
      </div>
    </div>
  );
}

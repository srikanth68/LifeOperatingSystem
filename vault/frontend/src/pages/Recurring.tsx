import { QueryClientProvider, useQuery } from '@tanstack/react-query';
import { makeModuleQueryClient } from '../services/moduleQuery';
import { authHeaders } from '../services/auth';
import { moduleApi } from '../services/apiHost';
import '../styles/recurring.css';

const API = moduleApi(5000);

// Vault's pages predate React Query and the module has no provider of its own, so this
// page brings one — the same pattern Vitara and Insight use. Recurring charges change
// once a day at most, so the cache is generous.
const qc = makeModuleQueryClient(10 * 60_000);

// What bills you whether you use it or not.
//
// The useful thing here is not the list — it is the annual total, which nobody can
// produce from memory and which the bank will never show you, because it is spread
// across twelve months in alphabetical company names.
//
// Three things get lifted out of the list because they are the ones worth acting on
// and the ones a statement hides best: a price that went up months ago, something
// billing in the next week, and something that stopped arriving.

interface Charge {
  merchant: string;
  amount: number;
  cadence: string;
  occurrences: number;
  category: string | null;
  status: 'active' | 'due' | 'overdue' | 'lapsed';
  note: string;
  lastCharged: string;
  nextExpected: string;
  annualCost: number;
  previousAmount: number | null;
  priceChangedOn: string | null;
  increase: number | null;
}

interface Recurring {
  windowDays: number;
  transactionsExamined: number;
  verdict: string;
  monthlyTotal: number;
  annualTotal: number;
  priceRises: Charge[];
  dueSoon: Charge[];
  lapsed: Charge[];
  charges: Charge[];
  method: string;
}

const money = (n: number) =>
  n.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: n % 1 === 0 ? 0 : 2 });

export default function Recurring() {
  return (
    <QueryClientProvider client={qc}>
      <RecurringInner />
    </QueryClientProvider>
  );
}

function RecurringInner() {
  const { data, isPending, isError } = useQuery<Recurring>({
    queryKey: ['recurring'],
    queryFn: async () => {
      const r = await fetch(`${API}/api/recurring`, { headers: authHeaders() });
      if (!r.ok) throw new Error(`${r.status}`);
      return r.json();
    },
  });

  if (isPending) return <p className="rc-muted">Reading the statement…</p>;
  if (isError || !data) return <p className="rc-muted">Couldn't load recurring charges.</p>;

  return (
    <div className="rc">
      {/* The number nobody has. */}
      <section className="rc-hero">
        <div>
          <p className="rc-eyebrow">Repeating charges, a year</p>
          <p className="rc-total">{money(data.annualTotal)}</p>
          <p className="rc-sub">{money(data.monthlyTotal)} a month · {data.verdict}</p>
        </div>
        <p className="rc-method">{data.method}</p>
      </section>

      {data.priceRises.length > 0 && (
        <Group
          title="Went up in price"
          note="The change a statement hides best: one number, months ago, by a couple of pounds."
          charges={data.priceRises}
          highlight
        />
      )}

      {data.dueSoon.length > 0 && (
        <Group title="Billing in the next week" charges={data.dueSoon} />
      )}

      {data.lapsed.length > 0 && (
        <Group
          title="Stopped arriving"
          note="Either a cancellation you made, or one you did not. Not counted in the yearly total above."
          charges={data.lapsed}
        />
      )}

      <Group
        title="Everything repeating"
        note={`${data.transactionsExamined.toLocaleString()} transactions over ${Math.round(data.windowDays / 30)} months.`}
        charges={data.charges}
      />
    </div>
  );
}

function Group({ title, note, charges, highlight }: {
  title: string;
  note?: string;
  charges: Charge[];
  highlight?: boolean;
}) {
  if (charges.length === 0) return null;

  return (
    <section className={`rc-group ${highlight ? 'is-highlight' : ''}`}>
      <header>
        <h3>{title}</h3>
        <span>{charges.length}</span>
      </header>
      {note && <p className="rc-note">{note}</p>}

      <ul className="rc-list">
        {charges.map(c => (
          <li key={c.merchant + c.lastCharged} className={`rc-item status-${c.status}`}>
            <span className="rc-name">
              <b>{c.merchant}</b>
              <em>{c.cadence} · {c.occurrences} charges{c.category ? ` · ${c.category}` : ''}</em>
            </span>

            <span className="rc-amount">
              {c.increase != null && c.increase > 0 && (
                <s>{money(c.previousAmount!)}</s>
              )}
              <b>{money(c.amount)}</b>
              <em>{money(c.annualCost)}/yr</em>
            </span>

            <span className="rc-when">{c.note}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

import { useState, useEffect } from 'react';
import axios from 'axios';

interface LinkedItem {
  id: string;
  institutionName: string;
  plaidInstitutionId: string;
  createdAt: string;
}

interface Props {
  refreshKey: number;
}

export default function LinkedAccounts({ refreshKey }: Props) {
  const [items, setItems] = useState<LinkedItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    axios.get('/api/plaid/items')
      .then(res => setItems(res.data))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [refreshKey]);

  const handleUnlink = async (id: string, name: string) => {
    if (!window.confirm(`Unlink ${name}? This removes all associated accounts and data.`)) return;
    await axios.delete(`/api/plaid/items/${id}`);
    setItems(prev => prev.filter(i => i.id !== id));
  };

  if (loading) return null;
  if (!items.length) return (
    <p className="text-muted" style={{ marginTop: '0.75rem' }}>
      No accounts linked yet.
    </p>
  );

  return (
    <div className="linked-accounts">
      {items.map(item => (
        <div key={item.id} className="linked-item">
          <div>
            <p className="linked-name">{item.institutionName}</p>
            <small className="text-muted">
              Linked {new Date(item.createdAt).toLocaleDateString()}
            </small>
          </div>
          <button
            className="btn-unlink"
            onClick={() => handleUnlink(item.id, item.institutionName)}
          >
            Unlink
          </button>
        </div>
      ))}
    </div>
  );
}

import { useCallback, useState } from 'react';
import { usePlaidLink } from 'react-plaid-link';
import axios from 'axios';

interface Props {
  onSuccess: () => void;
}

export default function PlaidLinkButton({ onSuccess }: Props) {
  const [linkToken, setLinkToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchLinkToken = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await axios.post('/api/plaid/link-token');
      setLinkToken(res.data.linkToken);
    } catch (err) {
      setError('Failed to start bank connection. Check API is running.');
      setLoading(false);
    }
  };

  const handleSuccess = useCallback(async (publicToken: string, metadata: any) => {
    try {
      setLoading(true);
      await axios.post('/api/plaid/exchange-token', {
        publicToken,
        plaidInstitutionId: metadata.institution?.institution_id ?? '',
        institutionName: metadata.institution?.name ?? 'Unknown'
      });
      setLinkToken(null);
      onSuccess();
    } catch (err) {
      setError('Failed to link account. Please try again.');
    } finally {
      setLoading(false);
    }
  }, [onSuccess]);

  const handleExit = useCallback(() => {
    setLinkToken(null);
    setLoading(false);
  }, []);

  const { open, ready } = usePlaidLink({
    token: linkToken ?? '',
    onSuccess: handleSuccess,
    onExit: handleExit,
  });

  // Auto-open once we have the token
  if (linkToken && ready) {
    open();
  }

  return (
    <div className="plaid-link-wrapper">
      {error && <p className="plaid-error">{error}</p>}
      <button
        className="btn-primary"
        onClick={fetchLinkToken}
        disabled={loading}
      >
        {loading ? 'Connecting...' : '+ Connect Bank Account'}
      </button>
    </div>
  );
}

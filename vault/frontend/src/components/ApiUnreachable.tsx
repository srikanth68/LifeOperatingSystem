/**
 * Full-page state shown when a module's backend can't be reached.
 * Used by modules that fetch with raw fetch/authFetch (no react-query error UI).
 */
export function ApiUnreachable({ name, port, mc, onRetry }: {
  name: string;
  port: number;
  mc?: string;            // module accent color, e.g. 'var(--karma)'
  onRetry?: () => void;
}) {
  return (
    <div className="module-empty" style={mc ? ({ '--mc': mc } as React.CSSProperties) : undefined}>
      <div className="module-empty-icon">⚠️</div>
      <h2>Can't reach {name}</h2>
      <p>
        The {name} backend isn't responding on port {port}. Start the full stack with{' '}
        <code>maaya-start.ps1</code> and try again.
      </p>
      {onRetry && (
        <button className="module-retry-btn" onClick={onRetry}>Retry</button>
      )}
    </div>
  );
}

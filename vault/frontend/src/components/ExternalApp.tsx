import { useEffect } from 'react';

// A module that has moved out of Maaya into an app of its own.
//
// Vitara and Insight are heading for a product of their own, and the first step is that they
// no longer render inside this shell. The sidebar entries stay (every habit and shortcut that
// points at them keeps working) and land here, on one screen with one button, instead of on a
// second copy of the page that would drift from the real one.
//
// The address is derived from where Maaya itself was reached, so it works on localhost, on the
// LAN and over the mesh without configuration; VITE_VITARA_URL overrides it when the app is
// served somewhere else. Plain http on purpose: it is a link to another origin, not a request
// from this page, so there is no mixed-content rule to trip even when Maaya is on https.

const VITARA_PORT = 3100;

export const vitaraUrl = (section?: 'insight'): string => {
  const configured = import.meta.env.VITE_VITARA_URL as string | undefined;
  const base = configured?.trim() || `http://${window.location.hostname}:${VITARA_PORT}`;
  return section ? `${base.replace(/\/$/, '')}/#${section}` : base;
};

export default function ExternalApp({ name, section, blurb }: {
  name: string;
  section?: 'insight';
  blurb: string;
}) {
  const href = vitaraUrl(section);

  // Cosmetic: the page title says where you are going, not where you are.
  useEffect(() => {
    const previous = document.title;
    document.title = `${name} · Maaya`;
    return () => { document.title = previous; };
  }, [name]);

  return (
    <div className="card" style={{ maxWidth: 560, margin: '3rem auto', textAlign: 'center', padding: '2.2rem 1.8rem' }}>
      <h1 style={{ marginBottom: '0.5rem' }}>{name} has its own app now</h1>
      <p className="text-muted" style={{ lineHeight: 1.6, marginBottom: '1.4rem' }}>{blurb}</p>
      <a className="btn-primary" href={href} target="_blank" rel="noopener noreferrer"
         style={{ display: 'inline-block', padding: '0.7rem 1.4rem', textDecoration: 'none', borderRadius: 'var(--r-md)' }}>
        Open {name} ↗
      </a>
      <p className="text-dim" style={{ marginTop: '1rem', wordBreak: 'break-all' }}>{href}</p>
    </div>
  );
}

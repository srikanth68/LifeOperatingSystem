// Every module API is reached through a same-origin proxy path (/svc/<module>),
// served by nginx in production and the Vite dev server locally. Same-origin means:
//   - inherits the page's protocol → an HTTPS dashboard never hits mixed-content
//     blocks (required for the mic / getUserMedia to work over Meshnet)
//   - no CORS preflight, no per-host allow-lists (fixes the .nord / IP origin pain)
//   - the browser only ever talks to ONE local address — nothing fans out
const PORT_TO_MODULE: Record<number, string> = {
  5000: 'vault', 5100: 'vitara', 5200: 'aasthi', 5300: 'san',
  5400: 'sutra', 5500: 'northstar', 5600: 'karma', 5700: 'nexus',
};

export const moduleApi = (port: number): string => {
  const mod = PORT_TO_MODULE[port];
  // Fallback to the old direct-host form only for any unmapped port.
  return mod ? `/svc/${mod}` : `http://${window.location.hostname}:${port}`;
};

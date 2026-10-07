// Every backend is reached through a same-origin proxy path (/svc/<name>), served by nginx in
// production and the Vite dev server locally. Same-origin means no CORS, no mixed content, and
// the browser only ever talks to ONE address.
//
// Only three things are reachable from this app, and that is deliberate:
//   vault    the sign-in server (auth only; nginx forwards nothing else from it)
//   vitara   readings, labs, profiles
//   insight  what the readings mean
// The port numbers are the services' own, kept so the pages read the same as they did when
// they lived inside Maaya.
const PORT_TO_SERVICE: Record<number, string> = {
  5000: 'vault',
  5100: 'vitara',
  5110: 'insight',
};

export const moduleApi = (port: number): string => {
  const name = PORT_TO_SERVICE[port];
  if (!name) throw new Error(`No route is defined for port ${port}.`);
  return `/svc/${name}`;
};

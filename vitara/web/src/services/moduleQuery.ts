import { QueryClient, onlineManager } from '@tanstack/react-query';

// Maaya modules are local/LAN tools. The browser's online/offline detection is
// unreliable in this context and can make react-query silently *pause* failed
// requests (fetchStatus 'paused', status stuck 'pending') instead of erroring —
// which hides "API is down" from the UI entirely. Force react-query online so
// failures actually surface.
onlineManager.setOnline(true);

/**
 * Shared QueryClient for every module. Key choices:
 *  - retry:false + networkMode:'always' → a failed request errors immediately
 *    (no paused/pending limbo), so isError-based UI actually renders. Retrying a
 *    connection-refused localhost call is pointless anyway; the user can retry.
 *  - refetchOnWindowFocus:false → these are dashboards, not live feeds.
 */
export function makeModuleQueryClient(staleTime = 30_000) {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime, refetchOnWindowFocus: false, networkMode: 'always' },
      mutations: { networkMode: 'always' },
    },
  });
}

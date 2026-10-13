import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// Same-origin module routing: the frontend calls /svc/<module>/... (see
// src/services/apiHost.ts). In dev, Vite proxies each prefix to its backend port;
// in prod, nginx does the same (see vault/frontend/nginx.conf). Keeps the browser
// single-origin so an HTTPS page can call the APIs without mixed-content/CORS.
const modules: Record<string, number> = {
  vault: 5000, vitara: 5100, insight: 5110, aasthi: 5200, san: 5300,
  sutra: 5400, northstar: 5500, karma: 5600, nexus: 5700,
}

const svcProxy = Object.fromEntries(
  Object.entries(modules).map(([name, port]) => [
    `/svc/${name}`,
    {
      target: `http://localhost:${port}`,
      changeOrigin: true,
      rewrite: (p: string) => p.replace(new RegExp(`^/svc/${name}`), ''),
      // A backend that is not running answers 502, as nginx does in production. Vite's
      // default is a 500, which the UI would read as an error INSIDE the module.
      configure: (proxy: { on: (event: 'error', handler: (err: Error, req: unknown, res: unknown) => void) => void }) => {
        proxy.on('error', (_err, _req, res) => {
          const r = res as { headersSent?: boolean; writeHead?: (code: number) => void; end?: () => void };
          if (r.writeHead && !r.headersSent) r.writeHead(502);
          r.end?.();
        });
      },
    },
  ])
)

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    proxy: {
      // Vault's axios client still uses the bare /api prefix.
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
      ...svcProxy,
    },
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
})

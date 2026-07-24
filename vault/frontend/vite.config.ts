import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

// Same-origin module routing: the frontend calls /svc/<module>/... (see
// src/services/apiHost.ts). In dev, Vite proxies each prefix to its backend port;
// in prod, nginx does the same (see vault/frontend/nginx.conf). Keeps the browser
// single-origin so an HTTPS page can call the APIs without mixed-content/CORS.
const modules: Record<string, number> = {
  vault: 5000, vitara: 5100, aasthi: 5200, san: 5300,
  sutra: 5400, northstar: 5500, karma: 5600, nexus: 5700,
}

const svcProxy = Object.fromEntries(
  Object.entries(modules).map(([name, port]) => [
    `/svc/${name}`,
    {
      target: `http://localhost:${port}`,
      changeOrigin: true,
      rewrite: (p: string) => p.replace(new RegExp(`^/svc/${name}`), ''),
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

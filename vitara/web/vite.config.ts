import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Same-origin routing, as in production (see nginx.conf): the browser calls /svc/<name>/...
// and Vite forwards it to the service on this machine. Vault is proxied for sign-in ONLY;
// nothing else of Maaya is reachable from this app.
const target = (port: number) => ({ target: `http://localhost:${port}`, changeOrigin: true })
const strip = (name: string) => (p: string) => p.replace(new RegExp(`^/svc/${name}`), '')

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3100,
    proxy: {
      '/svc/vault/api/auth': { ...target(5000), rewrite: strip('vault') },
      '/svc/vitara': { ...target(5100), rewrite: strip('vitara') },
      '/svc/insight': { ...target(5110), rewrite: strip('insight') },
    },
  },
})

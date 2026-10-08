import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Same-origin routing, as in production (see nginx.conf): the browser calls /svc/<name>/...
// and Vite forwards it to the service on this machine. Vault is proxied for sign-in ONLY;
// nothing else of Maaya is reachable from this app.
const target = (port: number) => ({ target: `http://localhost:${port}`, changeOrigin: true })
const strip = (name: string) => (p: string) => p.replace(new RegExp(`^/svc/${name}`), '')

const proxy = {
  '/svc/vault/api/auth': { ...target(5000), rewrite: strip('vault') },
  '/svc/vitara': { ...target(5100), rewrite: strip('vitara') },
  '/svc/insight': { ...target(5110), rewrite: strip('insight') },
}

export default defineConfig({
  plugins: [react()],
  server: { port: 3100, proxy },
  // `vite preview` serves the production build, which is the only place the service worker runs,
  // so it needs the same routes to the services.
  preview: { port: 3100, proxy },
})

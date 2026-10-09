import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { isNative } from './mobile/native'
import './styles/base.css'
import './styles/app.css'

// Makes the app installable and quick to open. Production only: in development it would cache
// the dev server's files and fight hot reload. See public/sw.js for what it does and, as
// importantly, what it refuses to keep. Not in the native app either: there the app's files are
// already on the phone, inside the app.
if ('serviceWorker' in navigator && import.meta.env.PROD && !isNative()) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => { /* the app works without it */ });
  });
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)

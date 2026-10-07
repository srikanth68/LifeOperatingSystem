/// <reference types="vite/client" />

// Vite scaffolds this file into every new project and it was never added here, which
// is why `import.meta.env` had no type and every use of it was a compile error. Vite
// injects those values at build time, so without this declaration TypeScript is right
// to complain — the fix is telling it the shape, not casting at each call site.

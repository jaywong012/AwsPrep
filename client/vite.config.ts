import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],

  // strictPort matters more than it looks. localStorage is scoped per origin, so a silent
  // fallback to 5174 when 5173 is busy gives the app a different origin - no session, and an
  // origin the API's CORS list does not carry, so signing in fails as an opaque network error.
  // Before there were accounts, the same slip quietly minted a new anonymous learner and
  // stranded that browser's progress. Better to refuse to start.
  server: { port: 5173, strictPort: true },
})

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // The browser only ever talks to the Vite origin. Requests under /api are
    // forwarded to the .NET API, so no CORS is needed in development, and the
    // client's URLs are already the same-origin paths production will use.
    // Range headers pass through, so audio seeking works via the proxy too.
    proxy: {
      '/api': 'http://localhost:5043',
    },
  },
})

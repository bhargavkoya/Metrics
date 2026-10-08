import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Dev proxy so the browser calls same-origin /api; target is the local API.
    proxy: { '/api': 'http://localhost:5223' },
  },
})

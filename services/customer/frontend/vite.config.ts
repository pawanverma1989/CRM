import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// The app is served behind nginx under the /customer/ path prefix, so hashed
// assets must resolve to /customer/assets/... and React Router runs with
// basename="/customer".
export default defineConfig({
  base: '/customer/',
  plugins: [react()],
  server: {
    port: 3002,
    proxy: {
      '/api': {
        target: 'http://localhost:5002',
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})

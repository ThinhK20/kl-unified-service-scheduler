/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  preview: { port: 5173 },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Run tests in a zone far from the dealership (Asia/Ho_Chi_Minh) to prove
    // the UI never uses the browser time zone.
    env: { TZ: 'America/New_York' },
    css: false,
    restoreMocks: true,
  },
});

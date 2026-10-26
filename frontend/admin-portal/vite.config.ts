import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Fixed ports: the Admin Portal always runs on 5173 and fails loudly if it is taken.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  preview: { port: 5173, strictPort: true },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
});

import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// Fixed ports: the Author Portal always runs on 5174 and fails loudly if it is taken.
export default defineConfig({
  plugins: [react()],
  server: { port: 5174, strictPort: true },
  preview: { port: 5174, strictPort: true },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
  },
});

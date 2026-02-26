import path from 'path';
import checker from 'vite-plugin-checker';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';

// ----------------------------------------------------------------------

const PORT = 8081;

export default defineConfig({
  plugins: [
    react(),
    checker({
      typescript: true,
      eslint: {
        useFlatConfig: true,
        lintCommand: 'eslint "./src/**/*.{js,jsx,ts,tsx}"',
        dev: { logLevel: ['error'] },
      },
      overlay: {
        position: 'tl',
        initialIsOpen: false,
      },
    }),
  ],
  resolve: {
    alias: [
      {
        find: /^~(.+)/,
        replacement: path.resolve(process.cwd(), 'node_modules/$1'),
      },
      {
        find: 'src',
        replacement: path.resolve(__dirname, 'src'),
      },
    ],
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) return;

          if (id.includes('react') || id.includes('scheduler') || id.includes('react-router')) {
            return 'vendor-react';
          }

          if (id.includes('@mui/x-data-grid')) {
            return 'vendor-grid';
          }

          if (id.includes('@mui/icons-material')) {
            return 'vendor-mui-icons';
          }

          if (id.includes('@mui') || id.includes('@emotion') || id.includes('@popperjs')) {
            return 'vendor-mui';
          }

          if (id.includes('dayjs') || id.includes('date-fns')) {
            return 'vendor-dates';
          }

          if (id.includes('lodash') || id.includes('clsx') || id.includes('axios')) {
            return 'vendor-utils';
          }

          return 'vendor-misc';
        },
      },
    },
  },
  server: {
    port: PORT,
    host: true,
    proxy: {
      '/api': {
        target: process.env.VITE_API_BASE_URL || 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  preview: { port: PORT, host: true },
});

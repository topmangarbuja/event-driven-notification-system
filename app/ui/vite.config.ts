import {defineConfig} from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// https://vite.dev/config/
export default defineConfig({
    plugins: [react(), tailwindcss()],
    server: {
        // Aspire injects VITE_PORT when running under the app host. The fallback is Vite's own
        // default and only applies to standalone `vite` runs; strictPort makes a busy port fail
        // rather than drift, which would break the proxy Aspire is pointed at.
        port: Number(process.env.VITE_PORT ?? 5173),
        strictPort: true,
        proxy: {
            '/api': {
                // Aspire always injects API_URL (the API endpoint it allocated), so this
                // fallback only applies when running `vite` standalone. It must match the
                // "http" profile's applicationUrl in app/api/src/Properties/launchSettings.json.
                target: process.env.API_URL ?? 'http://localhost:5139',
                changeOrigin: true,
            }
        }
    },
    test: {
        environment: 'jsdom',
        globals: true,
        setupFiles: ['./src/setupTests.ts'],
    }
})

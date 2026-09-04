import { defineConfig } from 'vite';

export default defineConfig({
    root: 'web',
    base: '/web/',
    build: {
        outDir: '../web-dist',
        emptyOutDir: true,
        cssCodeSplit: false,
        rollupOptions: {
            output: {
                entryFileNames: 'controller.js',
                assetFileNames: '[name][extname]',
            },
        },
    },
});

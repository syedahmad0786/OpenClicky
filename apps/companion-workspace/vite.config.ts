import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { resolve } from "node:path";

export default defineConfig({
  plugins: [react()],
  publicDir: "../../assets",
  base: "./",
  build: {
    outDir: "dist",
    emptyOutDir: true,
    rollupOptions: { input: { main: resolve(__dirname, "index.html"), shell: resolve(__dirname, "shell.html") } },
  },
});

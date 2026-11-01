import { defineConfig } from "vitest/config";

export default defineConfig({
  test: { environment: "jsdom", setupFiles: "./src/test-setup.ts", server: { deps: { inline: ["@copilotkit/react-core"] } } },
});

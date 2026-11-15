import { defineConfig } from '@playwright/test';

// Guard tests use only ephemeral local HTTP fixtures; no browser or production credentials.
export default defineConfig({ testDir: './guards', workers: 1, reporter: 'list' });

// Disposable localhost fixture only. Never creates sessions or imports cleanup.
const assert = require('node:assert/strict');
const { chromium, expect } = require('@playwright/test');

async function main() {
  const [client, api] = process.argv.slice(2);
  for (const raw of [client, api]) {
    const url = new URL(raw);
    assert.equal(url.protocol, 'http:');
    assert.equal(url.hostname, '127.0.0.1');
    assert.equal(url.pathname, '/');
    assert(Number(url.port) >= 1024);
    assert.equal(url.username + url.password + url.search + url.hash, '');
  }
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    await page.goto(client);
    await expect(page.locator('.maintenance-notice')).toBeVisible({ timeout: 30000 });
    const response = await page.request.post(new URL('/lobby/create-lobby', api).href, { data: {} });
    assert.equal(response.status(), 503);
    assert.equal((await response.json()).maintenance, true);
    assert.equal((await page.request.get(new URL('/health/ready', api).href)).status(), 200);
    console.log('PASS: fresh browser maintenance guidance and server-enforced admission after replacement.');
  } finally {
    await browser.close();
  }
}

main().catch(() => { console.error('Disposable maintenance browser check failed.'); process.exitCode = 1; });

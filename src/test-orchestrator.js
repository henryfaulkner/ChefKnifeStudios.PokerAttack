/**
 * Playwright-based multi-agent test orchestrator for PokerAttack
 *
 * Usage:
 *   npm install playwright
 *   node test-orchestrator.js
 *
 * This script launches multiple browser instances with AI agents enabled
 * to test concurrent gameplay and identify sync issues.
 */

const { chromium } = require('playwright');

// Configuration
const CONFIG = {
    baseUrl: 'https://localhost:7150',  // Update to match your local dev server
    numAgents: 4,                       // Number of agent browsers to launch
    headless: false,                    // Set to true for CI/CD environments
    gameTimeoutMs: 600000,              // 10 minutes max per game
    screenshotOnError: true,
    videoPath: './test-videos'
};

/**
 * Main test execution function
 */
async function runMultiAgentTest() {
    console.log(`[Orchestrator] Starting multi-agent test with ${CONFIG.numAgents} agents`);
    console.log(`[Orchestrator] Base URL: ${CONFIG.baseUrl}`);

    const browsers = [];
    const contexts = [];
    const pages = [];

    try {
        // Launch browser instances
        for (let i = 0; i < CONFIG.numAgents; i++) {
            console.log(`[Orchestrator] Launching agent ${i + 1}...`);

            const browser = await chromium.launch({
                headless: CONFIG.headless,
                args: ['--ignore-certificate-errors'] // For localhost SSL
            });

            const context = await browser.newContext({
                recordVideo: CONFIG.videoPath ? {
                    dir: CONFIG.videoPath,
                    size: { width: 1280, height: 720 }
                } : undefined,
                viewport: { width: 1280, height: 720 }
            });

            const page = await context.newPage();

            // Listen for console messages from the browser
            page.on('console', msg => {
                const text = msg.text();
                // Filter for agent-specific logs
                if (text.includes('[AutoPilot]') || text.includes('[LobbyAutomation]')) {
                    console.log(`[Agent ${i + 1}] ${text}`);
                }
            });

            // Listen for page errors
            page.on('pageerror', error => {
                console.error(`[Agent ${i + 1}] Page Error:`, error.message);
            });

            // Navigate to the lobbies page with autoPlay enabled
            console.log(`[Agent ${i + 1}] Navigating to ${CONFIG.baseUrl}/?autoPlay=true`);
            await page.goto(`${CONFIG.baseUrl}/?autoPlay=true`, {
                waitUntil: 'networkidle',
                timeout: 30000
            });

            browsers.push(browser);
            contexts.push(context);
            pages.push(page);

            // Stagger agent launches slightly to avoid thundering herd
            await new Promise(resolve => setTimeout(resolve, 1000));
        }

        console.log(`[Orchestrator] All ${CONFIG.numAgents} agents launched successfully`);
        console.log(`[Orchestrator] Waiting for game completion (max ${CONFIG.gameTimeoutMs / 1000}s)...`);

        // Wait for game completion or timeout
        const startTime = Date.now();
        while (Date.now() - startTime < CONFIG.gameTimeoutMs) {
            // Check if all agents have returned to lobby (game complete)
            const allInLobby = await Promise.all(
                pages.map(page => page.url().includes('/?') || page.url().endsWith('/'))
            );

            if (allInLobby.every(inLobby => inLobby)) {
                console.log('[Orchestrator] All agents returned to lobby - game complete!');
                break;
            }

            // Check if agents are still in gameplay
            const anyInGameplay = await Promise.all(
                pages.map(page => page.url().includes('/gameplay'))
            );

            if (anyInGameplay.some(inGame => inGame)) {
                console.log('[Orchestrator] Agents still in game...');
            }

            await new Promise(resolve => setTimeout(resolve, 5000)); // Check every 5 seconds
        }

        console.log('[Orchestrator] Test complete!');

        // Optional: Take final screenshots
        if (CONFIG.screenshotOnError) {
            for (let i = 0; i < pages.length; i++) {
                await pages[i].screenshot({
                    path: `./test-screenshots/agent-${i + 1}-final.png`,
                    fullPage: true
                });
            }
            console.log('[Orchestrator] Screenshots saved');
        }

    } catch (error) {
        console.error('[Orchestrator] Test failed:', error);

        // Take error screenshots
        if (CONFIG.screenshotOnError) {
            for (let i = 0; i < pages.length; i++) {
                try {
                    await pages[i].screenshot({
                        path: `./test-screenshots/agent-${i + 1}-error.png`,
                        fullPage: true
                    });
                } catch (screenshotError) {
                    console.error(`[Agent ${i + 1}] Failed to take error screenshot:`, screenshotError);
                }
            }
        }

        throw error;
    } finally {
        // Cleanup
        console.log('[Orchestrator] Cleaning up browsers...');
        for (const browser of browsers) {
            await browser.close();
        }
        console.log('[Orchestrator] All browsers closed');
    }
}

/**
 * Run the test with proper error handling
 */
runMultiAgentTest()
    .then(() => {
        console.log('[Orchestrator] ✓ Multi-agent test completed successfully');
        process.exit(0);
    })
    .catch((error) => {
        console.error('[Orchestrator] ✗ Multi-agent test failed:', error);
        process.exit(1);
    });

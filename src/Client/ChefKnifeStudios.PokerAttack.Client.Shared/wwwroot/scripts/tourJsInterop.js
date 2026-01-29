// Driver.js interop module for Blazor
// Uses the global driver.js loaded via CDN

let currentDriver = null;

export function highlightElement(selector, title, description) {
    if (!window.driver || !window.driver.js) {
        console.error('Driver.js is not loaded. Make sure the CDN script is included.');
        return;
    }

    // Destroy any existing driver instance
    if (currentDriver) {
        currentDriver.destroy();
    }

    const driver = window.driver.js.driver;
    currentDriver = driver({
        showProgress: false,
        showButtons: ['close'],
        popoverClass: 'poker-attack-tour'
    });

    currentDriver.highlight({
        element: selector,
        popover: {
            title: title,
            description: description,
            side: 'bottom',
            align: 'center'
        }
    });
}

export function startTour(steps) {
    if (!window.driver || !window.driver.js) {
        console.error('Driver.js is not loaded. Make sure the CDN script is included.');
        return;
    }

    // Destroy any existing driver instance
    if (currentDriver) {
        currentDriver.destroy();
    }

    const driver = window.driver.js.driver;

    // Transform steps from C# format to driver.js format
    const driverSteps = steps.map(step => ({
        element: step.element,
        popover: {
            title: step.title,
            description: step.description,
            side: step.side || 'bottom',
            align: step.align || 'center'
        }
    }));

    currentDriver = driver({
        showProgress: true,
        showButtons: ['next', 'previous', 'close'],
        steps: driverSteps,
        popoverClass: 'poker-attack-tour'
    });

    currentDriver.drive();
}

export function destroyTour() {
    if (currentDriver) {
        currentDriver.destroy();
        currentDriver = null;
    }
}

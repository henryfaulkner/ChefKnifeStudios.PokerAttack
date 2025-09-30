let dotNetRef = null;

export function registerGlobalKeyHandler(dotNetObject) {
    dotNetRef = dotNetObject;
    window.addEventListener('keydown', handleKeyDown);
}

export function unregisterGlobalKeyHandler() {
    window.removeEventListener('keydown', handleKeyDown);
    dotNetRef = null;
}

function handleKeyDown(e) {
    if (!dotNetRef) return;
    // Only send single-character keys and a few specials (customize as needed)
    if (e.key.length === 1 || e.key === " " || e.key === "Space" || e.key === "Enter") {
        dotNetRef.invokeMethodAsync('OnKeyPressed', e.key);
    }
}
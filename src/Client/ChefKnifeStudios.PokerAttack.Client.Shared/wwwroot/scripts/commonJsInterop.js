// This is a JavaScript module that is loaded on demand. It can export any number of
// functions, and may import other JavaScript modules if required.

export function showPrompt(message) {
    try {
        return prompt(message, 'Type anything here');
    } catch (e) { }
}

export function setFocus(elementId) {
    try {
        let elm = document.getElementById(elementId);
        if (elm) elm.focus();
    } catch (e) { }
}

export function setFocusBySelector(cssSelector) {
    try {
        let elements = document.querySelectorAll(cssSelector);
        if (elements) elements[0].focus();
    } catch (e) { }
}

export function loseFocus(elementId) {
    try {
        let elm = document.getElementById(elementId);
        if (elm) elm.blur();
    } catch (e) { }
}

export function scrollToTop(elementId, delayMS) {
    try {
        window.setTimeout(function () {
            let elm = document.getElementById(elementId);
            if (elm) elm.scroll({ top: 0, behavior: "smooth" });
        }, delayMS);
    } catch (e) { }
}

export function scrollToEnd(elementId, delayMS) {
    try {
        window.setTimeout(function () {
            let elm = document.getElementById(elementId);
            //if (elm) elm.scrollTop = elm.scrollHeight;
            if (elm) elm.scroll({ top: elm.scrollHeight, behavior: "smooth" });
        }, delayMS);
    } catch (e) { }
}

export function scrollIntoView(selector, delayMS) {
    try {
        window.setTimeout(function () {
            let elm = document.querySelector(selector);
            if (elm) elm.scrollIntoView({ behavior: "smooth", block: "end", inline: "nearest" });
        }, delayMS);
    } catch (e) { }
}

export function getBoundingClientRect(elementId) {
    try {
        let elm = document.getElementById(elementId);

        if (elm) {
            let rect = elm.getBoundingClientRect();
            return rect;
        }
    } catch (e) { }
    return new Object();
}

export function getBoundingClientRectWithPageOffset(elementId) {
    try {
        let elm = document.getElementById(elementId);

        if (elm) {
            let rect = elm.getBoundingClientRect();
            let scrollLeft = window.pageXOffset || document.documentElement.scrollLeft;
            let scrollTop = window.pageYOffset || document.documentElement.scrollTop;

            return {
                x: rect.left + scrollLeft,
                y: rect.top + scrollTop,
                width: rect.width,
                height: rect.height
            };
        }
    } catch (e) {
        console.error(e);
    }
    return {};
}


export function clickElement(selector) {
    try {
        let element = document.querySelector(selector);
        if (element) element.click();
    } catch (e) { }
}

export function registerBodyClickEvent(dotNetObjectRef) {
    try {
        document.body.addEventListener('click', (event) => {
            dotNetObjectRef.invokeMethodAsync('OnBodyClick', event);
        });
    } catch (e) { }
}

export function addTemporaryClassToElementById(className, elementId, milisecondsToHaveClass) {
    try {
        let elm = document.getElementById(elementId);

        elm.classList.add(className);
        setTimeout(() => elm.classList.remove(className), milisecondsToHaveClass);
    } catch (e) { }
}

export function addClassToAllElementsWithClass(targetClass, newClass) {
    try {
        var elements = document.querySelectorAll("." + targetClass);
        elements.forEach(element => element.classList.add(newClass));
    } catch (e) { }
}

export function removeClassFromAllElementsWithClass(targetClass, removedClass) {
    try {
        var elements = document.querySelectorAll("." + targetClass);
        elements.forEach(element => element.classList.remove(removedClass));
    } catch (e) { }
}

export function registerKeyDownEvent(dotNetObjectRef) {
    try {
        document.body.addEventListener('keydown', (event) => {
            // had to create an object for KeyboardEventArgs parameter manually, as JS event was not getting converted by default
            dotNetObjectRef.invokeMethodAsync('OnKeyDown', {
                Key: event.key,
                Code: event.code,
                Location: event.location,
                Repeat: event.repeat,
                CtrlKey: event.ctrlKey,
                ShiftKey: event.shiftKey,
                AltKey: event.altKey,
                MetaKey: event.metaKey,
                Type: event.type,
            });
        });
    } catch (e) { }
}

export function returnViewportWidth() {
    try {
        return window.innerWidth;
    } catch (e) { }
}

export function getViewportSize() {
    return { x: window.innerWidth, y: window.innerHeight };
}

export function openLinkInNewTab(url) {
    try {
        var newTab = window.open(url, '_blank');

        return (!newTab || newTab.closed || typeof newTab.closed == 'undefined');

    } catch (e) { }
}

export function copyToClipboard(elementId) {
    try {
        const richTextDiv = document.getElementById(elementId);

        const clipboardItem = new ClipboardItem({
            "text/plain": new Blob(
                [richTextDiv.innerText],
                { type: "text/plain" }
            ),
            "text/html": new Blob(
                [richTextDiv.outerHTML],
                { type: "text/html" }
            ),
        });

        navigator.clipboard.write([clipboardItem]);
    } catch (e) { }
}

export function selectInput(elementId) {
    const el = document.getElementById(elementId);
    if (el == null) return;
    el.select();
}

export function addOutsideClickListener(elementId, dotNetHelper) {
    const listener = (event) => {
        const element = document.getElementById(elementId);
        if (element && !element.contains(event.target)) {
            dotNetHelper.invokeMethodAsync('HandleOutsideClick', elementId);
        }
    };

    // Attach the listener to the document
    document.addEventListener('click', listener);

    // Return the listener so it can be removed later
    return listener;
}

export function removeOutsideClickListener(listener) {
    // Remove the specific listener passed in
    document.removeEventListener('click', listener);
}

export function removeAllViaQuerySelector(selector) {
    try {
        let elements = document.querySelectorAll(selector);
        if (elements.length < 1) return;
        elements.forEach(element => element.remove());
    } catch (e) { }
}

// Known theme classes - add new themes here as they're created
const THEMES = ['light', 'dark'];

export function setTheme(themeName) {
    try {
        // Remove all known theme classes
        document.body.classList.remove(...THEMES);
        // Add the new theme class (if not 'light', which is the default with no class)
        if (themeName && themeName !== 'light') {
            document.body.classList.add(themeName);
        }
    } catch (e) { }
}
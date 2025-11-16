/**
 * Creates and plays a new HTML Audio element for a single, transient sound.
 * This is used for sound effects (e.g., clicks, alerts).
 * @param {string} src - The URL of the audio file (relative to wwwroot).
 */
export function playOneShot(src) {
    const audio = new Audio(src);
    audio.play()
        .catch(e => console.error("Error playing one-shot audio:", e));

    // Optional: Clean up the element after it finishes playing
    audio.addEventListener('ended', () => audio.remove());
}
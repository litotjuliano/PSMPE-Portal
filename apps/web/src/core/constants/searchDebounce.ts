/**
 * How long a list page's search box waits after the last keystroke before it filters. Long enough
 * that someone typing at a normal pace finishes the word first, instead of the table re-filtering
 * mid-word. Shared so every list page behaves the same.
 */
export const SEARCH_DEBOUNCE_MS = 800

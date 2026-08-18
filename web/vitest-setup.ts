// Registers @testing-library/jest-dom's matchers (toHaveTextContent etc.)
// with vitest's expect, including their types.
import '@testing-library/jest-dom/vitest';

// jsdom does not implement window.scrollTo; the Solid router calls it during
// navigation, which would otherwise throw and abort the navigation in tests.
Object.defineProperty(window, 'scrollTo', {
  value: () => undefined,
  writable: true,
});

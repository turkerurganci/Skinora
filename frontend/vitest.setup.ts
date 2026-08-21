// Extends Vitest's `expect` with @testing-library/jest-dom matchers
// (toBeInTheDocument, toHaveTextContent, …) for component render tests.
import "@testing-library/jest-dom/vitest";

// T135 — jsdom parses <dialog> but does not implement its modal methods, so any
// component that opens one (CancelModal, DisputeModal, the "Teslim Aldım"
// confirmation) throws on mount before a single assertion runs. The shim keeps
// the only behaviour the tests observe: `open` reflects the state and closing
// fires the `close` event listeners rely on. Real browsers are unaffected —
// this file is loaded by Vitest only.
if (typeof HTMLDialogElement !== "undefined" && !HTMLDialogElement.prototype.showModal) {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.open = true;
  };
  HTMLDialogElement.prototype.show = function show(this: HTMLDialogElement) {
    this.open = true;
  };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    this.open = false;
    this.dispatchEvent(new Event("close"));
  };
}

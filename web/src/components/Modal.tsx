import { createEffect, onCleanup, Show, type ParentProps } from 'solid-js';

interface ModalProps extends ParentProps {
  open: boolean;
  title: string;
  /** Width class for the panel; defaults to max-w-lg. */
  size?: string;
  onClose: () => void;
}

/**
 * Centered modal overlay. Renders nothing until `open`; Escape or clicking the
 * backdrop closes it. Content is unmounted on close, so forms inside start fresh.
 */
export default function Modal(props: ModalProps) {
  createEffect(
    () => props.open,
    (open) => {
      if (!open) return;
      const onKey = (event: KeyboardEvent) => {
        if (event.key === 'Escape') props.onClose();
      };
      window.addEventListener('keydown', onKey);
      onCleanup(() => window.removeEventListener('keydown', onKey));
    },
  );

  return (
    <Show when={props.open}>
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4">
        <div
          class="absolute inset-0 bg-slate-900/50"
          onClick={() => props.onClose()}
          aria-hidden="true"
        />
        <div
          role="dialog"
          aria-modal="true"
          aria-label={props.title}
          class={`relative w-full ${props.size ?? 'max-w-lg'} max-h-[85vh] overflow-y-auto rounded-lg bg-white shadow-xl`}
        >
          <div class="sticky top-0 z-10 flex items-center justify-between border-b border-slate-200 bg-white px-5 py-3">
            <h2 class="text-base font-semibold text-slate-800">{props.title}</h2>
            <button
              class="rounded-md p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-800"
              onClick={() => props.onClose()}
              aria-label="Close"
            >
              <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
                <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
              </svg>
            </button>
          </div>
          <div class="p-5">{props.children}</div>
        </div>
      </div>
    </Show>
  );
}

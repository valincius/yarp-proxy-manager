import { createContext, createSignal, For, useContext, type ParentProps } from 'solid-js';

export type ToastType = 'error' | 'success' | 'info';

export interface Toast {
  id: number;
  type: ToastType;
  message: string;
}

interface ToastValue {
  push: (message: string, type?: ToastType) => void;
}

const ToastContext = createContext<ToastValue>();

let nextId = 1;

export function ToastProvider(props: ParentProps) {
  const [toasts, setToasts] = createSignal<Toast[]>([]);

  function dismiss(id: number) {
    setToasts((current) => current.filter((t) => t.id !== id));
  }

  function push(message: string, type: ToastType = 'info') {
    const id = nextId++;
    setToasts((current) => [...current, { id, type, message }]);
    // Auto-dismiss after 5s; errors stick a little longer.
    window.setTimeout(() => dismiss(id), type === 'error' ? 8000 : 5000);
  }

  const value: ToastValue = { push };

  const tones: Record<ToastType, string> = {
    error: 'border-red-300 bg-red-50 text-red-800',
    success: 'border-green-300 bg-green-50 text-green-800',
    info: 'border-slate-300 bg-white text-slate-800',
  };

  const icons: Record<ToastType, string> = {
    error: '✕',
    success: '✓',
    info: 'ℹ',
  };

  return (
    <ToastContext value={value}>
      {props.children}
      <div class="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-80 flex-col gap-2">
        <For each={toasts()}>
          {(toast) => (
            <div
              role="status"
              class={`pointer-events-auto flex items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-lg ${tones[toast.type]}`}
            >
              <span class="mt-0.5 shrink-0 font-bold">{icons[toast.type]}</span>
              <span class="flex-1 break-words">{toast.message}</span>
              <button
                class="shrink-0 text-xs text-slate-400 hover:text-slate-700"
                onClick={() => dismiss(toast.id)}
                aria-label="Dismiss"
              >
                ✕
              </button>
            </div>
          )}
        </For>
      </div>
    </ToastContext>
  );
}

export function useToast(): ToastValue {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return context;
}

/** Shorthand helpers for common cases. */
export function toastError(instance: ToastValue, message: string): void {
  instance.push(message, 'error');
}

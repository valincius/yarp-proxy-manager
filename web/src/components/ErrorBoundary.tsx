import { Errored } from 'solid-js';
import { createEffect, type ParentProps } from 'solid-js';
import { useToast } from '../lib/toast';

interface ErrorBoundaryProps extends ParentProps {
  /** Label for the section that failed, used in the fallback message and toast. */
  label?: string;
}

/**
 * Catches errors thrown by the subtree and shows a small inline fallback.
 * The failure is also surfaced as an error toast so it is visible even when
 * the fallback renders inside a scrollable page.
 */
export default function ErrorBoundary(props: ErrorBoundaryProps) {
  const toast = useToast();
  const label = () => props.label ?? 'This section';

  return (
    <Errored
      fallback={(err, reset) => {
        createEffect(
          () => err(),
          (error) => {
            const message = error instanceof Error ? error.message : String(error);
            toast.push(`${label()} failed to load: ${message}`, 'error');
          },
        );
        return (
          <div class="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-700">
            <p class="font-medium">{label()} hit an unexpected error.</p>
            <p class="mt-1 text-xs opacity-80">{(err() as Error)?.message ?? String(err())}</p>
            <button
              class="mt-2 rounded-md border border-red-300 bg-white px-3 py-1 text-xs font-medium text-red-700 hover:bg-red-100"
              onClick={reset}
            >
              Retry
            </button>
          </div>
        );
      }}
    >
      {props.children}
    </Errored>
  );
}

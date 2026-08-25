/**
 * Icon-only row actions. Each button is a fixed-size square with its own hover
 * background and a tooltip, so destructive/toggle actions stay visually
 * distinct from editing instead of crowding it as small text links.
 */

const baseClass =
  'inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-md transition-colors disabled:opacity-50';

export function PowerButton(props: { enabled: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      class={`${baseClass} ${
        props.enabled ? 'text-green-600 hover:bg-green-50' : 'text-slate-400 hover:bg-slate-100 hover:text-slate-600'
      }`}
      title={props.enabled ? 'Disable' : 'Enable'}
      aria-label={props.enabled ? 'Disable' : 'Enable'}
      onClick={props.onClick}
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path
          fill-rule="evenodd"
          d="M10 1a.75.75 0 0 1 .75.75v6.5a.75.75 0 0 1-1.5 0v-6.5A.75.75 0 0 1 10 1ZM5.06 3.44a.75.75 0 0 1 0 1.06A6.5 6.5 0 1 0 14.94 4.5a.75.75 0 1 1 1.06-1.06 8 8 0 1 1-10.94 0 .75.75 0 0 1 0-1.06Z"
          clip-rule="evenodd"
        />
      </svg>
    </button>
  );
}

export function EditButton(props: { href: string }) {
  return (
    <a
      href={props.href}
      class={`${baseClass} text-blue-600 hover:bg-blue-50`}
      title="Edit"
      aria-label="Edit"
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25H10A.75.75 0 0 0 10 3H4.75A2.75 2.75 0 0 0 2 5.75v9.5A2.75 2.75 0 0 0 4.75 18h9.5A2.75 2.75 0 0 0 17 15.25V10a.75.75 0 0 0-1.5 0v5.25c0 .69-.56 1.25-1.25 1.25h-9.5c-.69 0-1.25-.56-1.25-1.25v-9.5Z" />
      </svg>
    </a>
  );
}

export function DeleteButton(props: { onClick: () => void; disabled?: boolean; label?: string }) {
  return (
    <button
      type="button"
      class={`${baseClass} text-red-600 hover:bg-red-50`}
      disabled={props.disabled}
      title={props.label ?? 'Delete'}
      aria-label={props.label ?? 'Delete'}
      onClick={props.onClick}
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path
          fill-rule="evenodd"
          d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482 41.03 41.03 0 0 0-2.365-.298V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z"
          clip-rule="evenodd"
        />
      </svg>
    </button>
  );
}

export function KeyButton(props: { onClick: () => void; label?: string }) {
  return (
    <button
      type="button"
      class={`${baseClass} text-amber-600 hover:bg-amber-50`}
      title={props.label ?? 'Reset password'}
      aria-label={props.label ?? 'Reset password'}
      onClick={props.onClick}
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
        <path
          fill-rule="evenodd"
          d="M14 6a4 4 0 0 1-4.899 3.899l-1.955 1.955a.5.5 0 0 1-.353.146H5.5a.5.5 0 0 0-.5.5v1.5a.5.5 0 0 1-.5.5h-1a.5.5 0 0 1-.5-.5v-1.293a1 1 0 0 1 .293-.707l3.957-3.957A4 4 0 1 1 14 6Zm-4-2a1 1 0 1 0 0 2 1 1 0 0 0 0-2Z"
          clip-rule="evenodd"
        />
      </svg>
    </button>
  );
}

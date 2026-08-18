export function StatusBadge(props: { enabled: boolean }) {
  return props.enabled ? (
    <span class="inline-flex rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">Online</span>
  ) : (
    <span class="inline-flex rounded-full bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-600">Disabled</span>
  );
}

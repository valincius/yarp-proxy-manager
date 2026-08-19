import { Title } from '@solidjs/meta';
import { createEffect, createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import { useToast } from '../../lib/toast';
import type { AcmeSettings } from '../../lib/types';

const loadAcmeSettings = query(async (): Promise<AcmeSettings> => api.get('/acme-settings'), 'acme-settings');

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

function ErrorBanner(props: { messages: string[] | null }) {
  return (
    <Show when={props.messages && props.messages.length > 0}>
      <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
        <ul class="list-disc space-y-1 pl-5">
          <For each={props.messages!}>{(message) => <li>{message}</li>}</For>
        </ul>
      </div>
    </Show>
  );
}

export default function Settings() {
  return (
    <section class="space-y-8">
      <Title>Settings - YARP Proxy Manager</Title>
      <h1 class="text-2xl font-semibold text-slate-800">Settings</h1>
      <AcmeSection />
    </section>
  );
}

function AcmeSection() {
  const settings = createMemo(() => loadAcmeSettings());
  const toast = useToast();
  const [email, setEmail] = createSignal('');
  const [staging, setStaging] = createSignal(false);
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  createEffect(settings, (s) => {
    setEmail(s.email);
    setStaging(s.staging);
  });

  async function save(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.put('/acme-settings', { email: email(), directoryUrl: '', staging: staging() });
      revalidate('acme-settings');
      toast.push('ACME settings saved.', 'success');
    } catch (e) {
      setError(toMessages(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section class="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h2 class="mb-1 text-lg font-medium text-slate-800">Certificates — ACME account</h2>
      <p class="mb-4 text-sm text-slate-500">
        Let's Encrypt account used when requesting certificates. Applies to all new certificate requests.
      </p>
      <ErrorBanner messages={error()} />
      <form class="grid grid-cols-1 gap-4 sm:grid-cols-2" onSubmit={save}>
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Account email</span>
          <input type="email" required class={inputClass} value={email()} onInput={(e) => setEmail(e.currentTarget.value)} />
          <span class="mt-1 block text-xs text-slate-500">Renewal notices and rate-limit recovery are sent to this address.</span>
        </label>
        <label class="flex items-end gap-2 pb-2">
          <input
            type="checkbox"
            class="h-4 w-4 rounded border-slate-300 text-blue-600"
            checked={staging()}
            onChange={(e) => setStaging(e.currentTarget.checked)}
          />
          <span class="text-sm text-slate-700">Use Let's Encrypt staging CA (no rate limits)</span>
        </label>
        <div>
          <button
            type="submit"
            disabled={busy()}
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
          >
            {busy() ? 'Saving…' : 'Save ACME settings'}
          </button>
        </div>
      </form>
    </section>
  );
}

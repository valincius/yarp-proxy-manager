import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import type { AccessList, AccessListInput, AccessListRule } from '../../lib/types';

const loadAccessLists = query(async (): Promise<AccessList[]> => api.get('/access-lists'), 'access-lists');

const inputClass =
  'mt-1 block w-full rounded-md border border-slate-300 px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-500';

function toMessages(error: unknown): string[] {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors : [error.message];
  }
  return [error instanceof Error ? error.message : 'An unexpected error occurred.'];
}

export default function AccessLists() {
  const lists = createMemo(() => loadAccessLists());
  const [editing, setEditing] = createSignal<AccessList | null>(null);
  const [showForm, setShowForm] = createSignal(false);

  async function remove(list: AccessList) {
    if (!confirm(`Delete access list "${list.name}"?`)) {
      return;
    }
    await api.del(`/access-lists/${list.id}`);
    revalidate('access-lists');
  }

  return (
    <section>
      <Title>Access Lists - YARP Proxy Manager</Title>
      <div class="mb-6 flex items-center justify-between">
        <h1 class="text-2xl font-semibold text-slate-800">Access Lists</h1>
        <button
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
          onClick={() => {
            setEditing(null);
            setShowForm(true);
          }}
        >
          + New Access List
        </button>
      </div>

      <Show when={showForm()}>
        <div class="mb-6">
          <AccessListForm
            initial={editing() ?? undefined}
            onDone={() => {
              setShowForm(false);
              revalidate('access-lists');
            }}
          />
        </div>
      </Show>

      <Show when={lists().length > 0} fallback={<p class="text-sm text-slate-500">No access lists yet.</p>}>
        <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <For each={lists()}>
            {(list) => (
              <div class="rounded-lg border border-slate-200 bg-white p-5 shadow-sm">
                <div class="mb-3 flex items-center justify-between">
                  <h2 class="font-medium text-slate-800">{list.name}</h2>
                  <div class="flex gap-2">
                    <button
                      class="text-xs font-medium text-blue-600 hover:text-blue-700"
                      onClick={() => {
                        setEditing(list);
                        setShowForm(true);
                      }}
                    >
                      Edit
                    </button>
                    <button class="text-xs font-medium text-red-600 hover:text-red-700" onClick={() => void remove(list)}>
                      Delete
                    </button>
                  </div>
                </div>
                <p class="mb-2 text-xs text-slate-500">
                  {list.satisfyAny ? 'Satisfy Any' : 'Satisfy All'} · {list.rules.length} rules
                </p>
                <ul class="divide-y divide-slate-100 text-sm">
                  <For each={list.rules}>
                    {(rule) => (
                      <li class="flex items-center justify-between py-1.5">
                        <span class="text-slate-700">{rule.pattern}</span>
                        <span
                          class={`rounded-full px-2 py-0.5 text-xs font-medium ${
                            rule.action === 'Allow' ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
                          }`}
                        >
                          {rule.action}
                        </span>
                      </li>
                    )}
                  </For>
                </ul>
              </div>
            )}
          </For>
        </div>
      </Show>
    </section>
  );
}

interface RuleRow {
  action: 'Allow' | 'Deny';
  pattern: string;
}

function AccessListForm(props: { initial?: AccessList; onDone: () => void }) {
  const [name, setName] = createSignal(props.initial?.name ?? '');
  const [satisfyAny, setSatisfyAny] = createSignal(props.initial?.satisfyAny ?? true);
  const [rules, setRules] = createSignal<RuleRow[]>(
    props.initial?.rules.map((r) => ({ action: r.action, pattern: r.pattern })) ?? [{ action: 'Allow', pattern: '*' }],
  );
  const [busy, setBusy] = createSignal(false);
  const [error, setError] = createSignal<string[] | null>(null);

  function updateRule(index: number, patch: Partial<RuleRow>) {
    setRules((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  async function submit(event: SubmitEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const input: AccessListInput = {
      name: name(),
      satisfyAny: satisfyAny(),
      rules: rules().filter((r) => r.pattern.trim().length > 0),
    };
    try {
      if (props.initial) {
        await api.put(`/access-lists/${props.initial.id}`, input);
      } else {
        await api.post('/access-lists', input);
      }
      props.onDone();
    } catch (e) {
      setError(toMessages(e));
      setBusy(false);
    }
  }

  return (
    <form class="max-w-2xl space-y-4 rounded-lg border border-slate-200 bg-white p-6 shadow-sm" onSubmit={submit}>
      <Show when={error()}>
        <div class="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-700">
          <ul class="list-disc space-y-1 pl-5">
            <For each={error()!}>{(message) => <li>{message}</li>}</For>
          </ul>
        </div>
      </Show>
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm font-medium text-slate-700">Name</span>
          <input class={inputClass} value={name()} onInput={(e) => setName(e.currentTarget.value)} required />
        </label>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-700">
          <input type="checkbox" class="h-4 w-4 rounded border-slate-300" checked={satisfyAny()} onChange={(e) => setSatisfyAny(e.currentTarget.checked)} />
          Satisfy Any (allow if any rule matches)
        </label>
      </div>

      <div>
        <div class="mb-2 flex items-center justify-between">
          <span class="text-sm font-medium text-slate-700">Rules</span>
          <button
            type="button"
            class="text-xs font-medium text-blue-600 hover:text-blue-700"
            onClick={() => setRules((rows) => [...rows, { action: 'Allow', pattern: '' }])}
          >
            + Add rule
          </button>
        </div>
        <div class="space-y-2">
          <For each={rules()}>
            {(rule, index) => (
              <div class="flex items-center gap-2">
                <select
                  class="w-28 rounded-md border border-slate-300 px-2 py-2 text-sm"
                  value={rule.action}
                  onChange={(e) => updateRule(index(), { action: e.currentTarget.value as 'Allow' | 'Deny' })}
                >
                  <option value="Allow">Allow</option>
                  <option value="Deny">Deny</option>
                </select>
                <input
                  class="flex-1 rounded-md border border-slate-300 px-3 py-2 text-sm"
                  placeholder="IP, CIDR, or *"
                  value={rule.pattern}
                  onInput={(e) => updateRule(index(), { pattern: e.currentTarget.value })}
                />
                <button
                  type="button"
                  class="text-xs font-medium text-red-600 hover:text-red-700"
                  onClick={() => setRules((rows) => rows.filter((_, i) => i !== index()))}
                >
                  Remove
                </button>
              </div>
            )}
          </For>
        </div>
      </div>

      <button
        type="submit"
        disabled={busy()}
        class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
      >
        {busy() ? 'Saving…' : props.initial ? 'Save changes' : 'Create access list'}
      </button>
    </form>
  );
}

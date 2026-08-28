import { Title } from '@solidjs/meta';
import { createSignal, For, Show } from 'solid-js';
import { createMemo } from 'solid-js';
import { query, revalidate } from '@solidjs/router';
import { api, ApiError } from '../../lib/api';
import Modal from '../../components/Modal';
import type { AccessList, AccessListInput } from '../../lib/types';

const loadAccessLists = query(async (): Promise<AccessList[]> => api.get('/access-lists'), 'access-lists');

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

export default function AccessLists() {
  const lists = createMemo(() => loadAccessLists());
  const [editing, setEditing] = createSignal<AccessList | null>(null);
  const [showForm, setShowForm] = createSignal(false);
  // Rows whose rules are expanded; collapsed by default.
  const [expanded, setExpanded] = createSignal<Set<string>>(new Set());

  function toggleExpanded(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

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
      <div class="page-header mb-6 flex items-center justify-between">
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

      <Modal
        open={showForm()}
        title={editing() ? `Edit access list "${editing()!.name}"` : 'New access list'}
        onClose={() => setShowForm(false)}
      >
        <AccessListForm
          initial={editing() ?? undefined}
          onDone={() => {
            setShowForm(false);
            revalidate('access-lists');
          }}
        />
      </Modal>

      <Show when={lists().length > 0} fallback={<p class="text-sm text-slate-500">No access lists yet.</p>}>
        <table class="responsive-card-table w-full rounded-lg border border-slate-200 bg-white text-sm shadow-sm">
          <thead>
            <tr class="border-b border-slate-200 text-left text-xs uppercase tracking-wide text-slate-500">
              <th class="w-10 px-4 py-3"></th>
              <th class="px-4 py-3">Name</th>
              <th class="px-4 py-3">Policy</th>
              <th class="px-4 py-3">Rules</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <For each={lists()}>
              {(list) => {
                const isExpanded = () => expanded().has(list.id);
                return (
                  <>
                    <tr class="border-b border-slate-100 last:border-0 hover:bg-slate-50">
                      <td data-label="Status" class="px-4 py-3">
                        <button
                          class="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-800"
                          onClick={() => toggleExpanded(list.id)}
                          aria-label={isExpanded() ? 'Collapse rules' : 'Expand rules'}
                        >
                          <svg
                            class={`h-4 w-4 transition-transform ${isExpanded() ? 'rotate-90' : ''}`}
                            viewBox="0 0 20 20"
                            fill="currentColor"
                          >
                            <path
                              fill-rule="evenodd"
                              d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z"
                              clip-rule="evenodd"
                            />
                          </svg>
                        </button>
                      </td>
                      <td data-label="Name" class="px-4 py-3 font-medium text-slate-800">{list.name}</td>
                      <td data-label="Rules" class="px-4 py-3">
                        <span
                          class={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                            list.satisfyAny ? 'bg-blue-100 text-blue-700' : 'bg-slate-200 text-slate-600'
                          }`}
                        >
                          {list.satisfyAny ? 'Satisfy Any' : 'Satisfy All'}
                        </span>
                      </td>
                      <td data-label="Count" class="px-4 py-3 text-slate-600">{list.rules.length} rule{list.rules.length === 1 ? '' : 's'}</td>
                      <td data-label="Actions" class="px-4 py-3">
                        <div class="flex items-center justify-end gap-2">
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
                      </td>
                    </tr>
                    <Show when={isExpanded()}>
                      <tr class="border-b border-slate-100 bg-slate-50/60 last:border-0">
                        <td data-label="" class="px-4 py-3"></td>
                        <td colspan={5} class="px-4 py-3">
                          <Show
                            when={list.rules.length > 0}
                            fallback={<p class="text-xs text-slate-400">No rules — this list matches nothing.</p>}
                          >
                            <ul class="max-w-lg divide-y divide-slate-100 rounded-md border border-slate-200 bg-white text-sm">
                              <For each={list.rules}>
                                {(rule) => (
                                  <li class="flex items-center justify-between px-3 py-2">
                                    <code class="text-slate-700">{rule.pattern}</code>
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
                            <p class="mt-2 text-xs text-slate-500">
                              {list.satisfyAny
                                ? 'A request is allowed if any rule matches.'
                                : 'A request is allowed only if every rule matches.'}{' '}
                              Rules accept an IP address, a CIDR block, or <code>*</code> for everything.
                            </p>
                          </Show>
                        </td>
                      </tr>
                    </Show>
                  </>
                );
              }}
            </For>
          </tbody>
        </table>
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
    <form class="space-y-4" onSubmit={submit}>
      <ErrorBanner messages={error()} />
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
              <div class="form-row flex items-center gap-2">
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
        <p class="mt-2 text-xs text-slate-500">
          Each rule is an IP address (e.g. <code>192.168.1.10</code>), a CIDR block (e.g. <code>10.0.0.0/8</code>), or{' '}
          <code>*</code> to match every address.
        </p>
      </div>

      <div class="flex items-center gap-3 pt-2">
        <button
          type="submit"
          disabled={busy()}
          class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
        >
          {busy() ? 'Saving…' : props.initial ? 'Save changes' : 'Create access list'}
        </button>
        <button type="button" class="text-sm font-medium text-slate-600 hover:text-slate-900" onClick={props.onDone}>
          Cancel
        </button>
      </div>
    </form>
  );
}

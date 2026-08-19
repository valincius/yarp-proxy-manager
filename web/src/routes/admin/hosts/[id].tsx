import { Title } from '@solidjs/meta';
import { query, useNavigate, type RouteDefinition, type RouteProps } from '@solidjs/router';
import { createMemo, Loading, Show } from 'solid-js';
import HostForm from '../../../components/HostForm';
import { api } from '../../../lib/api';
import type { ProxyHost, ProxyHostInput } from '../../../lib/types';

const getHost = query(async (id: string): Promise<ProxyHost> => api.get(`/hosts/${id}`), 'host');

export const route = {
  preload: ({ params }) => void getHost(params.id!),
} satisfies RouteDefinition;

export default function EditHost(props: RouteProps<'/admin/hosts/:id'>) {
  const navigate = useNavigate();
  // The query() result is an async value: while pending it can only be read
  // inside a tracking scope, so gate the form with a <Loading> boundary. The
  // form itself must never receive a pending value (its signal initializers
  // run untracked), which is why it only mounts once the boundary resolves.
  const host = createMemo(() => getHost(props.params.id));

  async function update(input: ProxyHostInput) {
    await api.put(`/hosts/${props.params.id}`, input);
    navigate('/admin/hosts');
  }

  return (
    <section>
      <Title>Edit Proxy Host - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Edit Proxy Host</h1>
      <Loading fallback={<p class="text-sm text-slate-500">Loading host…</p>}>
        <HostForm initial={host()} submitLabel="Save changes" onSubmit={update} />
      </Loading>
    </section>
  );
}

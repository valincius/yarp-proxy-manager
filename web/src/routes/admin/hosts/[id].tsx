import { Title } from '@solidjs/meta';
import { query, useNavigate, type RouteDefinition, type RouteProps } from '@solidjs/router';
import { createMemo } from 'solid-js';
import HostForm from '../../../components/HostForm';
import { api } from '../../../lib/api';
import type { ProxyHost, ProxyHostInput } from '../../../lib/types';

const getHost = query(async (id: string): Promise<ProxyHost> => api.get(`/hosts/${id}`), 'host');

export const route = {
  preload: ({ params }) => void getHost(params.id!),
} satisfies RouteDefinition;

export default function EditHost(props: RouteProps<'/admin/hosts/:id'>) {
  const navigate = useNavigate();
  const host = createMemo(() => getHost(props.params.id));

  async function update(input: ProxyHostInput) {
    await api.put(`/hosts/${props.params.id}`, input);
    navigate('/admin/hosts');
  }

  return (
    <section>
      <Title>Edit Proxy Host - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">Edit Proxy Host</h1>
      <HostForm initial={host()} submitLabel="Save changes" onSubmit={update} />
    </section>
  );
}

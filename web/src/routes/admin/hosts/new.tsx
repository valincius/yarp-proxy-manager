import { Title } from '@solidjs/meta';
import { revalidate, useNavigate } from '@solidjs/router';
import HostForm from '../../../components/HostForm';
import { api } from '../../../lib/api';
import type { ProxyHostInput } from '../../../lib/types';

export default function NewHost() {
  const navigate = useNavigate();

  async function create(input: ProxyHostInput) {
    await api.post('/hosts', input);
    // The list and dashboard read through the router's query cache, which is
    // only invalidated by an explicit revalidate — otherwise the new host is
    // invisible until a full page refresh.
    revalidate('hosts-list');
    revalidate('dashboard');
    navigate('/admin/hosts');
  }

  return (
    <section>
      <Title>New Proxy Host - YARP Proxy Manager</Title>
      <h1 class="mb-6 text-2xl font-semibold text-slate-800">New Proxy Host</h1>
      <HostForm submitLabel="Create host" onSubmit={create} />
    </section>
  );
}

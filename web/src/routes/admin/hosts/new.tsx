import { Title } from '@solidjs/meta';
import { revalidate, useNavigate } from '@solidjs/router';
import HostForm, { type HostSubmitOptions } from '../../../components/HostForm';
import { api, ApiError } from '../../../lib/api';
import { useToast } from '../../../lib/toast';
import type { CertificateDto, ProxyHost, ProxyHostInput } from '../../../lib/types';

function certFailureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.errors && error.errors.length > 0 ? error.errors.join('; ') : error.message;
  }
  return error instanceof Error ? error.message : 'An unexpected error occurred.';
}

export default function NewHost() {
  const navigate = useNavigate();
  const toast = useToast();

  async function create(input: ProxyHostInput, options: HostSubmitOptions) {
    const host = await api.post<ProxyHost>('/hosts', input);

    if (options.autoCert) {
      try {
        const wildcard = input.domainNames.some((d) => d.startsWith('*.'));
        const certificate = await api.post<CertificateDto>('/certificates/issue', {
          name: input.domainNames[0],
          domains: input.domainNames,
          // Idempotent server-side: an existing cert for the same domains is reused.
          challengeType: wildcard ? 'Dns01' : 'Http01',
          dnsCredentialId: wildcard ? options.dnsCredentialId : null,
        });
        if (host.certificateId !== certificate.id) {
          await api.put(`/hosts/${host.id}`, { ...input, certificateId: certificate.id });
        }
        toast.push(`Certificate "${certificate.name}" requested.`, 'success');
      } catch (e) {
        // The host exists; surface the cert failure and land on its edit page so
        // the user can retry from there.
        toast.push(`Host created, but certificate failed: ${certFailureMessage(e)}`, 'error');
        revalidate('hosts-list');
        revalidate('dashboard');
        navigate(`/admin/hosts/${host.id}`);
        return;
      }
    }

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
      <HostForm submitLabel="Create host" isCreate onSubmit={create} />
    </section>
  );
}

import { useNavigate } from '@solidjs/router';
import { createEffect } from 'solid-js';

export default function Home() {
  const navigate = useNavigate();
  createEffect(() => navigate('/admin', { replace: true }));
  return null;
}

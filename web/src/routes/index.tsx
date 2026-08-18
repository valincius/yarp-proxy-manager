import { useNavigate } from '@solidjs/router';

export default function Home() {
  const navigate = useNavigate();
  // One-shot side effect: redirect on mount. Reads no signals, so under
  // Solid 2's compute/effect split it must be called directly rather than
  // wrapped in createEffect.
  navigate('/admin', { replace: true });
  return null;
}

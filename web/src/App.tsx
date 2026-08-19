import { Title } from '@solidjs/meta';
import { Loading } from 'solid-js';
import { Router } from './router';
import { AuthProvider } from './lib/auth';
import { ToastProvider } from './lib/toast';
import ErrorBoundary from './components/ErrorBoundary';
import './App.css';

// The app root: auth provider + router. Pages are the modules under src/routes.
export default function App() {
  return (
    <ToastProvider>
      <AuthProvider>
        <Router>
          {(props) => (
            <>
              <Title>YARP Proxy Manager</Title>
              <Loading fallback={<main>Loading…</main>}>
                <ErrorBoundary label="Page">{props.children}</ErrorBoundary>
              </Loading>
            </>
          )}
        </Router>
      </AuthProvider>
    </ToastProvider>
  );
}

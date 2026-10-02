import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from '../contexts/AuthContext';
import { ToastProvider } from '../contexts/ToastContext';
import { ToastContainer } from '../components/ToastContainer';

/**
 * Signs the test in by seeding the refresh-token key `AuthContext` reads on
 * mount; pair with an `/auth/refresh` MSW handler (see `handlers.ts`) that
 * returns a token carrying the role under test.
 */
export function signInAs() {
  localStorage.setItem('crm_refresh_token', 'test-refresh-token');
}

export function renderWithProviders(ui: ReactElement, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AuthProvider>
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
          <ToastContainer />
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}

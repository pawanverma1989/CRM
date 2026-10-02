import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './contexts/AuthContext';
import { ToastProvider } from './contexts/ToastContext';
import { ToastContainer } from './components/ToastContainer';
import { AuthGuard } from './components/AuthGuard';
import { Layout } from './components/Layout';
import { LeadsPage } from './pages/LeadsPage';
import { LeadFormPage } from './pages/LeadFormPage';
import { LeadDetailPage } from './pages/LeadDetailPage';
import { LeadSourcesPage } from './pages/LeadSourcesPage';
import { DisqualifyReasonsPage } from './pages/DisqualifyReasonsPage';
import { WebFormsPage } from './pages/WebFormsPage';
import { CustomFieldsPage } from './pages/CustomFieldsPage';
import { RecycleBinPage } from './pages/RecycleBinPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
});

function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/"
        element={
          <AuthGuard>
            <Layout>
              <LeadsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/new"
        element={
          <AuthGuard>
            <Layout>
              <LeadFormPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/:id"
        element={
          <AuthGuard>
            <Layout>
              <LeadDetailPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/:id/edit"
        element={
          <AuthGuard>
            <Layout>
              <LeadFormPage />
            </Layout>
          </AuthGuard>
        }
      />

      <Route
        path="/lead-sources"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <LeadSourcesPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/disqualify-reasons"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <DisqualifyReasonsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/web-forms"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <WebFormsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/custom-fields"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <CustomFieldsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/recycle-bin"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <RecycleBinPage />
            </Layout>
          </AuthGuard>
        }
      />

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AuthProvider>
          <BrowserRouter basename="/leads">
            <AppRoutes />
            <ToastContainer />
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}

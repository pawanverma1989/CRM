import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './contexts/AuthContext';
import { ToastProvider } from './contexts/ToastContext';
import { ToastContainer } from './components/ToastContainer';
import { AuthGuard } from './components/AuthGuard';
import { Layout } from './components/Layout';
import { ContactsPage } from './pages/ContactsPage';
import { ContactDetailPage } from './pages/ContactDetailPage';
import { ContactFormPage } from './pages/ContactFormPage';
import { CompaniesPage } from './pages/CompaniesPage';
import { CompanyDetailPage } from './pages/CompanyDetailPage';
import { CompanyFormPage } from './pages/CompanyFormPage';
import { MergePage } from './pages/MergePage';
import { CustomFieldsPage } from './pages/CustomFieldsPage';
import { PicklistsPage } from './pages/PicklistsPage';
import { RecycleBinPage } from './pages/RecycleBinPage';
import { ReassignmentQueuePage } from './pages/ReassignmentQueuePage';

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
        path="/contacts"
        element={
          <AuthGuard>
            <Layout>
              <ContactsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/contacts/new"
        element={
          <AuthGuard>
            <Layout>
              <ContactFormPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/contacts/:id"
        element={
          <AuthGuard>
            <Layout>
              <ContactDetailPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/contacts/:id/edit"
        element={
          <AuthGuard>
            <Layout>
              <ContactFormPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/contacts/merge"
        element={
          <AuthGuard requireRole="manager">
            <Layout>
              <MergePage entityType="contact" />
            </Layout>
          </AuthGuard>
        }
      />

      <Route
        path="/companies"
        element={
          <AuthGuard>
            <Layout>
              <CompaniesPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/companies/new"
        element={
          <AuthGuard>
            <Layout>
              <CompanyFormPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/companies/:id"
        element={
          <AuthGuard>
            <Layout>
              <CompanyDetailPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/companies/:id/edit"
        element={
          <AuthGuard>
            <Layout>
              <CompanyFormPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/companies/merge"
        element={
          <AuthGuard requireRole="manager">
            <Layout>
              <MergePage entityType="company" />
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
        path="/picklists"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <PicklistsPage />
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
      <Route
        path="/reassignment-queue"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <ReassignmentQueuePage />
            </Layout>
          </AuthGuard>
        }
      />

      <Route path="/" element={<Navigate to="/contacts" replace />} />
      <Route path="*" element={<Navigate to="/contacts" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AuthProvider>
          <BrowserRouter basename="/customer">
            <AppRoutes />
            <ToastContainer />
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}

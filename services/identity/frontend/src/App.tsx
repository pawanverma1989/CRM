import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './contexts/AuthContext';
import { ToastProvider } from './contexts/ToastContext';
import { ToastContainer } from './components/ToastContainer';
import { AuthGuard } from './components/AuthGuard';
import { Layout } from './components/Layout';
import { LoginPage } from './pages/LoginPage';
import { ForgotPasswordPage } from './pages/ForgotPasswordPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { AcceptInvitationPage } from './pages/AcceptInvitationPage';
import { UsersPage } from './pages/UsersPage';
import { UserDetailPage } from './pages/UserDetailPage';
import { TeamsPage } from './pages/TeamsPage';
import { SettingsPage } from './pages/SettingsPage';
import { ProfilePage } from './pages/ProfilePage';

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
      {/* Public routes */}
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/accept-invitation" element={<AcceptInvitationPage />} />

      {/* Protected routes */}
      <Route
        path="/profile"
        element={
          <AuthGuard>
            <Layout>
              <ProfilePage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/users"
        element={
          <AuthGuard requireRole="manager">
            <Layout>
              <UsersPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/users/:id"
        element={
          <AuthGuard requireRole="manager">
            <Layout>
              <UserDetailPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/teams"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <TeamsPage />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/settings"
        element={
          <AuthGuard requireRole="admin">
            <Layout>
              <SettingsPage />
            </Layout>
          </AuthGuard>
        }
      />

      {/* Default redirect */}
      <Route path="/" element={<Navigate to="/profile" replace />} />
      <Route path="*" element={<Navigate to="/profile" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <AuthProvider>
          <BrowserRouter>
            <AppRoutes />
            <ToastContainer />
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
    </QueryClientProvider>
  );
}

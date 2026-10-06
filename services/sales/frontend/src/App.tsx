import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './contexts/AuthContext';
import { ToastProvider } from './contexts/ToastContext';
import { ToastContainer } from './components/ToastContainer';
import { AuthGuard } from './components/AuthGuard';
import { Layout } from './components/Layout';
import { BoardPage } from './pages/BoardPage';
import { DealsPage } from './pages/DealsPage';
import { DealDetailPage } from './pages/DealDetailPage';
import { DealFormPage } from './pages/DealFormPage';
import { PipelinesPage } from './pages/PipelinesPage';
import { LossReasonsPage } from './pages/LossReasonsPage';
import { CustomFieldsPage } from './pages/CustomFieldsPage';
import { RecycleBinPage } from './pages/RecycleBinPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename="/sales/">
        <AuthProvider>
          <ToastProvider>
            <ToastContainer />
            <Routes>
              <Route
                path="/"
                element={
                  <AuthGuard>
                    <Layout />
                  </AuthGuard>
                }
              >
                <Route index element={<BoardPage />} />
                <Route path="deals" element={<DealsPage />} />
                <Route path="deals/new" element={<DealFormPage />} />
                <Route path="deals/:id" element={<DealDetailPage />} />
                <Route path="deals/:id/edit" element={<DealFormPage />} />
                <Route
                  path="pipelines"
                  element={
                    <AuthGuard requireRole="admin">
                      <PipelinesPage />
                    </AuthGuard>
                  }
                />
                <Route
                  path="loss-reasons"
                  element={
                    <AuthGuard requireRole="admin">
                      <LossReasonsPage />
                    </AuthGuard>
                  }
                />
                <Route
                  path="custom-fields"
                  element={
                    <AuthGuard requireRole="admin">
                      <CustomFieldsPage />
                    </AuthGuard>
                  }
                />
                <Route
                  path="recycle-bin"
                  element={
                    <AuthGuard requireRole="admin">
                      <RecycleBinPage />
                    </AuthGuard>
                  }
                />
              </Route>
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </ToastProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}

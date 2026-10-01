import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';

interface AuthGuardProps {
  children: React.ReactNode;
  requireRole?: 'admin' | 'manager';
}

export function AuthGuard({ children, requireRole }: AuthGuardProps) {
  const { isAuthenticated, isLoading, role } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (requireRole === 'admin' && role !== 'admin') {
    return <Navigate to="/profile" replace />;
  }

  if (requireRole === 'manager' && role !== 'admin' && role !== 'manager') {
    return <Navigate to="/profile" replace />;
  }

  return <>{children}</>;
}

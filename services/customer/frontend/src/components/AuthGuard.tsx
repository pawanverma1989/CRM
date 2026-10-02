import { useAuth } from '../contexts/AuthContext';
import { usePermissions } from '../hooks/usePermissions';
import { Navigate } from 'react-router-dom';

interface AuthGuardProps {
  children: React.ReactNode;
  /** 'manager' admits admins too, as in the Identity app. */
  requireRole?: 'admin' | 'manager';
}

export function AuthGuard({ children, requireRole }: AuthGuardProps) {
  const { isAuthenticated, isLoading, isSignedOut } = useAuth();
  const { role } = usePermissions();

  if (isLoading) {
    return (
      <div className="min-h-screen flex items-center justify-center" role="status" aria-live="polite">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
        <span className="sr-only">Restoring your session…</span>
      </div>
    );
  }

  if (!isAuthenticated) {
    // AuthContext has already started a full navigation to the Identity app's
    // /login. This is the fallback if the browser blocks that navigation.
    return (
      <div className="min-h-screen flex items-center justify-center p-6">
        <div className="max-w-sm text-center">
          <h1 className="text-lg font-semibold text-gray-900 mb-2">Please sign in</h1>
          <p className="text-sm text-gray-600 mb-4">
            {isSignedOut
              ? 'Your session has ended.'
              : 'We could not confirm your session.'}{' '}
            Sign in again to open the customer records.
          </p>
          <a
            href="/login"
            className="inline-block px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
          >
            Go to sign in
          </a>
        </div>
      </div>
    );
  }

  if (requireRole === 'admin' && role !== 'admin') {
    return <Navigate to="/contacts" replace />;
  }

  if (requireRole === 'manager' && role !== 'admin' && role !== 'manager') {
    return <Navigate to="/contacts" replace />;
  }

  return <>{children}</>;
}

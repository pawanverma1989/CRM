import { Link, NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { getInitials } from '../lib/utils';

interface LayoutProps {
  children: React.ReactNode;
}

export function Layout({ children }: LayoutProps) {
  const { user, role, logout } = useAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-2 rounded-md text-sm font-medium transition-colors ${
      isActive
        ? 'bg-primary-700 text-white'
        : 'text-primary-100 hover:bg-primary-700 hover:text-white'
    }`;

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-primary-800 shadow-sm">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            <div className="flex items-center gap-6">
              <Link to="/profile" className="text-white font-semibold text-lg">
                CRM
              </Link>
              <div className="flex items-center gap-1">
                {(role === 'admin' || role === 'manager') && (
                  <NavLink to="/users" className={navLinkClass}>
                    Users
                  </NavLink>
                )}
                {role === 'admin' && (
                  <>
                    <NavLink to="/teams" className={navLinkClass}>
                      Teams
                    </NavLink>
                    <NavLink to="/settings" className={navLinkClass}>
                      Settings
                    </NavLink>
                  </>
                )}
                <NavLink to="/profile" className={navLinkClass}>
                  Profile
                </NavLink>
              </div>
            </div>
            <div className="flex items-center gap-3">
              {user && (
                <div className="flex items-center gap-2">
                  <div className="w-8 h-8 rounded-full bg-primary-600 flex items-center justify-center text-white text-sm font-medium">
                    {getInitials(user.firstName, user.lastName)}
                  </div>
                  <span className="text-primary-100 text-sm">
                    {user.firstName} {user.lastName}
                  </span>
                </div>
              )}
              <button
                onClick={handleLogout}
                className="text-primary-200 hover:text-white text-sm transition-colors"
              >
                Sign out
              </button>
            </div>
          </div>
        </div>
      </nav>
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">{children}</main>
    </div>
  );
}

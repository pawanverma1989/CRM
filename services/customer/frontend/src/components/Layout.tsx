import { Link, NavLink } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { usePermissions } from '../hooks/usePermissions';
import { getInitials } from '../lib/utils';

interface LayoutProps {
  children: React.ReactNode;
}

export function Layout({ children }: LayoutProps) {
  const { user, signOut } = useAuth();
  const { isAdmin, canViewReassignmentQueue, canManageCustomFields, canManagePicklists, canRestore } =
    usePermissions();

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-2 rounded-md text-sm font-medium transition-colors ${
      isActive
        ? 'bg-primary-700 text-white'
        : 'text-primary-100 hover:bg-primary-700 hover:text-white'
    }`;

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-primary-800 shadow-sm" aria-label="Customer records">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex flex-wrap items-center justify-between gap-2 py-3 md:h-16 md:py-0">
            <div className="flex flex-wrap items-center gap-x-6 gap-y-1">
              <Link to="/contacts" className="text-white font-semibold text-lg">
                CRM
              </Link>
              <div className="flex flex-wrap items-center gap-1">
                <NavLink to="/contacts" className={navLinkClass}>
                  Contacts
                </NavLink>
                <NavLink to="/companies" className={navLinkClass}>
                  Companies
                </NavLink>
                {canManageCustomFields && (
                  <NavLink to="/custom-fields" className={navLinkClass}>
                    Custom fields
                  </NavLink>
                )}
                {canManagePicklists && (
                  <NavLink to="/picklists" className={navLinkClass}>
                    Picklists
                  </NavLink>
                )}
                {canRestore && (
                  <NavLink to="/recycle-bin" className={navLinkClass}>
                    Recycle bin
                  </NavLink>
                )}
                {canViewReassignmentQueue && (
                  <NavLink to="/reassignment-queue" className={navLinkClass}>
                    Reassignment
                  </NavLink>
                )}
                {/* Leaves this SPA: the Identity app owns users and settings. */}
                <a
                  href={isAdmin ? '/users' : '/profile'}
                  className="px-3 py-2 rounded-md text-sm font-medium text-primary-100 hover:bg-primary-700 hover:text-white transition-colors"
                >
                  {isAdmin ? 'Users' : 'Profile'}
                </a>
              </div>
            </div>
            <div className="flex items-center gap-3">
              {user && (
                <div className="flex items-center gap-2">
                  <div className="w-8 h-8 rounded-full bg-primary-600 flex items-center justify-center text-white text-sm font-medium">
                    {getInitials(user.firstName, user.lastName)}
                  </div>
                  <span className="text-primary-100 text-sm hidden sm:inline">
                    {user.firstName} {user.lastName}
                  </span>
                </div>
              )}
              <button
                onClick={signOut}
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

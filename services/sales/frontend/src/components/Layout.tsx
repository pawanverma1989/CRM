import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { usePermissions } from '../hooks/usePermissions';
import { getInitials } from '../lib/utils';

export function Layout() {
  const { user, signOut } = useAuth();
  const { isAdmin, canManageCustomFields, canManagePipelines, canManageLossReasons, canRestore } = usePermissions();

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `px-3 py-2 rounded-md text-sm font-medium transition-colors ${isActive ? 'bg-primary-700 text-white' : 'text-primary-100 hover:bg-primary-700 hover:text-white'}`;

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-primary-800 shadow-sm" aria-label="Sales pipeline">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex flex-wrap items-center justify-between gap-2 py-3 md:h-16 md:py-0">
            <div className="flex flex-wrap items-center gap-x-6 gap-y-1">
              <Link to="/" className="text-white font-semibold text-lg">CRM</Link>
              <div className="flex flex-wrap items-center gap-1">
                <NavLink to="/" end className={navLinkClass}>Board</NavLink>
                <NavLink to="/deals" className={navLinkClass}>Deals</NavLink>
                {canManagePipelines && <NavLink to="/pipelines" className={navLinkClass}>Pipelines</NavLink>}
                {canManageLossReasons && <NavLink to="/loss-reasons" className={navLinkClass}>Loss reasons</NavLink>}
                {canManageCustomFields && <NavLink to="/custom-fields" className={navLinkClass}>Custom fields</NavLink>}
                {canRestore && <NavLink to="/recycle-bin" className={navLinkClass}>Recycle bin</NavLink>}
                <a href={isAdmin ? '/users' : '/profile'} className="px-3 py-2 rounded-md text-sm font-medium text-primary-100 hover:bg-primary-700 hover:text-white transition-colors">
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
                  <span className="text-primary-100 text-sm hidden sm:inline">{user.firstName} {user.lastName}</span>
                </div>
              )}
              <button onClick={signOut} className="text-primary-200 hover:text-white text-sm transition-colors">Sign out</button>
            </div>
          </div>
        </div>
      </nav>
      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8"><Outlet /></main>
    </div>
  );
}

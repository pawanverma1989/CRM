import { useAuth } from '../contexts/AuthContext';

export interface Permissions {
  role: 'admin' | 'manager' | 'sales_rep' | null;
  isAdmin: boolean;
  isManager: boolean;
  canAssign: boolean;             // admin or manager
  canRestore: boolean;            // admin only
  canManageCustomFields: boolean; // admin only
  canManagePipelines: boolean;    // admin only
  canManageLossReasons: boolean;  // admin only
  canChooseOwner: boolean;        // admin or manager
}

export function usePermissions(): Permissions {
  const { role } = useAuth();
  const isAdmin = role === 'admin';
  const isManager = role === 'manager';
  return {
    role,
    isAdmin,
    isManager,
    canAssign: isAdmin || isManager,
    canRestore: isAdmin,
    canManageCustomFields: isAdmin,
    canManagePipelines: isAdmin,
    canManageLossReasons: isAdmin,
    canChooseOwner: isAdmin || isManager,
  };
}

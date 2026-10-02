import { useAuth } from '../contexts/AuthContext';

export interface Permissions {
  role: 'admin' | 'manager' | 'sales_rep' | null;
  isAdmin: boolean;
  isManager: boolean;
  canAssign: boolean;          // admin or manager (ASG-2)
  canConvert: boolean;         // any role that can view the lead (CNV-1)
  canRestore: boolean;         // admin only (LLS-3)
  canManageCustomFields: boolean;  // admin only (LLS-4)
  canManageSources: boolean;   // admin only (CAP-2)
  canManageWebForms: boolean;  // admin only (WEB-1)
  canChooseOwner: boolean;     // admin or manager
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
    canConvert: true,
    canRestore: isAdmin,
    canManageCustomFields: isAdmin,
    canManageSources: isAdmin,
    canManageWebForms: isAdmin,
    canChooseOwner: isAdmin || isManager,
  };
}

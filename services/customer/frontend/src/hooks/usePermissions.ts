import { useAuth } from '../contexts/AuthContext';

/**
 * The §2 permission matrix, for hiding and disabling actions only. The server
 * enforces every one of these; a hidden button is a courtesy, not a control.
 */
export interface Permissions {
  role: 'admin' | 'manager' | 'sales_rep' | null;
  isAdmin: boolean;
  isManager: boolean;
  /** Admin or manager — the two roles that may merge and reassign. */
  canMerge: boolean;
  canReassign: boolean;
  /** Restoring from the recycle bin is admin-only in the MVP. */
  canRestore: boolean;
  canManageCustomFields: boolean;
  canManagePicklists: boolean;
  canViewReassignmentQueue: boolean;
  /** Admins and managers may pick any owner they can see; reps own what they create. */
  canChooseOwner: boolean;
}

export function usePermissions(): Permissions {
  const { role } = useAuth();
  const isAdmin = role === 'admin';
  const isManager = role === 'manager';

  return {
    role,
    isAdmin,
    isManager,
    canMerge: isAdmin || isManager,
    canReassign: isAdmin || isManager,
    canRestore: isAdmin,
    canManageCustomFields: isAdmin,
    canManagePicklists: isAdmin,
    canViewReassignmentQueue: isAdmin,
    canChooseOwner: isAdmin || isManager,
  };
}

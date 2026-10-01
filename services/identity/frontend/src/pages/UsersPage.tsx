import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  getUsers,
  inviteUser,
  resendInvite,
  cancelInvite,
  deactivateUser,
  reactivateUser,
} from '../api/users';
import { getTeams } from '../api/teams';
import { useAuth } from '../contexts/AuthContext';
import { useToast } from '../contexts/ToastContext';
import { Modal } from '../components/Modal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { formatDate, getStatusColor, getStatusLabel, getRoleLabel, getApiErrorMessage } from '../lib/utils';
import type { UserDto, UserRole } from '../types';

const inviteSchema = z.object({
  email: z.string().email('Enter a valid email'),
  role: z.enum(['admin', 'manager', 'sales_rep']),
  teamId: z.string().optional(),
});

type InviteFormData = z.infer<typeof inviteSchema>;

type ConfirmAction =
  | { type: 'deactivate'; user: UserDto }
  | { type: 'reactivate'; user: UserDto }
  | { type: 'cancel_invite'; user: UserDto };

export function UsersPage() {
  const { role } = useAuth();
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [search, setSearch] = useState('');
  const [filterRole, setFilterRole] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [filterTeam, setFilterTeam] = useState('');
  const [cursor, setCursor] = useState<string | undefined>();
  const [inviteOpen, setInviteOpen] = useState(false);
  const [confirmAction, setConfirmAction] = useState<ConfirmAction | null>(null);

  const { data: teamsData } = useQuery({
    queryKey: ['teams'],
    queryFn: getTeams,
  });

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['users', search, filterRole, filterStatus, filterTeam, cursor],
    queryFn: () =>
      getUsers({
        search: search || undefined,
        role: (filterRole as UserRole) || undefined,
        status: filterStatus || undefined,
        teamId: filterTeam || undefined,
        cursor,
        limit: 50,
      }),
    placeholderData: (prev) => prev,
  });

  const inviteMutation = useMutation({
    mutationFn: inviteUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      showToast('Invitation sent!', 'success');
      setInviteOpen(false);
      inviteReset();
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const resendMutation = useMutation({
    mutationFn: resendInvite,
    onSuccess: () => showToast('Invitation resent', 'success'),
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const actionMutation = useMutation({
    mutationFn: async (action: ConfirmAction) => {
      if (action.type === 'deactivate') return deactivateUser(action.user.id);
      if (action.type === 'reactivate') return reactivateUser(action.user.id);
      if (action.type === 'cancel_invite') return cancelInvite(action.user.id);
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      const messages: Record<string, string> = {
        deactivate: 'User deactivated',
        reactivate: 'User reactivated',
        cancel_invite: 'Invitation cancelled',
      };
      showToast(messages[variables.type] ?? 'Done', 'success');
      setConfirmAction(null);
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setConfirmAction(null);
    },
  });

  const {
    register: inviteRegister,
    handleSubmit: handleInviteSubmit,
    reset: inviteReset,
    formState: { errors: inviteErrors },
  } = useForm<InviteFormData>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { role: 'sales_rep' },
  });

  const onInviteSubmit = (data: InviteFormData) => {
    inviteMutation.mutate({
      email: data.email,
      role: data.role,
      teamId: data.teamId || undefined,
    });
  };

  const teamOptions = teamsData?.map((t) => ({ value: t.id, label: t.name })) ?? [];

  const confirmMessages: Record<string, { title: string; message: string; label: string; danger: boolean }> = {
    deactivate: {
      title: 'Deactivate user',
      message: 'This will prevent the user from signing in. You can reactivate them later.',
      label: 'Deactivate',
      danger: true,
    },
    reactivate: {
      title: 'Reactivate user',
      message: 'This will allow the user to sign in again.',
      label: 'Reactivate',
      danger: false,
    },
    cancel_invite: {
      title: 'Cancel invitation',
      message: 'The invitation link will become invalid.',
      label: 'Cancel invitation',
      danger: true,
    },
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Users</h1>
        {(role === 'admin' || role === 'manager') && (
          <button
            onClick={() => setInviteOpen(true)}
            className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 transition-colors"
          >
            Invite user
          </button>
        )}
      </div>

      {/* Filters */}
      <div className="bg-white rounded-lg border border-gray-200 p-4 mb-6 flex flex-wrap gap-3">
        <input
          type="search"
          placeholder="Search by name or email..."
          value={search}
          onChange={(e) => { setSearch(e.target.value); setCursor(undefined); }}
          className="flex-1 min-w-[200px] px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
        />
        <select
          value={filterRole}
          onChange={(e) => { setFilterRole(e.target.value); setCursor(undefined); }}
          className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white"
        >
          <option value="">All roles</option>
          <option value="admin">Admin</option>
          <option value="manager">Manager</option>
          <option value="sales_rep">Sales Rep</option>
        </select>
        <select
          value={filterStatus}
          onChange={(e) => { setFilterStatus(e.target.value); setCursor(undefined); }}
          className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white"
        >
          <option value="">All statuses</option>
          <option value="active">Active</option>
          <option value="invited">Invited</option>
          <option value="deactivated">Deactivated</option>
        </select>
        {teamOptions.length > 0 && (
          <select
            value={filterTeam}
            onChange={(e) => { setFilterTeam(e.target.value); setCursor(undefined); }}
            className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white"
          >
            <option value="">All teams</option>
            {teamOptions.map((t) => (
              <option key={t.value} value={t.value}>{t.label}</option>
            ))}
          </select>
        )}
      </div>

      {/* Table */}
      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <div className="flex items-center justify-center py-16">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
          </div>
        ) : (
          <>
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Name
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Role
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider hidden md:table-cell">
                    Team
                  </th>
                  <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider hidden lg:table-cell">
                    Last login
                  </th>
                  <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-200">
                {data?.data.map((user) => {
                  const team = teamsData?.find((t) => t.id === user.teamId);
                  return (
                    <tr key={user.id} className="hover:bg-gray-50">
                      <td className="px-6 py-4 whitespace-nowrap">
                        <div>
                          <p className="text-sm font-medium text-gray-900">
                            {user.firstName} {user.lastName}
                          </p>
                          <p className="text-xs text-gray-500">{user.email}</p>
                        </div>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                        {getRoleLabel(user.role)}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap">
                        <span
                          className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${getStatusColor(user.status)}`}
                        >
                          {getStatusLabel(user.status)}
                        </span>
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 hidden md:table-cell">
                        {team?.name ?? '—'}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 hidden lg:table-cell">
                        {user.lastLoginAt ? formatDate(user.lastLoginAt) : '—'}
                      </td>
                      <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                        <div className="flex items-center justify-end gap-2">
                          <Link
                            to={`/users/${user.id}`}
                            className="text-primary-600 hover:text-primary-700 font-medium"
                          >
                            View
                          </Link>
                          {user.status === 'invited' && (
                            <>
                              <button
                                onClick={() => resendMutation.mutate(user.id)}
                                className="text-gray-600 hover:text-gray-800 font-medium"
                                disabled={resendMutation.isPending}
                              >
                                Resend
                              </button>
                              <button
                                onClick={() => setConfirmAction({ type: 'cancel_invite', user })}
                                className="text-red-600 hover:text-red-700 font-medium"
                              >
                                Cancel
                              </button>
                            </>
                          )}
                          {user.status === 'active' && role === 'admin' && (
                            <button
                              onClick={() => setConfirmAction({ type: 'deactivate', user })}
                              className="text-red-600 hover:text-red-700 font-medium"
                            >
                              Deactivate
                            </button>
                          )}
                          {user.status === 'deactivated' && role === 'admin' && (
                            <button
                              onClick={() => setConfirmAction({ type: 'reactivate', user })}
                              className="text-green-600 hover:text-green-700 font-medium"
                            >
                              Reactivate
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })}
                {data?.data.length === 0 && (
                  <tr>
                    <td colSpan={6} className="px-6 py-12 text-center text-sm text-gray-500">
                      No users found
                    </td>
                  </tr>
                )}
              </tbody>
            </table>

            {/* Pagination */}
            <div className="px-6 py-3 border-t border-gray-200 flex items-center justify-between">
              <p className="text-sm text-gray-600">
                {isFetching ? 'Loading...' : `Showing ${data?.data.length ?? 0} users`}
              </p>
              <div className="flex gap-2">
                {cursor && (
                  <button
                    onClick={() => setCursor(undefined)}
                    className="px-3 py-1.5 text-sm text-gray-600 border border-gray-300 rounded-md hover:bg-gray-50"
                  >
                    First page
                  </button>
                )}
                {data?.hasMore && data.nextCursor && (
                  <button
                    onClick={() => setCursor(data.nextCursor)}
                    className="px-3 py-1.5 text-sm text-primary-600 border border-primary-300 rounded-md hover:bg-primary-50"
                  >
                    Load more
                  </button>
                )}
              </div>
            </div>
          </>
        )}
      </div>

      {/* Invite modal */}
      <Modal isOpen={inviteOpen} title="Invite user" onClose={() => { setInviteOpen(false); inviteReset(); }}>
        <form onSubmit={handleInviteSubmit(onInviteSubmit)} className="space-y-4" noValidate>
          <FormField
            label="Email address"
            type="email"
            error={inviteErrors.email?.message}
            {...inviteRegister('email')}
          />
          <SelectField
            label="Role"
            options={[
              { value: 'admin', label: 'Admin' },
              { value: 'manager', label: 'Manager' },
              { value: 'sales_rep', label: 'Sales Rep' },
            ]}
            error={inviteErrors.role?.message}
            {...inviteRegister('role')}
          />
          {teamOptions.length > 0 && (
            <SelectField
              label="Team (optional)"
              options={teamOptions}
              placeholder="No team"
              {...inviteRegister('teamId')}
            />
          )}
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => { setInviteOpen(false); inviteReset(); }}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={inviteMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {inviteMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Send invitation
            </button>
          </div>
        </form>
      </Modal>

      {/* Confirm dialog */}
      {confirmAction && (
        <ConfirmDialog
          isOpen
          title={confirmMessages[confirmAction.type].title}
          message={`${confirmMessages[confirmAction.type].message}\n\nUser: ${confirmAction.user.firstName} ${confirmAction.user.lastName} (${confirmAction.user.email})`}
          confirmLabel={confirmMessages[confirmAction.type].label}
          danger={confirmMessages[confirmAction.type].danger}
          isLoading={actionMutation.isPending}
          onConfirm={() => actionMutation.mutate(confirmAction)}
          onCancel={() => setConfirmAction(null)}
        />
      )}
    </div>
  );
}

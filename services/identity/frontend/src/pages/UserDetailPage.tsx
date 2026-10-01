import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { getUser, updateUser, deactivateUser, reactivateUser, logoutUserAll } from '../api/users';
import { getTeams } from '../api/teams';
import { useAuth } from '../contexts/AuthContext';
import { useToast } from '../contexts/ToastContext';
import { SelectField } from '../components/SelectField';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { formatDate, formatDateTime, getRoleLabel, getStatusLabel, getStatusColor, getApiErrorMessage } from '../lib/utils';
import { useState } from 'react';
import type { UserRole } from '../types';

const editSchema = z.object({
  role: z.enum(['admin', 'manager', 'sales_rep']),
  teamId: z.string().optional(),
});

type EditFormData = z.infer<typeof editSchema>;

type ConfirmType = 'deactivate' | 'reactivate' | 'logout_all';

export function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { role: myRole } = useAuth();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [confirm, setConfirm] = useState<ConfirmType | null>(null);

  const { data: user, isLoading } = useQuery({
    queryKey: ['user', id],
    queryFn: () => getUser(id!),
    enabled: !!id,
  });

  const { data: teams } = useQuery({
    queryKey: ['teams'],
    queryFn: getTeams,
  });

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<EditFormData>({
    resolver: zodResolver(editSchema),
    values: user ? { role: user.role, teamId: user.teamId ?? '' } : undefined,
  });

  const updateMutation = useMutation({
    mutationFn: (data: EditFormData) =>
      updateUser(id!, { role: data.role as UserRole, teamId: data.teamId || null }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['user', id] });
      queryClient.invalidateQueries({ queryKey: ['users'] });
      showToast('User updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const actionMutation = useMutation({
    mutationFn: async (type: ConfirmType) => {
      if (type === 'deactivate') return deactivateUser(id!);
      if (type === 'reactivate') return reactivateUser(id!);
      if (type === 'logout_all') return logoutUserAll(id!);
    },
    onSuccess: (_data, type) => {
      if (type !== 'logout_all') queryClient.invalidateQueries({ queryKey: ['user', id] });
      queryClient.invalidateQueries({ queryKey: ['users'] });
      showToast(
        type === 'deactivate' ? 'User deactivated' : type === 'reactivate' ? 'User reactivated' : 'User sessions ended',
        'success'
      );
      setConfirm(null);
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setConfirm(null);
    },
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
      </div>
    );
  }

  if (!user) {
    return (
      <div className="text-center py-16">
        <p className="text-gray-500">User not found.</p>
        <button onClick={() => navigate('/users')} className="text-primary-600 hover:underline text-sm mt-2">
          Back to users
        </button>
      </div>
    );
  }

  const teamOptions = teams?.map((t) => ({ value: t.id, label: t.name })) ?? [];

  const confirmConfig: Record<ConfirmType, { title: string; message: string; label: string; danger: boolean }> = {
    deactivate: {
      title: 'Deactivate user',
      message: `Deactivate ${user.firstName} ${user.lastName}? They will not be able to sign in.`,
      label: 'Deactivate',
      danger: true,
    },
    reactivate: {
      title: 'Reactivate user',
      message: `Reactivate ${user.firstName} ${user.lastName}? They will be able to sign in again.`,
      label: 'Reactivate',
      danger: false,
    },
    logout_all: {
      title: 'End all sessions',
      message: `This will immediately sign out ${user.firstName} ${user.lastName} from all devices.`,
      label: 'End sessions',
      danger: true,
    },
  };

  return (
    <div className="max-w-2xl">
      <button
        onClick={() => navigate('/users')}
        className="text-sm text-gray-500 hover:text-gray-700 mb-6 flex items-center gap-1"
      >
        ← Back to users
      </button>

      <div className="bg-white rounded-lg border border-gray-200 p-6 mb-6">
        <div className="flex items-start justify-between mb-4">
          <div>
            <h1 className="text-xl font-bold text-gray-900">
              {user.firstName} {user.lastName}
            </h1>
            <p className="text-sm text-gray-500">{user.email}</p>
            {user.phone && <p className="text-sm text-gray-500">{user.phone}</p>}
          </div>
          <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${getStatusColor(user.status)}`}>
            {getStatusLabel(user.status)}
          </span>
        </div>
        <dl className="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
          <div>
            <dt className="text-gray-500">Role</dt>
            <dd className="text-gray-900 font-medium">{getRoleLabel(user.role)}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Team</dt>
            <dd className="text-gray-900 font-medium">
              {teams?.find((t) => t.id === user.teamId)?.name ?? '—'}
            </dd>
          </div>
          <div>
            <dt className="text-gray-500">Last login</dt>
            <dd className="text-gray-900">{user.lastLoginAt ? formatDateTime(user.lastLoginAt) : '—'}</dd>
          </div>
          <div>
            <dt className="text-gray-500">Joined</dt>
            <dd className="text-gray-900">{formatDate(user.createdAt)}</dd>
          </div>
        </dl>
      </div>

      {myRole === 'admin' && (
        <>
          <div className="bg-white rounded-lg border border-gray-200 p-6 mb-4">
            <h2 className="text-base font-semibold text-gray-900 mb-4">Edit user</h2>
            <form onSubmit={handleSubmit((d) => updateMutation.mutate(d))} className="space-y-4" noValidate>
              <SelectField
                label="Role"
                options={[
                  { value: 'admin', label: 'Admin' },
                  { value: 'manager', label: 'Manager' },
                  { value: 'sales_rep', label: 'Sales Rep' },
                ]}
                error={errors.role?.message}
                {...register('role')}
              />
              {teamOptions.length > 0 && (
                <SelectField
                  label="Team"
                  options={teamOptions}
                  placeholder="No team"
                  {...register('teamId')}
                />
              )}
              <div className="flex justify-end">
                <button
                  type="submit"
                  disabled={!isDirty || updateMutation.isPending}
                  className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
                >
                  {updateMutation.isPending && (
                    <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
                  )}
                  Save changes
                </button>
              </div>
            </form>
          </div>

          <div className="bg-white rounded-lg border border-gray-200 p-6">
            <h2 className="text-base font-semibold text-gray-900 mb-4">Actions</h2>
            <div className="flex flex-wrap gap-3">
              {user.status === 'active' && (
                <>
                  <button
                    onClick={() => setConfirm('logout_all')}
                    className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
                  >
                    End all sessions
                  </button>
                  <button
                    onClick={() => setConfirm('deactivate')}
                    className="px-4 py-2 text-sm font-medium text-white bg-red-600 rounded-md hover:bg-red-700"
                  >
                    Deactivate user
                  </button>
                </>
              )}
              {user.status === 'deactivated' && (
                <button
                  onClick={() => setConfirm('reactivate')}
                  className="px-4 py-2 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700"
                >
                  Reactivate user
                </button>
              )}
            </div>
          </div>
        </>
      )}

      {confirm && (
        <ConfirmDialog
          isOpen
          title={confirmConfig[confirm].title}
          message={confirmConfig[confirm].message}
          confirmLabel={confirmConfig[confirm].label}
          danger={confirmConfig[confirm].danger}
          isLoading={actionMutation.isPending}
          onConfirm={() => actionMutation.mutate(confirm)}
          onCancel={() => setConfirm(null)}
        />
      )}
    </div>
  );
}

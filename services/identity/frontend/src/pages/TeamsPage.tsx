import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { getTeams, createTeam, updateTeam, deleteTeam } from '../api/teams';
import { getUsers } from '../api/users';
import { useToast } from '../contexts/ToastContext';
import { Modal } from '../components/Modal';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { formatDate, getApiErrorMessage } from '../lib/utils';
import type { TeamDto } from '../types';

const teamSchema = z.object({
  name: z.string().min(1, 'Team name is required').max(100),
  managerId: z.string().optional(),
});

type TeamFormData = z.infer<typeof teamSchema>;

export function TeamsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [editTeam, setEditTeam] = useState<TeamDto | null>(null);
  const [deleteTeamTarget, setDeleteTeamTarget] = useState<TeamDto | null>(null);

  const { data: teams, isLoading } = useQuery({
    queryKey: ['teams'],
    queryFn: getTeams,
  });

  const { data: usersData } = useQuery({
    queryKey: ['users', '', '', 'active', ''],
    queryFn: () => getUsers({ status: 'active', limit: 200 }),
  });

  const createMutation = useMutation({
    mutationFn: createTeam,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['teams'] });
      showToast('Team created', 'success');
      setCreateOpen(false);
      createReset();
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: TeamFormData }) =>
      updateTeam(id, { name: data.name, managerId: data.managerId || null }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['teams'] });
      showToast('Team updated', 'success');
      setEditTeam(null);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteTeam(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['teams'] });
      showToast('Team deleted', 'success');
      setDeleteTeamTarget(null);
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setDeleteTeamTarget(null);
    },
  });

  const {
    register: createRegister,
    handleSubmit: handleCreateSubmit,
    reset: createReset,
    formState: { errors: createErrors },
  } = useForm<TeamFormData>({ resolver: zodResolver(teamSchema) });

  const {
    register: editRegister,
    handleSubmit: handleEditSubmit,
    formState: { errors: editErrors, isDirty: editDirty },
    reset: editReset,
  } = useForm<TeamFormData>({
    resolver: zodResolver(teamSchema),
    values: editTeam ? { name: editTeam.name, managerId: editTeam.managerId ?? '' } : undefined,
  });

  const managerOptions =
    usersData?.data
      .filter((u) => u.role === 'admin' || u.role === 'manager')
      .map((u) => ({ value: u.id, label: `${u.firstName} ${u.lastName}` })) ?? [];

  const getUserName = (id: string) => {
    const u = usersData?.data.find((u) => u.id === id);
    return u ? `${u.firstName} ${u.lastName}` : id;
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Teams</h1>
        <button
          onClick={() => setCreateOpen(true)}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Create team
        </button>
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center py-16">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
        </div>
      ) : (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Team name
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider hidden md:table-cell">
                  Manager
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider hidden lg:table-cell">
                  Created
                </th>
                <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Actions
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {teams?.map((team) => (
                <tr key={team.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap">
                    <p className="text-sm font-medium text-gray-900">{team.name}</p>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 hidden md:table-cell">
                    {team.managerId ? getUserName(team.managerId) : '—'}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600 hidden lg:table-cell">
                    {formatDate(team.createdAt)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm">
                    <div className="flex items-center justify-end gap-3">
                      <button
                        onClick={() => { setEditTeam(team); editReset({ name: team.name, managerId: team.managerId ?? '' }); }}
                        className="text-primary-600 hover:text-primary-700 font-medium"
                      >
                        Edit
                      </button>
                      <button
                        onClick={() => setDeleteTeamTarget(team)}
                        className="text-red-600 hover:text-red-700 font-medium"
                      >
                        Delete
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
              {teams?.length === 0 && (
                <tr>
                  <td colSpan={4} className="px-6 py-12 text-center text-sm text-gray-500">
                    No teams yet. Create one to get started.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Create team modal */}
      <Modal isOpen={createOpen} title="Create team" onClose={() => { setCreateOpen(false); createReset(); }}>
        <form onSubmit={handleCreateSubmit((d) => createMutation.mutate(d))} className="space-y-4" noValidate>
          <FormField
            label="Team name"
            type="text"
            error={createErrors.name?.message}
            {...createRegister('name')}
          />
          {managerOptions.length > 0 && (
            <SelectField
              label="Manager (optional)"
              options={managerOptions}
              placeholder="No manager"
              {...createRegister('managerId')}
            />
          )}
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => { setCreateOpen(false); createReset(); }}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={createMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {createMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Create team
            </button>
          </div>
        </form>
      </Modal>

      {/* Edit team modal */}
      <Modal isOpen={!!editTeam} title="Edit team" onClose={() => setEditTeam(null)}>
        <form
          onSubmit={handleEditSubmit((d) => updateMutation.mutate({ id: editTeam!.id, data: d }))}
          className="space-y-4"
          noValidate
        >
          <FormField
            label="Team name"
            type="text"
            error={editErrors.name?.message}
            {...editRegister('name')}
          />
          {managerOptions.length > 0 && (
            <SelectField
              label="Manager (optional)"
              options={managerOptions}
              placeholder="No manager"
              {...editRegister('managerId')}
            />
          )}
          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => setEditTeam(null)}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={!editDirty || updateMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {updateMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Save changes
            </button>
          </div>
        </form>
      </Modal>

      {/* Delete confirm */}
      <ConfirmDialog
        isOpen={!!deleteTeamTarget}
        title="Delete team"
        message={`Delete "${deleteTeamTarget?.name}"? This cannot be undone. Members will be unassigned from this team.`}
        confirmLabel="Delete team"
        danger
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleteTeamTarget && deleteMutation.mutate(deleteTeamTarget.id)}
        onCancel={() => setDeleteTeamTarget(null)}
      />
    </div>
  );
}

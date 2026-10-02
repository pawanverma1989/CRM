import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getDisqualifyReasons, createDisqualifyReason, updateDisqualifyReason } from '../api/disqualifyReasons';
import { useToast } from '../contexts/ToastContext';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { getApiErrorMessage } from '../lib/utils';

export function DisqualifyReasonsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [nameError, setNameError] = useState('');

  const { data: reasons = [], isLoading, isError, error, refetch } = useQuery({
    queryKey: ['disqualify-reasons', 'all'],
    queryFn: getDisqualifyReasons,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['disqualify-reasons'] });

  const createMutation = useMutation({
    mutationFn: () => createDisqualifyReason(newName.trim()),
    onSuccess: () => {
      invalidate();
      showToast('Disqualify reason created', 'success');
      setCreateOpen(false);
      setNewName('');
      setNameError('');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleActiveMutation = useMutation({
    mutationFn: (reason: { id: string; isActive: boolean }) =>
      updateDisqualifyReason(reason.id, { isActive: !reason.isActive }),
    onSuccess: (_, reason) => {
      invalidate();
      showToast(reason.isActive ? 'Reason deactivated' : 'Reason reactivated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const handleCreate = () => {
    if (!newName.trim()) {
      setNameError('Name is required');
      return;
    }
    setNameError('');
    createMutation.mutate();
  };

  return (
    <div className="max-w-3xl">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Disqualify reasons</h1>
        <button
          type="button"
          onClick={() => { setCreateOpen(true); setNewName(''); setNameError(''); }}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add reason
        </button>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading disqualify reasons…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load disqualify reasons" onRetry={refetch} />
        ) : reasons.length === 0 ? (
          <EmptyBlock title="No disqualify reasons yet" message="Add the first one." />
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {reasons.map((reason) => (
                <tr key={reason.id} className={reason.isActive ? '' : 'opacity-60'}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{reason.name}</td>
                  <td className="px-4 py-3 text-sm">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${reason.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                      {reason.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => toggleActiveMutation.mutate(reason)}
                      disabled={toggleActiveMutation.isPending}
                      className="text-sm font-medium text-gray-600 hover:text-gray-800 disabled:opacity-50"
                    >
                      {reason.isActive ? 'Deactivate' : 'Reactivate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Modal isOpen={createOpen} title="Add disqualify reason" onClose={() => setCreateOpen(false)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            error={nameError}
            autoFocus
          />
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setCreateOpen(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="button"
              onClick={handleCreate}
              disabled={createMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {createMutation.isPending && <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />}
              Create
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

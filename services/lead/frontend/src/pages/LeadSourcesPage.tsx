import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getLeadSources, createLeadSource, updateLeadSource } from '../api/leadSources';
import { useToast } from '../contexts/ToastContext';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { getApiErrorMessage } from '../lib/utils';

export function LeadSourcesPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [nameError, setNameError] = useState('');

  const { data: sources = [], isLoading, isError, error, refetch } = useQuery({
    queryKey: ['lead-sources', 'all'],
    queryFn: getLeadSources,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['lead-sources'] });

  const createMutation = useMutation({
    mutationFn: () => createLeadSource(newName.trim()),
    onSuccess: () => {
      invalidate();
      showToast('Lead source created', 'success');
      setCreateOpen(false);
      setNewName('');
      setNameError('');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleActiveMutation = useMutation({
    mutationFn: (source: { id: string; isActive: boolean }) =>
      updateLeadSource(source.id, { isActive: !source.isActive }),
    onSuccess: (_, source) => {
      invalidate();
      showToast(source.isActive ? 'Source deactivated' : 'Source reactivated', 'success');
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
        <h1 className="text-2xl font-bold text-gray-900">Lead sources</h1>
        <button
          type="button"
          onClick={() => { setCreateOpen(true); setNewName(''); setNameError(''); }}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add source
        </button>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading lead sources…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load lead sources" onRetry={refetch} />
        ) : sources.length === 0 ? (
          <EmptyBlock title="No lead sources yet" message="Add the first one." />
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
              {sources.map((source) => (
                <tr key={source.id} className={source.isActive ? '' : 'opacity-60'}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{source.name}</td>
                  <td className="px-4 py-3 text-sm">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${source.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                      {source.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => toggleActiveMutation.mutate(source)}
                      disabled={toggleActiveMutation.isPending}
                      className="text-sm font-medium text-gray-600 hover:text-gray-800 disabled:opacity-50"
                    >
                      {source.isActive ? 'Deactivate' : 'Reactivate'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Modal isOpen={createOpen} title="Add lead source" onClose={() => setCreateOpen(false)}>
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

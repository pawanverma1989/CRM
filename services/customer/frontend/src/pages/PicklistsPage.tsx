import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { getPicklist, createPicklistItem, updatePicklistItem } from '../api/picklists';
import { picklistItemSchema, type PicklistItemFormValues } from '../lib/validation';
import { useToast } from '../contexts/ToastContext';
import { getApiErrorMessage } from '../lib/utils';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import type { PicklistItemDto, PicklistType } from '../types';

const listLabels: Record<PicklistType, string> = {
  company_industry: 'Company industries',
  contact_source: 'Contact sources',
};

export function PicklistsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [listType, setListType] = useState<PicklistType>('company_industry');
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<PicklistItemDto | null>(null);

  const { data: items = [], isLoading, isError, error, refetch } = useQuery({
    queryKey: ['picklist', listType, 'all'],
    queryFn: () => getPicklist(listType, true),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['picklist'] });

  const createForm = useForm<PicklistItemFormValues>({
    resolver: zodResolver(picklistItemSchema),
    defaultValues: { listType, value: '', sortOrder: 0 },
  });

  const editForm = useForm<PicklistItemFormValues>({
    resolver: zodResolver(picklistItemSchema),
  });

  const createMutation = useMutation({
    mutationFn: (values: PicklistItemFormValues) =>
      createPicklistItem({ listType, value: values.value.trim(), sortOrder: Number(values.sortOrder) }),
    onSuccess: () => {
      invalidate();
      showToast('Value added', 'success');
      setCreateOpen(false);
      createForm.reset({ listType, value: '', sortOrder: 0 });
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: (values: PicklistItemFormValues) => {
      if (!editing) throw new Error('No value selected');
      return updatePicklistItem(editing.id, { value: values.value.trim(), sortOrder: Number(values.sortOrder) });
    },
    onSuccess: () => {
      invalidate();
      showToast('Value saved', 'success');
      setEditing(null);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleActiveMutation = useMutation({
    mutationFn: (item: PicklistItemDto) => updatePicklistItem(item.id, { isActive: !item.isActive }),
    onSuccess: (_, item) => {
      invalidate();
      showToast(item.isActive ? 'Value deactivated' : 'Value reactivated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const openEdit = (item: PicklistItemDto) => {
    setEditing(item);
    editForm.reset({ listType: item.listType, value: item.value, sortOrder: item.sortOrder });
  };

  return (
    <div className="max-w-2xl">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Picklists</h1>
        <button
          type="button"
          onClick={() => {
            createForm.reset({ listType, value: '', sortOrder: 0 });
            setCreateOpen(true);
          }}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add value
        </button>
      </div>

      <div className="flex gap-2 mb-6" role="tablist" aria-label="Picklist">
        {(['company_industry', 'contact_source'] as const).map((t) => (
          <button
            key={t}
            type="button"
            role="tab"
            aria-selected={listType === t}
            onClick={() => setListType(t)}
            className={`px-4 py-2 rounded-md text-sm font-medium ${
              listType === t ? 'bg-primary-600 text-white' : 'bg-white text-gray-700 border border-gray-300 hover:bg-gray-50'
            }`}
          >
            {listLabels[t]}
          </button>
        ))}
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading values…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load this list" onRetry={refetch} />
        ) : items.length === 0 ? (
          <EmptyBlock title="No values yet" message="Add the first value for this list." />
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Value</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Order</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {[...items].sort((a, b) => a.sortOrder - b.sortOrder).map((item) => (
                <tr key={item.id} className={item.isActive ? '' : 'opacity-60'}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{item.value}</td>
                  <td className="px-4 py-3 text-sm text-gray-600">{item.sortOrder}</td>
                  <td className="px-4 py-3 text-sm">
                    <span
                      className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${
                        item.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'
                      }`}
                    >
                      {item.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right text-sm">
                    <div className="flex justify-end gap-3">
                      <button onClick={() => openEdit(item)} className="text-primary-600 hover:text-primary-700 font-medium">
                        Edit
                      </button>
                      <button
                        onClick={() => toggleActiveMutation.mutate(item)}
                        className="text-gray-600 hover:text-gray-800 font-medium"
                      >
                        {item.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Modal isOpen={createOpen} title={`Add ${listLabels[listType].toLowerCase()} value`} onClose={() => setCreateOpen(false)}>
        <form onSubmit={createForm.handleSubmit((v) => createMutation.mutate(v))} className="space-y-4" noValidate>
          <FormField label="Value" error={createForm.formState.errors.value?.message} {...createForm.register('value')} />
          <FormField
            label="Display order"
            type="number"
            error={createForm.formState.errors.sortOrder?.message}
            {...createForm.register('sortOrder')}
          />
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={() => setCreateOpen(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="submit"
              disabled={createMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {createMutation.isPending && <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />}
              Add value
            </button>
          </div>
        </form>
      </Modal>

      <Modal isOpen={!!editing} title="Edit value" onClose={() => setEditing(null)}>
        <form onSubmit={editForm.handleSubmit((v) => updateMutation.mutate(v))} className="space-y-4" noValidate>
          <FormField label="Value" error={editForm.formState.errors.value?.message} {...editForm.register('value')} />
          <FormField
            label="Display order"
            type="number"
            error={editForm.formState.errors.sortOrder?.message}
            {...editForm.register('sortOrder')}
          />
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" onClick={() => setEditing(null)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="submit"
              disabled={updateMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {updateMutation.isPending && <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />}
              Save changes
            </button>
          </div>
        </form>
      </Modal>
    </div>
  );
}

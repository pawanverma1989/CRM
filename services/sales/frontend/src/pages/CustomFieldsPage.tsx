import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listCustomFields, createCustomField, updateCustomField } from '../api/customFields';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { getApiErrorMessage } from '../lib/utils';
import { CUSTOM_FIELD_TYPE_LABELS, fieldTypeNeedsOptions } from '../lib/customFields';
import type { CustomFieldDefinitionDto, CustomFieldType } from '../types';

const FIELD_TYPES: CustomFieldType[] = ['text', 'textarea', 'number', 'currency', 'boolean', 'date', 'datetime', 'select'];

interface FieldFormState {
  label: string;
  fieldKey: string;
  fieldType: CustomFieldType;
  isRequired: boolean;
  isActive: boolean;
  optionsRaw: string;
}

const emptyForm = (): FieldFormState => ({
  label: '',
  fieldKey: '',
  fieldType: 'text',
  isRequired: false,
  isActive: true,
  optionsRaw: '',
});

function slugify(s: string) {
  return s.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
}

export function CustomFieldsPage() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<CustomFieldDefinitionDto | null>(null);
  const [form, setForm] = useState<FieldFormState>(emptyForm());
  const [autoSlug, setAutoSlug] = useState(true);

  const { data: fields = [], isLoading, error, refetch } = useQuery({
    queryKey: ['custom-fields'],
    queryFn: () => listCustomFields('deal'),
  });

  const createMutation = useMutation({
    mutationFn: () =>
      createCustomField({
        entityType: 'deal',
        label: form.label,
        fieldKey: form.fieldKey,
        fieldType: form.fieldType,
        isRequired: form.isRequired,
        options: fieldTypeNeedsOptions(form.fieldType) ? form.optionsRaw.split('\n').map((o) => o.trim()).filter(Boolean) : undefined,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['custom-fields'] });
      setShowCreate(false);
      showToast('Custom field created', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: () =>
      updateCustomField(editing!.id, {
        label: form.label,
        isRequired: form.isRequired,
        isActive: form.isActive,
        options: fieldTypeNeedsOptions(form.fieldType) ? form.optionsRaw.split('\n').map((o) => o.trim()).filter(Boolean) : undefined,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['custom-fields'] });
      setEditing(null);
      showToast('Custom field updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const openCreate = () => {
    setForm(emptyForm());
    setAutoSlug(true);
    setShowCreate(true);
  };

  const openEdit = (f: CustomFieldDefinitionDto) => {
    setEditing(f);
    setAutoSlug(false);
    setForm({
      label: f.label,
      fieldKey: f.fieldKey,
      fieldType: f.fieldType,
      isRequired: f.isRequired,
      isActive: f.isActive,
      optionsRaw: f.options?.join('\n') ?? '',
    });
  };

  const handleLabelChange = (label: string) => {
    setForm((prev) => ({
      ...prev,
      label,
      fieldKey: autoSlug ? slugify(label) : prev.fieldKey,
    }));
  };

  if (isLoading) return <LoadingBlock label="Loading custom fields…" />;
  if (error) return <ErrorBlock error={error} title="Could not load custom fields" onRetry={() => refetch()} />;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Custom Fields</h1>
          <p className="text-sm text-gray-500 mt-0.5">Additional fields shown on the deal form.</p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
        >
          New field
        </button>
      </div>

      {fields.length === 0 ? (
        <EmptyBlock title="No custom fields" message="Create custom fields to capture additional deal data." />
      ) : (
        <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Label</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Key</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Type</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Required</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th className="px-6 py-3" />
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {fields.map((f) => (
                <tr key={f.id} className="hover:bg-gray-50">
                  <td className="px-6 py-3 text-sm font-medium text-gray-900">{f.label}</td>
                  <td className="px-6 py-3 text-sm text-gray-500 font-mono">{f.fieldKey}</td>
                  <td className="px-6 py-3 text-sm text-gray-600">{CUSTOM_FIELD_TYPE_LABELS[f.fieldType] ?? f.fieldType}</td>
                  <td className="px-6 py-3 text-sm text-gray-600">{f.isRequired ? 'Yes' : 'No'}</td>
                  <td className="px-6 py-3">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${f.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-500'}`}>
                      {f.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-6 py-3 text-right">
                    <button
                      type="button"
                      onClick={() => openEdit(f)}
                      className="text-sm text-primary-600 hover:text-primary-800"
                    >
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Create modal */}
      <Modal isOpen={showCreate} title="New custom field" onClose={() => setShowCreate(false)}>
        <div className="space-y-4">
          <FormField
            label="Label"
            required
            value={form.label}
            onChange={(e) => handleLabelChange(e.target.value)}
            placeholder="e.g. Contract value"
          />
          <FormField
            label="Field key"
            required
            value={form.fieldKey}
            onChange={(e) => { setAutoSlug(false); setForm((f) => ({ ...f, fieldKey: e.target.value })); }}
            hint="snake_case identifier used in the API and exports"
            placeholder="e.g. contract_value"
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Type <span className="text-red-600">*</span></label>
            <select
              value={form.fieldType}
              onChange={(e) => setForm((f) => ({ ...f, fieldType: e.target.value as CustomFieldType }))}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {FIELD_TYPES.map((t) => (
                <option key={t} value={t}>{CUSTOM_FIELD_TYPE_LABELS[t] ?? t}</option>
              ))}
            </select>
          </div>
          {fieldTypeNeedsOptions(form.fieldType) && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Options <span className="text-red-600">*</span></label>
              <textarea
                value={form.optionsRaw}
                onChange={(e) => setForm((f) => ({ ...f, optionsRaw: e.target.value }))}
                rows={4}
                placeholder="One option per line"
                className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
          )}
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={form.isRequired}
              onChange={(e) => setForm((f) => ({ ...f, isRequired: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Required field
          </label>
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setShowCreate(false)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => createMutation.mutate()}
              disabled={!form.label || !form.fieldKey || createMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {createMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Create
            </button>
          </div>
        </div>
      </Modal>

      {/* Edit modal */}
      <Modal isOpen={!!editing} title={`Edit: ${editing?.label ?? ''}`} onClose={() => setEditing(null)}>
        <div className="space-y-4">
          <FormField
            label="Label"
            required
            value={form.label}
            onChange={(e) => setForm((f) => ({ ...f, label: e.target.value }))}
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Type</label>
            <p className="text-sm text-gray-500">{CUSTOM_FIELD_TYPE_LABELS[form.fieldType] ?? form.fieldType} (cannot change after creation)</p>
          </div>
          {fieldTypeNeedsOptions(form.fieldType) && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Options</label>
              <textarea
                value={form.optionsRaw}
                onChange={(e) => setForm((f) => ({ ...f, optionsRaw: e.target.value }))}
                rows={4}
                placeholder="One option per line"
                className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
              />
            </div>
          )}
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={form.isRequired}
              onChange={(e) => setForm((f) => ({ ...f, isRequired: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Required field
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={(e) => setForm((f) => ({ ...f, isActive: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Active
          </label>
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setEditing(null)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => updateMutation.mutate()}
              disabled={!form.label || updateMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {updateMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Save
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

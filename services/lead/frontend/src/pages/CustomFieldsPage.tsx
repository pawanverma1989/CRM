import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { getCustomFields, createCustomField, updateCustomField } from '../api/customFields';
import {
  customFieldDefinitionSchema,
  MAX_ACTIVE_CUSTOM_FIELDS,
  type CustomFieldDefinitionFormValues,
} from '../lib/validation';
import { CUSTOM_FIELD_TYPE_LABELS, fieldTypeNeedsOptions } from '../lib/customFields';
import { useToast } from '../contexts/ToastContext';
import { getApiErrorMessage } from '../lib/utils';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { TextAreaField } from '../components/TextAreaField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import type { CustomFieldDefinitionDto } from '../types';

const typeOptions = Object.entries(CUSTOM_FIELD_TYPE_LABELS).map(([value, label]) => ({ value, label }));

function optionsTextFrom(def?: CustomFieldDefinitionDto): string {
  return def?.options?.join('\n') ?? '';
}

export function CustomFieldsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<CustomFieldDefinitionDto | null>(null);

  const { data: fields = [], isLoading, isError, error, refetch } = useQuery({
    queryKey: ['custom-fields', 'all'],
    queryFn: () => getCustomFields(true),
  });

  const activeCount = fields.filter((f) => f.isActive).length;
  const atLimit = activeCount >= MAX_ACTIVE_CUSTOM_FIELDS;

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['custom-fields'] });

  const createForm = useForm<CustomFieldDefinitionFormValues>({
    resolver: zodResolver(customFieldDefinitionSchema),
    defaultValues: {
      label: '',
      fieldKey: '',
      fieldType: 'text',
      isRequired: false,
      sortOrder: '0',
      optionsText: '',
    },
  });

  const editForm = useForm<CustomFieldDefinitionFormValues>({
    resolver: zodResolver(customFieldDefinitionSchema),
  });

  const createMutation = useMutation({
    mutationFn: (values: CustomFieldDefinitionFormValues) =>
      createCustomField({
        entityType: 'lead',
        fieldKey: values.fieldKey.trim(),
        label: values.label.trim(),
        fieldType: values.fieldType,
        isRequired: values.isRequired,
        sortOrder: Number(values.sortOrder),
        options: fieldTypeNeedsOptions(values.fieldType)
          ? values.optionsText.split('\n').map((o) => o.trim()).filter(Boolean)
          : null,
      }),
    onSuccess: () => {
      invalidate();
      showToast('Custom field created', 'success');
      setCreateOpen(false);
      createForm.reset();
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: (values: CustomFieldDefinitionFormValues) => {
      if (!editing) throw new Error('No field selected');
      return updateCustomField(editing.id, {
        label: values.label.trim(),
        isRequired: values.isRequired,
        sortOrder: Number(values.sortOrder),
        options: fieldTypeNeedsOptions(values.fieldType)
          ? values.optionsText.split('\n').map((o) => o.trim()).filter(Boolean)
          : null,
      });
    },
    onSuccess: () => {
      invalidate();
      showToast('Custom field saved', 'success');
      setEditing(null);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleActiveMutation = useMutation({
    mutationFn: (field: CustomFieldDefinitionDto) =>
      updateCustomField(field.id, { isActive: !field.isActive }),
    onSuccess: (_, field) => {
      invalidate();
      showToast(field.isActive ? 'Field deactivated' : 'Field reactivated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const openEdit = (field: CustomFieldDefinitionDto) => {
    setEditing(field);
    editForm.reset({
      label: field.label,
      fieldKey: field.fieldKey,
      fieldType: field.fieldType,
      isRequired: field.isRequired,
      sortOrder: String(field.sortOrder),
      optionsText: optionsTextFrom(field),
    });
  };

  const watchedCreateType = createForm.watch('fieldType');
  const watchedEditType = editForm.watch('fieldType');

  return (
    <div className="max-w-4xl">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Custom fields</h1>
        <button
          type="button"
          disabled={atLimit}
          onClick={() => {
            createForm.reset({
              label: '',
              fieldKey: '',
              fieldType: 'text',
              isRequired: false,
              sortOrder: '0',
              optionsText: '',
            });
            setCreateOpen(true);
          }}
          title={atLimit ? `Limit of ${MAX_ACTIVE_CUSTOM_FIELDS} active fields reached` : undefined}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-50"
        >
          Add field
        </button>
      </div>

      <p className="text-sm text-gray-600 mb-4">
        {activeCount} of {MAX_ACTIVE_CUSTOM_FIELDS} active fields used. Deactivating a field hides it from forms and
        lists, but its stored values are kept and come back if it is reactivated.
      </p>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading custom fields…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load custom fields" onRetry={refetch} />
        ) : fields.length === 0 ? (
          <EmptyBlock title="No custom fields yet" message="Add the first custom field for leads." />
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Label</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Key</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Type</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Required</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Order</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {[...fields].sort((a, b) => a.sortOrder - b.sortOrder).map((field) => (
                <tr key={field.id} className={field.isActive ? '' : 'opacity-60'}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{field.label}</td>
                  <td className="px-4 py-3 text-sm text-gray-600 font-mono">{field.fieldKey}</td>
                  <td className="px-4 py-3 text-sm text-gray-600">{CUSTOM_FIELD_TYPE_LABELS[field.fieldType]}</td>
                  <td className="px-4 py-3 text-sm text-gray-600">{field.isRequired ? 'Yes' : 'No'}</td>
                  <td className="px-4 py-3 text-sm text-gray-600">{field.sortOrder}</td>
                  <td className="px-4 py-3 text-sm">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${field.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                      {field.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right text-sm">
                    <div className="flex justify-end gap-3">
                      <button onClick={() => openEdit(field)} className="text-primary-600 hover:text-primary-700 font-medium">
                        Edit
                      </button>
                      <button
                        onClick={() => toggleActiveMutation.mutate(field)}
                        disabled={!field.isActive && atLimit}
                        title={!field.isActive && atLimit ? `Limit of ${MAX_ACTIVE_CUSTOM_FIELDS} active fields reached` : undefined}
                        className="text-gray-600 hover:text-gray-800 font-medium disabled:opacity-50"
                      >
                        {field.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Modal isOpen={createOpen} title="Add custom field" onClose={() => setCreateOpen(false)}>
        <form onSubmit={createForm.handleSubmit((v) => createMutation.mutate(v))} className="space-y-4" noValidate>
          <FormField label="Label" error={createForm.formState.errors.label?.message} {...createForm.register('label')} />
          <FormField
            label="Key"
            hint="Lower-case letters, digits and underscores; cannot change later"
            error={createForm.formState.errors.fieldKey?.message}
            {...createForm.register('fieldKey')}
          />
          <SelectField
            label="Type"
            hint="Cannot change later"
            options={typeOptions}
            error={createForm.formState.errors.fieldType?.message}
            {...createForm.register('fieldType')}
          />
          {fieldTypeNeedsOptions(watchedCreateType) && (
            <TextAreaField
              label="Options (one per line)"
              error={createForm.formState.errors.optionsText?.message}
              {...createForm.register('optionsText')}
            />
          )}
          <FormField
            label="Display order"
            inputMode="numeric"
            error={createForm.formState.errors.sortOrder?.message}
            {...createForm.register('sortOrder')}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500" {...createForm.register('isRequired')} />
            Required (applies to new saves only)
          </label>
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
              Create field
            </button>
          </div>
        </form>
      </Modal>

      <Modal isOpen={!!editing} title="Edit custom field" onClose={() => setEditing(null)}>
        <form onSubmit={editForm.handleSubmit((v) => updateMutation.mutate(v))} className="space-y-4" noValidate>
          <FormField label="Label" error={editForm.formState.errors.label?.message} {...editForm.register('label')} />
          <FormField label="Key" disabled hint="Cannot change after creation" {...editForm.register('fieldKey')} />
          <SelectField label="Type" disabled hint="Cannot change after creation" options={typeOptions} {...editForm.register('fieldType')} />
          {fieldTypeNeedsOptions(watchedEditType) && (
            <TextAreaField
              label="Options (one per line)"
              error={editForm.formState.errors.optionsText?.message}
              {...editForm.register('optionsText')}
            />
          )}
          <FormField
            label="Display order"
            inputMode="numeric"
            error={editForm.formState.errors.sortOrder?.message}
            {...editForm.register('sortOrder')}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700">
            <input type="checkbox" className="h-4 w-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500" {...editForm.register('isRequired')} />
            Required (applies to new saves only)
          </label>
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

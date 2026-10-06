import { useEffect, useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getDeal, createDeal, updateDeal } from '../api/deals';
import { listPipelines } from '../api/pipelines';
import { useOwners } from '../hooks/useOwners';
import { OwnerSelectHint } from '../components/OwnerSelectHint';
import { listCustomFields } from '../api/customFields';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { TagInput } from '../components/TagInput';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { dealFormSchema, type DealFormValues } from '../lib/validation';
import { initialCustomFieldValues, validateCustomFields } from '../lib/customFields';
import { getApiErrorMessage } from '../lib/utils';
import type { CustomFieldValues } from '../types';

export function DealFormPage() {
  const { id } = useParams<{ id?: string }>();
  const isEdit = !!id;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [customFields, setCustomFields] = useState<CustomFieldValues>({});
  const [customFieldErrors, setCustomFieldErrors] = useState<Record<string, string>>({});
  const [tags, setTags] = useState<string[]>([]);

  const { data: deal, isLoading: dealLoading, error: dealError } = useQuery({
    queryKey: ['deal', id],
    queryFn: () => getDeal(id!),
    enabled: isEdit,
  });

  const { data: pipelines = [], isLoading: pipelinesLoading } = useQuery({
    queryKey: ['pipelines'],
    queryFn: listPipelines,
    staleTime: 60_000,
  });

  const { owners, isLoading: ownersLoading, isFetching: ownersFetching, refetch: refetchOwners } = useOwners({ fresh: true });
  const { data: cfDefs = [] } = useQuery({ queryKey: ['custom-fields'], queryFn: () => listCustomFields('deal'), staleTime: 60_000 });

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<DealFormValues>({
    resolver: zodResolver(dealFormSchema),
    defaultValues: {
      name: '',
      pipelineId: '',
      stageId: '',
      amount: '0',
      currency: 'INR',
      ownerId: '',
      companyId: '',
      primaryContactId: '',
      probability: '',
      expectedCloseDate: '',
    },
  });

  const selectedPipelineId = watch('pipelineId');
  const selectedPipeline = pipelines.find((p) => p.id === selectedPipelineId);
  const activeStages = selectedPipeline?.stages.filter((s) => s.isActive) ?? [];

  useEffect(() => {
    if (deal) {
      reset({
        name: deal.name,
        pipelineId: deal.pipelineId,
        stageId: deal.stageId,
        amount: String(deal.amount),
        currency: deal.currency,
        ownerId: deal.ownerId ?? '',
        companyId: deal.companyId ?? '',
        primaryContactId: deal.primaryContactId ?? '',
        probability: deal.probability != null ? String(deal.probability) : '',
        expectedCloseDate: deal.expectedCloseDate ?? '',
      });
      setTags(deal.tags ?? []);
      setCustomFields(initialCustomFieldValues(cfDefs, deal.customFields));
    } else if (!isEdit && cfDefs.length > 0) {
      setCustomFields(initialCustomFieldValues(cfDefs, undefined));
    }
  }, [deal, cfDefs, isEdit, reset]);

  // Reset stageId when pipeline changes
  useEffect(() => {
    if (!isEdit) setValue('stageId', '');
  }, [selectedPipelineId, isEdit, setValue]);

  const createMutation = useMutation({
    mutationFn: (values: DealFormValues) =>
      createDeal({
        pipelineId: values.pipelineId,
        stageId: values.stageId,
        name: values.name,
        amount: values.amount ? Number(values.amount) : 0,
        currency: values.currency || 'INR',
        ownerId: values.ownerId || null,
        companyId: values.companyId || null,
        primaryContactId: values.primaryContactId || null,
        probability: values.probability ? Number(values.probability) : null,
        expectedCloseDate: values.expectedCloseDate || null,
        tags,
        customFields: customFields as Record<string, unknown>,
      }),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      showToast('Deal created', 'success');
      navigate(`/deals/${created.id}`);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: (values: DealFormValues) =>
      updateDeal(id!, {
        version: deal!.version,
        name: values.name,
        amount: values.amount ? Number(values.amount) : 0,
        currency: values.currency || 'INR',
        ownerId: values.ownerId || null,
        companyId: values.companyId || null,
        primaryContactId: values.primaryContactId || null,
        probability: values.probability ? Number(values.probability) : null,
        expectedCloseDate: values.expectedCloseDate || null,
        tags,
        customFields: customFields as Record<string, unknown>,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['deal', id] });
      queryClient.invalidateQueries({ queryKey: ['deals'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      showToast('Deal updated', 'success');
      navigate(`/deals/${id}`);
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const onSubmit = (values: DealFormValues) => {
    const cfErrors = validateCustomFields(cfDefs, customFields);
    if (Object.keys(cfErrors).length > 0) {
      setCustomFieldErrors(cfErrors);
      return;
    }
    setCustomFieldErrors({});
    if (isEdit) updateMutation.mutate(values);
    else createMutation.mutate(values);
  };

  if (isEdit && dealLoading) return <LoadingBlock label="Loading deal…" />;
  if (isEdit && dealError) return <ErrorBlock error={dealError} title="Could not load deal" />;
  if (pipelinesLoading) return <LoadingBlock label="Loading pipelines…" />;

  const isPending = isSubmitting || createMutation.isPending || updateMutation.isPending;

  return (
    <div className="max-w-2xl mx-auto">
      <nav className="text-sm text-gray-500 mb-4" aria-label="Breadcrumb">
        <Link to="/deals" className="hover:text-primary-600">Deals</Link>
        {isEdit && deal && (
          <>
            <span className="mx-2">/</span>
            <Link to={`/deals/${id}`} className="hover:text-primary-600">{deal.name}</Link>
          </>
        )}
        <span className="mx-2">/</span>
        <span className="text-gray-900">{isEdit ? 'Edit' : 'New deal'}</span>
      </nav>

      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h1 className="text-xl font-bold text-gray-900 mb-6">{isEdit ? 'Edit deal' : 'New deal'}</h1>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-5">
          <FormField
            label="Deal name"
            required
            error={errors.name?.message}
            {...register('name')}
          />

          <div className="grid grid-cols-2 gap-4">
            <SelectField
              label="Pipeline"
              required
              placeholder="Select a pipeline…"
              options={pipelines.map((p) => ({ value: p.id, label: p.name }))}
              error={errors.pipelineId?.message}
              disabled={isEdit}
              {...register('pipelineId')}
            />
            <SelectField
              label="Stage"
              required
              placeholder={selectedPipelineId ? 'Select a stage…' : 'Choose a pipeline first'}
              options={activeStages.map((s) => ({ value: s.id, label: s.name }))}
              error={errors.stageId?.message}
              disabled={isEdit || !selectedPipelineId}
              {...register('stageId')}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <FormField
              label="Amount"
              type="number"
              min="0"
              step="0.01"
              placeholder="0.00"
              error={errors.amount?.message}
              {...register('amount')}
            />
            <FormField
              label="Currency"
              placeholder="INR"
              maxLength={3}
              error={errors.currency?.message}
              {...register('currency')}
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <SelectField
                label="Owner"
                placeholder="Unassigned"
                options={owners.filter((o) => o.isActive).map((o) => ({ value: o.id, label: o.displayName }))}
                error={errors.ownerId?.message}
                {...register('ownerId')}
              />
              <OwnerSelectHint
                ownerCount={owners.filter((o) => o.isActive).length}
                isLoading={ownersLoading}
                isFetching={ownersFetching}
                onRefresh={() => void refetchOwners()}
              />
            </div>
            <FormField
              label="Probability (%)"
              type="number"
              min="0"
              max="100"
              step="1"
              placeholder="0–100"
              error={errors.probability?.message}
              {...register('probability')}
            />
          </div>

          <FormField
            label="Expected close date"
            type="date"
            error={errors.expectedCloseDate?.message}
            {...register('expectedCloseDate')}
          />

          <TagInput value={tags} onChange={setTags} />

          {cfDefs.filter((d) => d.isActive).map((def) => (
            <div key={def.fieldKey}>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                {def.label}{def.isRequired && <span className="text-red-600 ml-0.5" aria-hidden="true">*</span>}
              </label>
              {def.fieldType === 'boolean' ? (
                <input
                  type="checkbox"
                  checked={Boolean(customFields[def.fieldKey])}
                  onChange={(e) => setCustomFields((prev) => ({ ...prev, [def.fieldKey]: e.target.checked }))}
                  className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                />
              ) : def.fieldType === 'select' ? (
                <select
                  value={String(customFields[def.fieldKey] ?? '')}
                  onChange={(e) => setCustomFields((prev) => ({ ...prev, [def.fieldKey]: e.target.value }))}
                  className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
                >
                  <option value="">Select…</option>
                  {def.options?.map((o) => <option key={o} value={o}>{o}</option>)}
                </select>
              ) : def.fieldType === 'textarea' ? (
                <textarea
                  value={String(customFields[def.fieldKey] ?? '')}
                  onChange={(e) => setCustomFields((prev) => ({ ...prev, [def.fieldKey]: e.target.value }))}
                  rows={3}
                  className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
                />
              ) : (
                <input
                  type={def.fieldType === 'date' ? 'date' : def.fieldType === 'datetime' ? 'datetime-local' : def.fieldType === 'number' || def.fieldType === 'currency' ? 'number' : 'text'}
                  value={String(customFields[def.fieldKey] ?? '')}
                  onChange={(e) => setCustomFields((prev) => ({ ...prev, [def.fieldKey]: e.target.value }))}
                  className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
                />
              )}
              {customFieldErrors[def.fieldKey] && (
                <p className="mt-1 text-xs text-red-600">{customFieldErrors[def.fieldKey]}</p>
              )}
            </div>
          ))}

          <div className="flex justify-end gap-3 pt-2">
            <button
              type="button"
              onClick={() => navigate(isEdit ? `/deals/${id}` : '/deals')}
              className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              {isEdit ? 'Save changes' : 'Create deal'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

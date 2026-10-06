import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { getLead, createLead, updateLead } from '../api/leads';
import { getLeadSources } from '../api/leadSources';
import { useOwners } from '../hooks/useOwners';
import { OwnerSelectHint } from '../components/OwnerSelectHint';
import { getCustomFields } from '../api/customFields';
import { checkDuplicates } from '../api/duplicates';
import { leadFormSchema, type LeadFormValues } from '../lib/validation';
import { validateCustomFields, initialCustomFieldValues } from '../lib/customFields';
import { getApiErrorMessage, isVersionConflict, getFieldErrors, leadName } from '../lib/utils';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { TextAreaField } from '../components/TextAreaField';
import { TagInput } from '../components/TagInput';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import type { CustomFieldValues, DuplicateLeadDto } from '../types';

export function LeadFormPage() {
  const { id } = useParams<{ id: string }>();
  const isEditing = Boolean(id);
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canChooseOwner } = usePermissions();

  const [tags, setTags] = useState<string[]>([]);
  const [customFieldValues, setCustomFieldValues] = useState<CustomFieldValues>({});
  const [customFieldErrors, setCustomFieldErrors] = useState<Record<string, string>>({});
  const [duplicateMatches, setDuplicateMatches] = useState<DuplicateLeadDto[]>([]);
  const [duplicateDismissed, setDuplicateDismissed] = useState(false);
  const [versionConflict, setVersionConflict] = useState(false);
  const [initialized, setInitialized] = useState(!isEditing);

  const {
    data: lead,
    isLoading: isLoadingLead,
    isError: isLeadError,
    error: leadError,
    refetch: refetchLead,
  } = useQuery({
    queryKey: ['lead', id],
    queryFn: () => getLead(id!),
    enabled: isEditing,
  });

  const { owners, isLoading: ownersLoading, isFetching: ownersFetching, refetch: refetchOwners } = useOwners({ fresh: true });
  const { data: leadSources = [] } = useQuery({ queryKey: ['lead-sources'], queryFn: getLeadSources });
  const { data: customFieldDefs = [], isSuccess: customFieldsLoaded } = useQuery({
    queryKey: ['custom-fields'],
    queryFn: () => getCustomFields(),
  });

  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<LeadFormValues>({
    resolver: zodResolver(leadFormSchema),
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      phone: '',
      companyName: '',
      jobTitle: '',
      leadSourceId: '',
      ownerId: '',
      utmSource: '',
      utmMedium: '',
      utmCampaign: '',
      notes: '',
    },
  });

  // Seed the form once the record and custom field definitions are both loaded.
  if (isEditing && lead && customFieldsLoaded && !initialized) {
    reset({
      firstName: lead.firstName ?? '',
      lastName: lead.lastName ?? '',
      email: lead.email ?? '',
      phone: lead.phone ?? '',
      companyName: lead.companyName ?? '',
      jobTitle: lead.jobTitle ?? '',
      leadSourceId: lead.leadSourceId ?? '',
      ownerId: lead.ownerId ?? '',
      utmSource: lead.utmSource ?? '',
      utmMedium: lead.utmMedium ?? '',
      utmCampaign: lead.utmCampaign ?? '',
      notes: lead.notes ?? '',
    });
    setTags(lead.tags);
    setCustomFieldValues(initialCustomFieldValues(customFieldDefs, lead.customFields));
    setInitialized(true);
  }

  // Duplicate check — debounced on email/phone changes
  const watchedEmail = watch('email');
  const watchedPhone = watch('phone');
  const debouncedEmail = useDebouncedValue(watchedEmail, 500);
  const debouncedPhone = useDebouncedValue(watchedPhone, 500);
  const prevCheckRef = useRef({ email: '', phone: '' });

  useEffect(() => {
    const email = debouncedEmail.trim();
    const phone = debouncedPhone.trim();
    if (email === prevCheckRef.current.email && phone === prevCheckRef.current.phone) return;
    prevCheckRef.current = { email, phone };
    if (!email && !phone) {
      setDuplicateMatches([]);
      return;
    }
    setDuplicateDismissed(false);
    checkDuplicates({
      excludeId: lead?.id,
      email: email || undefined,
      phone: phone || undefined,
    })
      .then((result) => {
        if (result.hasDuplicates) setDuplicateMatches(result.matches);
        else setDuplicateMatches([]);
      })
      .catch(() => setDuplicateMatches([]));
  }, [debouncedEmail, debouncedPhone, lead?.id]);

  const saveMutation = useMutation({
    mutationFn: async (values: LeadFormValues) => {
      const payload: Record<string, unknown> = {
        firstName: values.firstName.trim() || null,
        lastName: values.lastName.trim() || null,
        email: values.email.trim() || null,
        phone: values.phone.trim() || null,
        companyName: values.companyName.trim() || null,
        jobTitle: values.jobTitle.trim() || null,
        leadSourceId: values.leadSourceId || null,
        utmSource: values.utmSource.trim() || null,
        utmMedium: values.utmMedium.trim() || null,
        utmCampaign: values.utmCampaign.trim() || null,
        notes: values.notes.trim() || null,
        tags,
        customFields: customFieldValues,
      };
      if (canChooseOwner) {
        payload.ownerId = values.ownerId || null;
      }
      if (isEditing && lead) {
        return updateLead(lead.id, { ...payload, version: lead.version });
      }
      return createLead(payload);
    },
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['leads'] });
      queryClient.invalidateQueries({ queryKey: ['lead', saved.id] });
      showToast(isEditing ? 'Lead saved' : 'Lead created', 'success');
      navigate(`/${saved.id}`);
    },
    onError: (err) => {
      if (isVersionConflict(err)) {
        setVersionConflict(true);
        return;
      }
      showToast(getApiErrorMessage(err), 'error');
    },
  });

  const onValidSubmit = async (values: LeadFormValues) => {
    const cfErrors = validateCustomFields(customFieldDefs, customFieldValues);
    setCustomFieldErrors(cfErrors);
    if (Object.keys(cfErrors).length > 0) return;

    try {
      await saveMutation.mutateAsync(values);
    } catch {
      // Already surfaced to the user via the mutation's onError above.
    }
  };

  const onReload = async () => {
    setVersionConflict(false);
    setInitialized(false);
    await refetchLead();
  };

  if (isEditing && isLoadingLead) return <LoadingBlock label="Loading lead…" />;
  if (isEditing && isLeadError) {
    return <ErrorBlock error={leadError} title="Could not load this lead" onRetry={refetchLead} />;
  }

  const isConverted = lead?.status === 'converted';
  const serverFieldErrors = getFieldErrors(saveMutation.error);
  const fieldError = (name: string, zodMessage?: string) => zodMessage ?? serverFieldErrors[name];

  return (
    <div className="max-w-3xl">
      <Link
        to={isEditing && lead ? `/leads/${lead.id}` : '/leads'}
        className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1"
      >
        ← Back
      </Link>
      <h1 className="text-2xl font-bold text-gray-900 mt-2 mb-6">
        {isEditing ? 'Edit lead' : 'Add lead'}
      </h1>

      {isConverted && (
        <div className="mb-6 rounded-lg border border-purple-200 bg-purple-50 px-4 py-3 text-sm text-purple-800">
          This lead has been converted and is read-only.
        </div>
      )}

      {versionConflict && (
        <div className="mb-6 rounded-lg border border-yellow-200 bg-yellow-50 px-4 py-3 flex items-center justify-between gap-4">
          <p className="text-sm text-yellow-800">
            This lead was updated by someone else while you were editing. Reload to see the latest version.
          </p>
          <button
            type="button"
            onClick={onReload}
            className="px-3 py-1.5 text-sm font-medium text-yellow-900 border border-yellow-400 rounded-md hover:bg-yellow-100 shrink-0"
          >
            Reload
          </button>
        </div>
      )}

      {duplicateMatches.length > 0 && !duplicateDismissed && (
        <div className="mb-6 rounded-lg border border-orange-200 bg-orange-50 px-4 py-3">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-medium text-orange-800 mb-1">Possible duplicate leads</p>
              <ul className="text-sm text-orange-700 list-disc list-inside space-y-0.5">
                {duplicateMatches.map((m) => (
                  <li key={m.id}>
                    <Link to={`/leads/${m.id}`} className="underline hover:text-orange-900" target="_blank" rel="noopener noreferrer">
                      {leadName(m)}
                    </Link>{' '}
                    ({m.status})
                  </li>
                ))}
              </ul>
            </div>
            <button
              type="button"
              onClick={() => setDuplicateDismissed(true)}
              className="text-orange-600 hover:text-orange-900 text-sm font-medium shrink-0"
            >
              Dismiss
            </button>
          </div>
        </div>
      )}

      <form onSubmit={handleSubmit(onValidSubmit)} className="space-y-6" noValidate>
        <fieldset disabled={isConverted} className="contents">
          <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
            <h2 className="text-sm font-semibold text-gray-900">Basic details</h2>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <FormField
                label="First name"
                error={fieldError('firstName', errors.firstName?.message)}
                {...register('firstName')}
              />
              <FormField
                label="Last name"
                error={fieldError('lastName', errors.lastName?.message)}
                {...register('lastName')}
              />
              <FormField
                label="Email"
                type="email"
                hint="A lead needs at least an email or a phone"
                error={fieldError('email', errors.email?.message)}
                {...register('email')}
              />
              <FormField
                label="Phone"
                type="tel"
                hint="A lead needs at least an email or a phone"
                error={fieldError('phone', errors.phone?.message)}
                {...register('phone')}
              />
              <FormField
                label="Company"
                error={fieldError('companyName', errors.companyName?.message)}
                {...register('companyName')}
              />
              <FormField
                label="Job title"
                error={fieldError('jobTitle', errors.jobTitle?.message)}
                {...register('jobTitle')}
              />
              <SelectField
                label="Lead source"
                placeholder="Not set"
                options={leadSources.filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name }))}
                error={fieldError('leadSourceId')}
                {...register('leadSourceId')}
              />
              {canChooseOwner && (
                <div>
                  <SelectField
                    label="Owner"
                    placeholder="No owner (visible to everyone)"
                    options={owners.filter((o) => o.isActive).map((o) => ({ value: o.id, label: o.displayName }))}
                    error={fieldError('ownerId')}
                    {...register('ownerId')}
                  />
                  <OwnerSelectHint
                    ownerCount={owners.filter((o) => o.isActive).length}
                    isLoading={ownersLoading}
                    isFetching={ownersFetching}
                    onRefresh={() => void refetchOwners()}
                  />
                </div>
              )}
            </div>
          </section>

          <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
            <h2 className="text-sm font-semibold text-gray-900">UTM parameters</h2>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <FormField
                label="UTM source"
                error={fieldError('utmSource', errors.utmSource?.message)}
                {...register('utmSource')}
              />
              <FormField
                label="UTM medium"
                error={fieldError('utmMedium', errors.utmMedium?.message)}
                {...register('utmMedium')}
              />
              <FormField
                label="UTM campaign"
                error={fieldError('utmCampaign', errors.utmCampaign?.message)}
                {...register('utmCampaign')}
              />
            </div>
          </section>

          <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
            <h2 className="text-sm font-semibold text-gray-900">Notes</h2>
            <TextAreaField
              label="Notes"
              rows={4}
              error={fieldError('notes', errors.notes?.message)}
              {...register('notes')}
            />
          </section>

          <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
            <h2 className="text-sm font-semibold text-gray-900">Tags</h2>
            <TagInput value={tags} onChange={setTags} disabled={isConverted} />
          </section>

          {customFieldDefs.length > 0 && (
            <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
              <h2 className="text-sm font-semibold text-gray-900">Custom fields</h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {customFieldDefs.filter((d) => d.isActive).map((def) => (
                  <div key={def.id}>
                    <label className="block text-sm font-medium text-gray-700 mb-1">
                      {def.label}
                      {def.isRequired && <span className="text-red-600 ml-0.5" aria-hidden="true">*</span>}
                    </label>
                    <input
                      type="text"
                      value={String(customFieldValues[def.fieldKey] ?? '')}
                      onChange={(e) => setCustomFieldValues((prev) => ({ ...prev, [def.fieldKey]: e.target.value }))}
                      className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
                    />
                    {customFieldErrors[def.fieldKey] && (
                      <p className="mt-1 text-xs text-red-600">{customFieldErrors[def.fieldKey]}</p>
                    )}
                  </div>
                ))}
              </div>
            </section>
          )}
        </fieldset>

        {!isConverted && (
          <div className="flex justify-end gap-3">
            <button
              type="button"
              onClick={() => navigate(isEditing && lead ? `/${lead.id}` : '/')}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={isSubmitting || saveMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {saveMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Save lead
            </button>
          </div>
        )}
      </form>
    </div>
  );
}

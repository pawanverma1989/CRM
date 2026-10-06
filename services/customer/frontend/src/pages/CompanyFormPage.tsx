import { useState } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { getCompany, createCompany, updateCompany, type CompanyWritePayload } from '../api/companies';
import { checkDuplicates } from '../api/duplicates';
import { getCustomFields } from '../api/customFields';
import { getPicklist } from '../api/picklists';
import { useOwners } from '../hooks/useOwners';
import { OwnerSelectHint } from '../components/OwnerSelectHint';
import { emptyToNull, emptyToIntOrNull, emptyToDecimalStringOrNull, pruneUndefined, cleanCustomFields } from '../api/payload';
import { companyFormSchema, normalizeDomain, type CompanyFormValues } from '../lib/validation';
import { validateCustomFields, initialCustomFieldValues } from '../lib/customFields';
import { getApiErrorMessage, isVersionConflict, getConflictRef, getFieldErrors } from '../lib/utils';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { TagInput } from '../components/TagInput';
import { CustomFieldInput } from '../components/CustomFieldInput';
import { DuplicateWarning, ConflictBanner, VersionConflictBanner } from '../components/DuplicateWarning';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import type { ConflictRef, CustomFieldValues, DuplicateMatchDto } from '../types';

export function CompanyFormPage() {
  const { id } = useParams<{ id: string }>();
  const isEditing = Boolean(id);
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canChooseOwner } = usePermissions();

  const [tags, setTags] = useState<string[]>([]);
  const [customFieldValues, setCustomFieldValues] = useState<CustomFieldValues>({});
  const [customFieldErrors, setCustomFieldErrors] = useState<Record<string, string>>({});
  const [duplicateMatches, setDuplicateMatches] = useState<DuplicateMatchDto[]>([]);
  const [pendingValues, setPendingValues] = useState<CompanyFormValues | null>(null);
  const [versionConflict, setVersionConflict] = useState(false);
  const [conflict, setConflict] = useState<{ message: string; ref?: ConflictRef } | null>(null);
  const [initialized, setInitialized] = useState(!isEditing);

  const {
    data: company,
    isLoading: isLoadingCompany,
    isError: isCompanyError,
    error: companyError,
    refetch: refetchCompany,
  } = useQuery({
    queryKey: ['company', id],
    queryFn: () => getCompany(id!),
    enabled: isEditing,
  });

  const { owners, isLoading: ownersLoading, isFetching: ownersFetching, refetch: refetchOwners } = useOwners({ fresh: true });
  const { data: customFieldDefs = [], isSuccess: customFieldsLoaded } = useQuery({
    queryKey: ['custom-fields', 'company'],
    queryFn: () => getCustomFields('company'),
  });
  const { data: industries = [] } = useQuery({
    queryKey: ['picklist', 'company_industry'],
    queryFn: () => getPicklist('company_industry'),
  });

  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<CompanyFormValues>({
    resolver: zodResolver(companyFormSchema),
    defaultValues: {
      name: '',
      domain: '',
      industryId: '',
      employeeCount: '',
      annualRevenue: '',
      currency: 'INR',
      phone: '',
      website: '',
      gstin: '',
      ownerId: '',
      addressLine1: '',
      addressLine2: '',
      city: '',
      state: '',
      postalCode: '',
      country: 'IN',
    },
  });

  const domainDraft = watch('domain');
  const normalizedDomainPreview = domainDraft?.trim() ? normalizeDomain(domainDraft) : '';

  if (isEditing && company && customFieldsLoaded && !initialized) {
    reset({
      name: company.name,
      domain: company.domain ?? '',
      industryId: company.industryId ?? '',
      employeeCount: company.employeeCount != null ? String(company.employeeCount) : '',
      annualRevenue: company.annualRevenue ?? '',
      currency: company.currency ?? 'INR',
      phone: company.phone ?? '',
      website: company.website ?? '',
      gstin: company.gstin ?? '',
      ownerId: company.ownerId ?? '',
      addressLine1: company.addressLine1 ?? '',
      addressLine2: company.addressLine2 ?? '',
      city: company.city ?? '',
      state: company.state ?? '',
      postalCode: company.postalCode ?? '',
      country: company.country ?? 'IN',
    });
    setTags(company.tags);
    setCustomFieldValues(initialCustomFieldValues(customFieldDefs, company.customFields));
    setInitialized(true);
  }

  const duplicateCheckMutation = useMutation({ mutationFn: checkDuplicates });

  const saveMutation = useMutation({
    mutationFn: async (values: CompanyFormValues) => {
      const basePayload = {
        name: values.name.trim(),
        domain: emptyToNull(values.domain) ? normalizeDomain(values.domain) : null,
        industryId: emptyToNull(values.industryId),
        employeeCount: emptyToIntOrNull(values.employeeCount),
        annualRevenue: emptyToDecimalStringOrNull(values.annualRevenue),
        currency: emptyToNull(values.currency),
        phone: emptyToNull(values.phone),
        website: emptyToNull(values.website),
        gstin: emptyToNull(values.gstin),
        ownerId: canChooseOwner ? emptyToNull(values.ownerId) : undefined,
        addressLine1: emptyToNull(values.addressLine1),
        addressLine2: emptyToNull(values.addressLine2),
        city: emptyToNull(values.city),
        state: emptyToNull(values.state),
        postalCode: emptyToNull(values.postalCode),
        country: emptyToNull(values.country),
        tags,
        customFields: cleanCustomFields(customFieldValues),
      };
      if (isEditing && company) {
        return updateCompany(company.id, { ...pruneUndefined(basePayload), version: company.version });
      }
      return createCompany({ ...basePayload, ownerId: basePayload.ownerId ?? null } as CompanyWritePayload);
    },
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['companies'] });
      queryClient.invalidateQueries({ queryKey: ['company', saved.id] });
      showToast(isEditing ? 'Company saved' : 'Company created', 'success');
      navigate(`/companies/${saved.id}`);
    },
    onError: (err) => {
      if (isVersionConflict(err)) {
        setVersionConflict(true);
        return;
      }
      const ref = getConflictRef(err);
      if (ref) {
        setConflict({ message: getApiErrorMessage(err), ref });
        return;
      }
      showToast(getApiErrorMessage(err), 'error');
    },
  });

  const doSave = async (values: CompanyFormValues) => {
    setConflict(null);
    setVersionConflict(false);
    try {
      await saveMutation.mutateAsync(values);
    } catch {
      // Already surfaced to the user via the mutation's onError above.
    }
  };

  const onValidSubmit = async (values: CompanyFormValues) => {
    const cfErrors = validateCustomFields(customFieldDefs, customFieldValues);
    setCustomFieldErrors(cfErrors);
    if (Object.keys(cfErrors).length > 0) return;

    try {
      const result = await duplicateCheckMutation.mutateAsync({
        entityType: 'company',
        excludeId: company?.id,
        name: values.name,
        domain: emptyToNull(values.domain) ? normalizeDomain(values.domain) : undefined,
      });
      if (result.matches.length > 0) {
        setDuplicateMatches(result.matches);
        setPendingValues(values);
        return;
      }
    } catch {
      // Courtesy check only; the server's own DUP-1 rule is authoritative.
    }
    await doSave(values);
  };

  const onSaveAnyway = async () => {
    if (!pendingValues) return;
    setDuplicateMatches([]);
    await doSave(pendingValues);
  };

  const onDismissDuplicates = () => {
    setDuplicateMatches([]);
    setPendingValues(null);
  };

  const onReload = async () => {
    setVersionConflict(false);
    setInitialized(false);
    await refetchCompany();
  };

  if (isEditing && isLoadingCompany) return <LoadingBlock label="Loading company…" />;
  if (isEditing && isCompanyError) {
    return <ErrorBlock error={companyError} title="Could not load this company" onRetry={refetchCompany} />;
  }

  const serverFieldErrors = conflict ? {} : getFieldErrors(saveMutation.error);
  const fieldError = (name: string, zodMessage?: string) => zodMessage ?? serverFieldErrors[name];

  return (
    <div className="max-w-3xl">
      <Link to={isEditing && company ? `/companies/${company.id}` : '/companies'} className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back
      </Link>
      <h1 className="text-2xl font-bold text-gray-900 mt-2 mb-6">{isEditing ? 'Edit company' : 'Add company'}</h1>

      {versionConflict && (
        <div className="mb-6">
          <VersionConflictBanner onReload={onReload} />
        </div>
      )}
      {conflict && (
        <div className="mb-6">
          <ConflictBanner message={conflict.message} conflict={conflict.ref} />
        </div>
      )}
      {duplicateMatches.length > 0 && (
        <div className="mb-6">
          <DuplicateWarning
            matches={duplicateMatches}
            onDismiss={onDismissDuplicates}
            onSaveAnyway={onSaveAnyway}
            isSaving={saveMutation.isPending}
          />
        </div>
      )}

      <form onSubmit={handleSubmit(onValidSubmit)} className="space-y-6" noValidate>
        <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
          <h2 className="text-sm font-semibold text-gray-900">Basic details</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Name" required error={fieldError('name', errors.name?.message)} {...register('name')} />
            <FormField
              label="Domain"
              hint={
                normalizedDomainPreview && normalizedDomainPreview !== domainDraft?.trim().toLowerCase()
                  ? `Will be stored as "${normalizedDomainPreview}"`
                  : 'Stored in lower case without http(s):// or www.'
              }
              error={fieldError('domain', errors.domain?.message)}
              {...register('domain')}
            />
            <SelectField
              label="Industry"
              placeholder="Not set"
              options={industries.filter((i) => i.isActive).map((i) => ({ value: i.id, label: i.value }))}
              error={fieldError('industryId')}
              {...register('industryId')}
            />
            <FormField
              label="Employee count"
              inputMode="numeric"
              error={fieldError('employeeCount', errors.employeeCount?.message)}
              {...register('employeeCount')}
            />
            <FormField
              label="Annual revenue"
              inputMode="decimal"
              hint="INR, 2 decimals"
              error={fieldError('annualRevenue', errors.annualRevenue?.message)}
              {...register('annualRevenue')}
            />
            <FormField
              label="Currency"
              hint="Three-letter code, e.g. INR"
              error={fieldError('currency', errors.currency?.message)}
              {...register('currency')}
            />
            <FormField label="Phone" type="tel" error={fieldError('phone', errors.phone?.message)} {...register('phone')} />
            <FormField
              label="Website"
              hint="Must start with http:// or https://"
              error={fieldError('website', errors.website?.message)}
              {...register('website')}
            />
            <FormField
              label="GSTIN"
              hint="15 characters, e.g. 29ABCDE1234F1Z5"
              error={fieldError('gstin', errors.gstin?.message)}
              {...register('gstin')}
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
          <h2 className="text-sm font-semibold text-gray-900">Address</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <FormField label="Address line 1" error={fieldError('addressLine1', errors.addressLine1?.message)} {...register('addressLine1')} />
            <FormField label="Address line 2" error={fieldError('addressLine2', errors.addressLine2?.message)} {...register('addressLine2')} />
            <FormField label="City" error={fieldError('city', errors.city?.message)} {...register('city')} />
            <FormField label="State" error={fieldError('state', errors.state?.message)} {...register('state')} />
            <FormField label="Postal code" hint="6 digits when country is India" error={fieldError('postalCode', errors.postalCode?.message)} {...register('postalCode')} />
            <FormField label="Country" hint="Two-letter ISO code, e.g. IN" error={fieldError('country', errors.country?.message)} {...register('country')} />
          </div>
        </section>

        <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
          <h2 className="text-sm font-semibold text-gray-900">Tags</h2>
          <TagInput value={tags} onChange={setTags} />
        </section>

        {customFieldDefs.length > 0 && (
          <section className="bg-white rounded-lg border border-gray-200 p-6 space-y-4">
            <h2 className="text-sm font-semibold text-gray-900">Custom fields</h2>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              {customFieldDefs.map((def) => (
                <CustomFieldInput
                  key={def.id}
                  definition={def}
                  owners={owners}
                  value={customFieldValues[def.fieldKey] ?? null}
                  error={customFieldErrors[def.fieldKey]}
                  onChange={(value) =>
                    setCustomFieldValues((prev) => ({ ...prev, [def.fieldKey]: value }))
                  }
                />
              ))}
            </div>
          </section>
        )}

        <div className="flex justify-end gap-3">
          <button
            type="button"
            onClick={() => navigate(isEditing && company ? `/companies/${company.id}` : '/companies')}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={isSubmitting || saveMutation.isPending || duplicateCheckMutation.isPending}
            className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
          >
            {(saveMutation.isPending || duplicateCheckMutation.isPending) && (
              <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
            )}
            Save company
          </button>
        </div>
      </form>
    </div>
  );
}

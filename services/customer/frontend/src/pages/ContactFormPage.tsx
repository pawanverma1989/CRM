import { useState } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { getContact, createContact, updateContact, type ContactWritePayload } from '../api/contacts';
import { checkDuplicates } from '../api/duplicates';
import { getCustomFields } from '../api/customFields';
import { getPicklist } from '../api/picklists';
import { useOwners } from '../hooks/useOwners';
import { OwnerSelectHint } from '../components/OwnerSelectHint';
import { emptyToNull, pruneUndefined, cleanCustomFields } from '../api/payload';
import { contactFormSchema, type ContactFormValues } from '../lib/validation';
import { validateCustomFields, initialCustomFieldValues } from '../lib/customFields';
import { getApiErrorMessage, isVersionConflict, getConflictRef, getFieldErrors } from '../lib/utils';
import { usePermissions } from '../hooks/usePermissions';
import { useToast } from '../contexts/ToastContext';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { TagInput } from '../components/TagInput';
import { CompanyPicker } from '../components/CompanyPicker';
import { CustomFieldInput } from '../components/CustomFieldInput';
import { DuplicateWarning, ConflictBanner, VersionConflictBanner } from '../components/DuplicateWarning';
import { LoadingBlock, ErrorBlock } from '../components/StateBlocks';
import type { ConflictRef, CustomFieldValues, DuplicateMatchDto } from '../types';

export function ContactFormPage() {
  const { id } = useParams<{ id: string }>();
  const isEditing = Boolean(id);
  const navigate = useNavigate();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const { canChooseOwner } = usePermissions();

  const [tags, setTags] = useState<string[]>([]);
  const [companyId, setCompanyId] = useState<string | null>(null);
  const [companyLabel, setCompanyLabel] = useState<string | null>(null);
  const [customFieldValues, setCustomFieldValues] = useState<CustomFieldValues>({});
  const [customFieldErrors, setCustomFieldErrors] = useState<Record<string, string>>({});
  const [duplicateMatches, setDuplicateMatches] = useState<DuplicateMatchDto[]>([]);
  const [pendingValues, setPendingValues] = useState<ContactFormValues | null>(null);
  const [versionConflict, setVersionConflict] = useState(false);
  const [conflict, setConflict] = useState<{ message: string; ref?: ConflictRef } | null>(null);
  const [initialized, setInitialized] = useState(!isEditing);

  const {
    data: contact,
    isLoading: isLoadingContact,
    isError: isContactError,
    error: contactError,
    refetch: refetchContact,
  } = useQuery({
    queryKey: ['contact', id],
    queryFn: () => getContact(id!),
    enabled: isEditing,
  });

  const { owners, isLoading: ownersLoading, isFetching: ownersFetching, refetch: refetchOwners } = useOwners({ fresh: true });
  const { data: customFieldDefs = [], isSuccess: customFieldsLoaded } = useQuery({
    queryKey: ['custom-fields', 'contact'],
    queryFn: () => getCustomFields('contact'),
  });
  const { data: sources = [] } = useQuery({
    queryKey: ['picklist', 'contact_source'],
    queryFn: () => getPicklist('contact_source'),
  });

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<ContactFormValues>({
    resolver: zodResolver(contactFormSchema),
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      phone: '',
      mobile: '',
      jobTitle: '',
      companyId: '',
      sourceId: '',
      ownerId: '',
      addressLine1: '',
      addressLine2: '',
      city: '',
      state: '',
      postalCode: '',
      country: 'IN',
    },
  });

  // Seed the form, local tag/company/custom-field state once the record and
  // its field definitions are both in hand — both are needed to seed values.
  if (isEditing && contact && customFieldsLoaded && !initialized) {
    reset({
      firstName: contact.firstName,
      lastName: contact.lastName ?? '',
      email: contact.email ?? '',
      phone: contact.phone ?? '',
      mobile: contact.mobile ?? '',
      jobTitle: contact.jobTitle ?? '',
      companyId: contact.companyId ?? '',
      sourceId: contact.sourceId ?? '',
      ownerId: contact.ownerId ?? '',
      addressLine1: contact.addressLine1 ?? '',
      addressLine2: contact.addressLine2 ?? '',
      city: contact.city ?? '',
      state: contact.state ?? '',
      postalCode: contact.postalCode ?? '',
      country: contact.country ?? 'IN',
    });
    setTags(contact.tags);
    setCompanyId(contact.companyId ?? null);
    setCompanyLabel(contact.companyName ?? null);
    setCustomFieldValues(initialCustomFieldValues(customFieldDefs, contact.customFields));
    setInitialized(true);
  }

  const duplicateCheckMutation = useMutation({ mutationFn: checkDuplicates });

  const saveMutation = useMutation({
    mutationFn: async (values: ContactFormValues) => {
      const basePayload = {
        firstName: values.firstName.trim(),
        lastName: emptyToNull(values.lastName),
        email: emptyToNull(values.email),
        phone: emptyToNull(values.phone),
        mobile: emptyToNull(values.mobile),
        jobTitle: emptyToNull(values.jobTitle),
        companyId,
        sourceId: emptyToNull(values.sourceId),
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
      if (isEditing && contact) {
        return updateContact(contact.id, { ...pruneUndefined(basePayload), version: contact.version });
      }
      return createContact({ ...basePayload, ownerId: basePayload.ownerId ?? null } as ContactWritePayload);
    },
    onSuccess: (saved) => {
      queryClient.invalidateQueries({ queryKey: ['contacts'] });
      queryClient.invalidateQueries({ queryKey: ['contact', saved.id] });
      showToast(isEditing ? 'Contact saved' : 'Contact created', 'success');
      navigate(`/contacts/${saved.id}`);
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

  const doSave = async (values: ContactFormValues) => {
    setConflict(null);
    setVersionConflict(false);
    try {
      await saveMutation.mutateAsync(values);
    } catch {
      // Already surfaced to the user via the mutation's onError above.
    }
  };

  const onValidSubmit = async (values: ContactFormValues) => {
    const cfErrors = validateCustomFields(customFieldDefs, customFieldValues);
    setCustomFieldErrors(cfErrors);
    if (Object.keys(cfErrors).length > 0) return;

    try {
      const result = await duplicateCheckMutation.mutateAsync({
        entityType: 'contact',
        excludeId: contact?.id,
        firstName: values.firstName,
        lastName: emptyToNull(values.lastName) ?? undefined,
        email: emptyToNull(values.email) ?? undefined,
        phone: emptyToNull(values.phone) ?? undefined,
        companyId: companyId ?? undefined,
      });
      if (result.matches.length > 0) {
        setDuplicateMatches(result.matches);
        setPendingValues(values);
        return;
      }
    } catch {
      // The duplicate check is a courtesy; if it fails, the save still goes ahead
      // and the server's own DUP-1 check is authoritative.
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
    await refetchContact();
  };

  if (isEditing && isLoadingContact) return <LoadingBlock label="Loading contact…" />;
  if (isEditing && isContactError) {
    return <ErrorBlock error={contactError} title="Could not load this contact" onRetry={refetchContact} />;
  }

  const serverFieldErrors = conflict ? {} : getFieldErrors(saveMutation.error);
  const fieldError = (name: string, zodMessage?: string) => zodMessage ?? serverFieldErrors[name];

  return (
    <div className="max-w-3xl">
      <Link to={isEditing && contact ? `/contacts/${contact.id}` : '/contacts'} className="text-sm text-gray-500 hover:text-gray-700 mb-6 inline-flex items-center gap-1">
        ← Back
      </Link>
      <h1 className="text-2xl font-bold text-gray-900 mt-2 mb-6">{isEditing ? 'Edit contact' : 'Add contact'}</h1>

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
            <FormField label="First name" required error={fieldError('firstName', errors.firstName?.message)} {...register('firstName')} />
            <FormField label="Last name" error={fieldError('lastName', errors.lastName?.message)} {...register('lastName')} />
            <FormField
              label="Email"
              type="email"
              hint="An existing live contact cannot share this email"
              error={fieldError('email', errors.email?.message)}
              {...register('email')}
            />
            <FormField
              label="Phone"
              type="tel"
              hint="Without a country code the number is treated as Indian (+91)"
              error={fieldError('phone', errors.phone?.message)}
              {...register('phone')}
            />
            <FormField label="Mobile" type="tel" error={fieldError('mobile', errors.mobile?.message)} {...register('mobile')} />
            <FormField label="Job title" error={fieldError('jobTitle', errors.jobTitle?.message)} {...register('jobTitle')} />
            <CompanyPicker
              value={companyId}
              valueLabel={companyLabel}
              onChange={(cid, label) => {
                setCompanyId(cid);
                setCompanyLabel(label);
                setValue('companyId', cid ?? '');
              }}
            />
            <SelectField
              label="Source"
              placeholder="Not set"
              options={sources.filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.value }))}
              error={fieldError('sourceId')}
              {...register('sourceId')}
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
            onClick={() => navigate(isEditing && contact ? `/contacts/${contact.id}` : '/contacts')}
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
            Save contact
          </button>
        </div>
      </form>
    </div>
  );
}

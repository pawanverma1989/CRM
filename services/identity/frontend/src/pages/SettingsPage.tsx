import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { getOrganization, updateOrganization } from '../api/organization';
import { useToast } from '../contexts/ToastContext';
import { FormField } from '../components/FormField';
import { SelectField } from '../components/SelectField';
import { getApiErrorMessage } from '../lib/utils';

const CURRENCIES = ['INR', 'USD', 'EUR', 'GBP', 'AUD', 'CAD', 'SGD', 'AED'];

const TIMEZONES = [
  'Asia/Kolkata',
  'UTC',
  'America/New_York',
  'America/Los_Angeles',
  'America/Chicago',
  'Europe/London',
  'Europe/Paris',
  'Asia/Dubai',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Sydney',
];

const schema = z.object({
  name: z.string().min(1, 'Organization name is required').max(200),
  defaultCurrency: z.string().min(1, 'Currency is required'),
  timezone: z.string().min(1, 'Timezone is required'),
});

type FormData = z.infer<typeof schema>;

export function SettingsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const { data: org, isLoading } = useQuery({
    queryKey: ['organization'],
    queryFn: getOrganization,
  });

  const { register, handleSubmit, formState: { errors, isDirty } } = useForm<FormData>({
    resolver: zodResolver(schema),
    values: org
      ? { name: org.name, defaultCurrency: org.defaultCurrency, timezone: org.timezone }
      : undefined,
  });

  const mutation = useMutation({
    mutationFn: updateOrganization,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['organization'] });
      showToast('Organization settings saved', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
      </div>
    );
  }

  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-bold text-gray-900 mb-6">Organization settings</h1>

      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <form onSubmit={handleSubmit((d) => mutation.mutate(d))} className="space-y-5" noValidate>
          <FormField
            label="Organization name"
            type="text"
            error={errors.name?.message}
            {...register('name')}
          />

          <SelectField
            label="Default currency"
            options={CURRENCIES.map((c) => ({ value: c, label: c }))}
            error={errors.defaultCurrency?.message}
            {...register('defaultCurrency')}
          />

          <SelectField
            label="Timezone"
            options={TIMEZONES.map((tz) => ({ value: tz, label: tz }))}
            error={errors.timezone?.message}
            {...register('timezone')}
          />

          <div className="flex justify-end pt-2">
            <button
              type="submit"
              disabled={!isDirty || mutation.isPending}
              className="flex items-center gap-2 px-5 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60 transition-colors"
            >
              {mutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Save settings
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

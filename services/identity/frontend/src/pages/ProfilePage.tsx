import { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { getSessions, revokeSession, updateMe, changePassword } from '../api/me';
import { useAuth } from '../contexts/AuthContext';
import { useToast } from '../contexts/ToastContext';
import { FormField } from '../components/FormField';
import { PasswordStrength } from '../components/PasswordStrength';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { formatRelative, formatDateTime, getRoleLabel, getApiErrorMessage } from '../lib/utils';

const profileSchema = z.object({
  firstName: z.string().min(1, 'First name is required').max(100),
  lastName: z.string().max(100).optional(),
  phone: z.string().max(30).optional(),
});

const passwordSchema = z
  .object({
    currentPassword: z.string().min(1, 'Current password is required'),
    newPassword: z.string().min(10, 'New password must be at least 10 characters'),
    confirmPassword: z.string(),
  })
  .refine((d) => d.newPassword === d.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  });

type ProfileFormData = z.infer<typeof profileSchema>;
type PasswordFormData = z.infer<typeof passwordSchema>;

export function ProfilePage() {
  const { user, updateUser } = useAuth();
  const { showToast } = useToast();
  const queryClient = useQueryClient();
  const location = useLocation();
  const [revokeTarget, setRevokeTarget] = useState<string | null>(null);

  const { data: sessions, isLoading: sessionsLoading } = useQuery({
    queryKey: ['sessions'],
    queryFn: getSessions,
  });

  const updateProfileMutation = useMutation({
    mutationFn: updateMe,
    onSuccess: (data) => {
      updateUser(data);
      showToast('Profile updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const changePasswordMutation = useMutation({
    mutationFn: changePassword,
    onSuccess: () => {
      showToast('Password changed', 'success');
      passwordReset();
      // The server clears the flag; mirror it locally so the prompt never reappears.
      if (user) updateUser({ ...user, mustChangePassword: false });
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const revokeMutation = useMutation({
    mutationFn: revokeSession,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      showToast('Session revoked', 'success');
      setRevokeTarget(null);
    },
    onError: (err) => {
      showToast(getApiErrorMessage(err), 'error');
      setRevokeTarget(null);
    },
  });

  const {
    register: profileRegister,
    handleSubmit: handleProfileSubmit,
    formState: { errors: profileErrors, isDirty: profileDirty },
  } = useForm<ProfileFormData>({
    resolver: zodResolver(profileSchema),
    values: user ? { firstName: user.firstName, lastName: user.lastName ?? '', phone: user.phone ?? '' } : undefined,
  });

  const {
    register: passwordRegister,
    handleSubmit: handlePasswordSubmit,
    reset: passwordReset,
    watch,
    formState: { errors: passwordErrors },
  } = useForm<PasswordFormData>({ resolver: zodResolver(passwordSchema) });

  const newPasswordValue = watch('newPassword', '');

  // Deep link from the password-change prompt: scroll to the section and focus its first field.
  useEffect(() => {
    if (location.hash !== '#change-password') return;
    document.getElementById('change-password')?.scrollIntoView({ block: 'start' });
    document.getElementById('current-password')?.focus({ preventScroll: true });
  }, [location.hash, location.key]);

  return (
    <div className="max-w-2xl space-y-6">
      <h1 className="text-2xl font-bold text-gray-900">My profile</h1>

      {/* Profile info */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <div className="flex items-center gap-3 mb-5">
          <div className="w-12 h-12 rounded-full bg-primary-600 flex items-center justify-center text-white text-lg font-medium">
            {user?.firstName.charAt(0).toUpperCase()}
            {user?.lastName?.charAt(0).toUpperCase()}
          </div>
          <div>
            <p className="font-semibold text-gray-900">
              {user?.firstName} {user?.lastName}
            </p>
            <p className="text-sm text-gray-500">{user?.email}</p>
            <p className="text-xs text-gray-400 mt-0.5">{getRoleLabel(user?.role ?? '')}</p>
          </div>
        </div>

        <form
          onSubmit={handleProfileSubmit((d) => updateProfileMutation.mutate(d))}
          className="space-y-4"
          noValidate
        >
          <div className="grid grid-cols-2 gap-3">
            <FormField
              label="First name"
              type="text"
              autoComplete="given-name"
              error={profileErrors.firstName?.message}
              {...profileRegister('firstName')}
            />
            <FormField
              label="Last name"
              type="text"
              autoComplete="family-name"
              error={profileErrors.lastName?.message}
              {...profileRegister('lastName')}
            />
          </div>

          <FormField
            label="Phone"
            type="tel"
            autoComplete="tel"
            error={profileErrors.phone?.message}
            placeholder="+91 98765 43210"
            {...profileRegister('phone')}
          />

          <div className="flex justify-end pt-1">
            <button
              type="submit"
              disabled={!profileDirty || updateProfileMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {updateProfileMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Save profile
            </button>
          </div>
        </form>
      </div>

      {/* Change password */}
      <div id="change-password" className="bg-white rounded-lg border border-gray-200 p-6 scroll-mt-6">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Change password</h2>
        <form
          onSubmit={handlePasswordSubmit((d) =>
            changePasswordMutation.mutate({
              currentPassword: d.currentPassword,
              newPassword: d.newPassword,
            })
          )}
          className="space-y-4"
          noValidate
        >
          <FormField
            label="Current password"
            type="password"
            autoComplete="current-password"
            error={passwordErrors.currentPassword?.message}
            {...passwordRegister('currentPassword')}
          />
          <div>
            <FormField
              label="New password"
              type="password"
              autoComplete="new-password"
              error={passwordErrors.newPassword?.message}
              {...passwordRegister('newPassword')}
            />
            <PasswordStrength password={newPasswordValue} />
          </div>
          <FormField
            label="Confirm new password"
            type="password"
            autoComplete="new-password"
            error={passwordErrors.confirmPassword?.message}
            {...passwordRegister('confirmPassword')}
          />
          <div className="flex justify-end pt-1">
            <button
              type="submit"
              disabled={changePasswordMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {changePasswordMutation.isPending && (
                <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
              )}
              Change password
            </button>
          </div>
        </form>
      </div>

      {/* Active sessions */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
        <h2 className="text-base font-semibold text-gray-900 mb-4">Active sessions</h2>
        {sessionsLoading ? (
          <div className="flex items-center justify-center py-8">
            <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-primary-600" />
          </div>
        ) : sessions?.length === 0 ? (
          <p className="text-sm text-gray-500">No active sessions.</p>
        ) : (
          <div className="space-y-3">
            {sessions?.map((session) => (
              <div
                key={session.id}
                className="flex items-start justify-between p-3 rounded-md border border-gray-100 bg-gray-50"
              >
                <div className="min-w-0 flex-1">
                  <p className="text-sm font-medium text-gray-900 truncate">
                    {session.userAgent ?? 'Unknown device'}
                  </p>
                  <p className="text-xs text-gray-500 mt-0.5">
                    {session.ipAddress && <span>{session.ipAddress} · </span>}
                    Started {formatRelative(session.createdAt)}
                    {session.lastUsedAt && <span> · Last used {formatRelative(session.lastUsedAt)}</span>}
                  </p>
                  <p className="text-xs text-gray-400">Expires {formatDateTime(session.expiresAt)}</p>
                </div>
                <button
                  onClick={() => setRevokeTarget(session.id)}
                  className="ml-4 flex-shrink-0 text-sm text-red-600 hover:text-red-700 font-medium"
                >
                  Revoke
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      <ConfirmDialog
        isOpen={!!revokeTarget}
        title="Revoke session"
        message="This will immediately sign out that session. Continue?"
        confirmLabel="Revoke"
        danger
        isLoading={revokeMutation.isPending}
        onConfirm={() => revokeTarget && revokeMutation.mutate(revokeTarget)}
        onCancel={() => setRevokeTarget(null)}
      />
    </div>
  );
}

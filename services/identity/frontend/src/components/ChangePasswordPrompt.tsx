import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { dismissPasswordPrompt, isPasswordPromptDismissed } from '../lib/passwordPrompt';

/**
 * Non-blocking banner suggesting a password change for users whose password
 * was set by an administrator. Dismissal lasts for the current login session.
 */
export function ChangePasswordPrompt() {
  const { user } = useAuth();
  if (!user?.mustChangePassword) return null;
  return <PromptBanner key={user.id} userId={user.id} />;
}

function PromptBanner({ userId }: { userId: string }) {
  const navigate = useNavigate();
  const [dismissed, setDismissed] = useState(() => isPasswordPromptDismissed(userId));

  if (dismissed) return null;

  const dismiss = () => {
    dismissPasswordPrompt(userId);
    setDismissed(true);
  };

  const goToChangePassword = () => {
    dismiss();
    navigate('/profile#change-password');
  };

  return (
    <div role="region" aria-label="Password change suggestion" className="bg-amber-50 border-b border-amber-200">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-3 flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-amber-900">Change your password</p>
          <p className="text-sm text-amber-800">
            Your password was set by an administrator. For your security, we recommend changing it now.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={goToChangePassword}
            className="px-3 py-1.5 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-1"
          >
            Change password
          </button>
          <button
            type="button"
            onClick={dismiss}
            className="px-3 py-1.5 text-sm font-medium text-amber-900 bg-white border border-amber-300 rounded-md hover:bg-amber-100 focus:outline-none focus:ring-2 focus:ring-amber-500 focus:ring-offset-1"
          >
            Remind me later
          </button>
        </div>
      </div>
    </div>
  );
}

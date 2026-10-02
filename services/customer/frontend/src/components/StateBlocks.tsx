import { getApiErrorMessage } from '../lib/utils';

/**
 * The four async states every screen in this app shows explicitly: loading,
 * error, empty and success (success is the screen's own content).
 */

export function LoadingBlock({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center py-16" role="status" aria-live="polite">
      <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600" />
      <span className="sr-only">{label}</span>
    </div>
  );
}

interface ErrorBlockProps {
  error: unknown;
  /** Shown above the message, e.g. "Could not load contacts". */
  title?: string;
  onRetry?: () => void;
}

export function ErrorBlock({ error, title = 'Something went wrong', onRetry }: ErrorBlockProps) {
  return (
    <div className="px-6 py-12 text-center" role="alert">
      <h2 className="text-sm font-semibold text-gray-900 mb-1">{title}</h2>
      <p className="text-sm text-gray-600 mb-4">{getApiErrorMessage(error)}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="px-3 py-1.5 text-sm font-medium text-primary-700 border border-primary-300 rounded-md hover:bg-primary-50"
        >
          Try again
        </button>
      )}
    </div>
  );
}

interface EmptyBlockProps {
  title: string;
  message?: string;
  action?: React.ReactNode;
}

export function EmptyBlock({ title, message, action }: EmptyBlockProps) {
  return (
    <div className="px-6 py-12 text-center">
      <h2 className="text-sm font-semibold text-gray-900 mb-1">{title}</h2>
      {message && <p className="text-sm text-gray-600 mb-4">{message}</p>}
      {action}
    </div>
  );
}

/**
 * CON-5 / COM-2: the deals and timeline panels. The Sales and Activity services
 * do not exist yet, so this states that plainly instead of calling them.
 */
export function NotAvailablePanel({ title, service }: { title: string; service: string }) {
  return (
    <section className="bg-white rounded-lg border border-gray-200 p-4">
      <h2 className="text-sm font-semibold text-gray-900 mb-1">{title}</h2>
      <p className="text-sm text-gray-500">
        Not available yet — the {service} service has not been built. This panel will show{' '}
        {title.toLowerCase()} once it is.
      </p>
    </section>
  );
}

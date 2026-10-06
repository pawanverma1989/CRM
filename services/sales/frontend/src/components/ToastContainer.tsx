import { useToast } from '../contexts/ToastContext';

const typeClasses = {
  success: 'bg-green-600 text-white',
  error: 'bg-red-600 text-white',
  warning: 'bg-yellow-500 text-white',
  info: 'bg-primary-600 text-white',
};

const typeIcons = {
  success: '✓',
  error: '✕',
  warning: '!',
  info: 'i',
};

const typeLabels = {
  success: 'Success',
  error: 'Error',
  warning: 'Warning',
  info: 'Information',
};

export function ToastContainer() {
  const { toasts, removeToast } = useToast();

  return (
    <div
      className="fixed top-4 right-4 z-50 flex flex-col gap-2 max-w-sm w-full pointer-events-none"
      role="status"
      aria-live="polite"
    >
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className={`flex items-start gap-3 px-4 py-3 rounded-lg shadow-lg pointer-events-auto transition-all duration-300 ${typeClasses[toast.type]}`}
        >
          <span
            className="flex-shrink-0 w-5 h-5 flex items-center justify-center rounded-full bg-white/20 text-xs font-bold"
            aria-hidden="true"
          >
            {typeIcons[toast.type]}
          </span>
          <p className="flex-1 text-sm">
            <span className="sr-only">{typeLabels[toast.type]}: </span>
            {toast.message}
          </p>
          <button
            onClick={() => removeToast(toast.id)}
            className="flex-shrink-0 text-white/80 hover:text-white text-lg leading-none"
            aria-label="Dismiss"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}

import { forwardRef, type InputHTMLAttributes } from 'react';
import { cn } from '../lib/utils';

interface FormFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  hint?: string;
  /** Marks the field visually and for assistive technology. */
  required?: boolean;
}

export const FormField = forwardRef<HTMLInputElement, FormFieldProps>(
  ({ label, error, hint, className, id, required, name, ...props }, ref) => {
    const fieldId = id ?? name ?? label.toLowerCase().replace(/\s+/g, '-');
    const hintId = `${fieldId}-hint`;
    const errorId = `${fieldId}-error`;

    return (
      <div>
        <label htmlFor={fieldId} className="block text-sm font-medium text-gray-700 mb-1">
          {label}
          {required && (
            <span className="text-red-600 ml-0.5" aria-hidden="true">
              *
            </span>
          )}
        </label>
        <input
          id={fieldId}
          name={name}
          ref={ref}
          required={required}
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : hint ? hintId : undefined}
          className={cn(
            'block w-full px-3 py-2 border rounded-md shadow-sm text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 disabled:bg-gray-50 disabled:text-gray-500',
            error
              ? 'border-red-300 text-red-900 placeholder-red-300'
              : 'border-gray-300 text-gray-900 placeholder-gray-400',
            className
          )}
          {...props}
        />
        {hint && !error && (
          <p id={hintId} className="mt-1 text-xs text-gray-500">
            {hint}
          </p>
        )}
        {error && (
          <p id={errorId} className="mt-1 text-xs text-red-600">
            {error}
          </p>
        )}
      </div>
    );
  }
);

FormField.displayName = 'FormField';

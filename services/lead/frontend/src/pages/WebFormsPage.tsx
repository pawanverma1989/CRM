import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getWebForms, createWebForm, updateWebForm, getWebFormEmbed } from '../api/webForms';
import { useToast } from '../contexts/ToastContext';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { getApiErrorMessage } from '../lib/utils';
import type { WebFormDto, WebFormEmbedDto } from '../types';

export function WebFormsPage() {
  const { showToast } = useToast();
  const queryClient = useQueryClient();

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [nameError, setNameError] = useState('');
  const [embedForm, setEmbedForm] = useState<WebFormDto | null>(null);
  const [embedData, setEmbedData] = useState<WebFormEmbedDto | null>(null);
  const [embedLoading, setEmbedLoading] = useState(false);
  const [copied, setCopied] = useState(false);

  const { data: forms = [], isLoading, isError, error, refetch } = useQuery({
    queryKey: ['web-forms'],
    queryFn: getWebForms,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['web-forms'] });

  const createMutation = useMutation({
    mutationFn: () => createWebForm({ name: newName.trim() }),
    onSuccess: () => {
      invalidate();
      showToast('Web form created', 'success');
      setCreateOpen(false);
      setNewName('');
      setNameError('');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const toggleActiveMutation = useMutation({
    mutationFn: (form: { id: string; isActive: boolean }) =>
      updateWebForm(form.id, { isActive: !form.isActive }),
    onSuccess: (_, form) => {
      invalidate();
      showToast(form.isActive ? 'Form deactivated' : 'Form activated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const handleCreate = () => {
    if (!newName.trim()) {
      setNameError('Name is required');
      return;
    }
    setNameError('');
    createMutation.mutate();
  };

  const openEmbed = async (form: WebFormDto) => {
    setEmbedForm(form);
    setEmbedData(null);
    setEmbedLoading(true);
    try {
      const data = await getWebFormEmbed(form.id);
      setEmbedData(data);
    } catch (err) {
      showToast(getApiErrorMessage(err), 'error');
    } finally {
      setEmbedLoading(false);
    }
  };

  const copySnippet = () => {
    if (!embedData) return;
    navigator.clipboard.writeText(embedData.embedSnippet).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <div className="max-w-5xl">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Web forms</h1>
        <button
          type="button"
          onClick={() => { setCreateOpen(true); setNewName(''); setNameError(''); }}
          className="px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700"
        >
          Add form
        </button>
      </div>

      <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
        {isLoading ? (
          <LoadingBlock label="Loading web forms…" />
        ) : isError ? (
          <ErrorBlock error={error} title="Could not load web forms" onRetry={refetch} />
        ) : forms.length === 0 ? (
          <EmptyBlock title="No web forms yet" message="Add the first form to start capturing leads from your website." />
        ) : (
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Name</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Public key</th>
                <th scope="col" className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                <th scope="col" className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {forms.map((form) => (
                <tr key={form.id} className={form.isActive ? '' : 'opacity-60'}>
                  <td className="px-4 py-3 text-sm font-medium text-gray-900">{form.name}</td>
                  <td className="px-4 py-3 text-sm text-gray-600 font-mono">
                    <span className="text-xs">{form.publicKey}</span>
                  </td>
                  <td className="px-4 py-3 text-sm">
                    <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${form.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                      {form.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right text-sm">
                    <div className="flex justify-end gap-3">
                      <button
                        type="button"
                        onClick={() => openEmbed(form)}
                        className="text-primary-600 hover:text-primary-700 font-medium"
                      >
                        Embed
                      </button>
                      <button
                        type="button"
                        onClick={() => toggleActiveMutation.mutate(form)}
                        disabled={toggleActiveMutation.isPending}
                        className="text-gray-600 hover:text-gray-800 font-medium disabled:opacity-50"
                      >
                        {form.isActive ? 'Deactivate' : 'Activate'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Modal isOpen={createOpen} title="Add web form" onClose={() => setCreateOpen(false)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            error={nameError}
            autoFocus
          />
          <div className="flex justify-end gap-3">
            <button type="button" onClick={() => setCreateOpen(false)} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50">
              Cancel
            </button>
            <button
              type="button"
              onClick={handleCreate}
              disabled={createMutation.isPending}
              className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
            >
              {createMutation.isPending && <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />}
              Create
            </button>
          </div>
        </div>
      </Modal>

      <Modal isOpen={!!embedForm} title={embedForm ? `Embed: ${embedForm.name}` : ''} onClose={() => { setEmbedForm(null); setEmbedData(null); }} maxWidth="lg">
        {embedLoading ? (
          <div className="flex items-center justify-center py-8">
            <span className="animate-spin h-6 w-6 border-2 border-primary-300 border-t-primary-600 rounded-full" />
          </div>
        ) : embedData ? (
          <div className="space-y-4">
            <div>
              <p className="text-sm font-medium text-gray-700 mb-1">Submit URL</p>
              <code className="block text-xs bg-gray-50 border border-gray-200 rounded-md px-3 py-2 text-gray-700 break-all">
                {embedData.submitUrl}
              </code>
            </div>
            <div>
              <p className="text-sm font-medium text-gray-700 mb-1">Embed snippet</p>
              <pre className="text-xs bg-gray-50 border border-gray-200 rounded-md px-3 py-2 text-gray-700 overflow-x-auto whitespace-pre-wrap break-all">
                {embedData.embedSnippet}
              </pre>
            </div>
            <div className="flex justify-end">
              <button
                type="button"
                onClick={copySnippet}
                className="px-4 py-2 text-sm font-medium text-primary-700 border border-primary-300 rounded-md hover:bg-primary-50"
              >
                {copied ? 'Copied!' : 'Copy snippet'}
              </button>
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}

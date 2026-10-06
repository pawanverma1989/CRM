import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listPipelines,
  createPipeline,
  updatePipeline,
  addStage,
  reorderStages,
  updateStage,
} from '../api/pipelines';
import { Modal } from '../components/Modal';
import { FormField } from '../components/FormField';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { useToast } from '../contexts/ToastContext';
import { getApiErrorMessage } from '../lib/utils';
import type { PipelineDto, PipelineStageDto } from '../types';

interface PipelineFormState {
  name: string;
  description: string;
  isDefault: boolean;
  staleDays: string;
}

interface StageFormState {
  name: string;
  stageType: string;
  probability: string;
  isActive: boolean;
}

const STAGE_TYPES = ['open', 'won', 'lost'] as const;

export function PipelinesPage() {
  const queryClient = useQueryClient();
  const { showToast } = useToast();

  const [editingPipeline, setEditingPipeline] = useState<PipelineDto | null>(null);
  const [showPipelineForm, setShowPipelineForm] = useState(false);
  const [pipelineForm, setPipelineForm] = useState<PipelineFormState>({ name: '', description: '', isDefault: false, staleDays: '14' });

  const [addingStageForPipeline, setAddingStageForPipeline] = useState<PipelineDto | null>(null);
  const [editingStage, setEditingStage] = useState<{ pipelineId: string; stage: PipelineStageDto } | null>(null);
  const [stageForm, setStageForm] = useState<StageFormState>({ name: '', stageType: 'open', probability: '0', isActive: true });

  const { data: pipelines = [], isLoading, error, refetch } = useQuery({
    queryKey: ['pipelines'],
    queryFn: listPipelines,
  });

  const createMutation = useMutation({
    mutationFn: () =>
      createPipeline({
        name: pipelineForm.name,
        description: pipelineForm.description || null,
        isDefault: pipelineForm.isDefault,
        staleDays: Number(pipelineForm.staleDays) || 14,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pipelines'] });
      setShowPipelineForm(false);
      showToast('Pipeline created', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateMutation = useMutation({
    mutationFn: () =>
      updatePipeline(editingPipeline!.id, {
        name: pipelineForm.name,
        description: pipelineForm.description || null,
        isDefault: pipelineForm.isDefault,
        staleDays: Number(pipelineForm.staleDays) || 14,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pipelines'] });
      setEditingPipeline(null);
      showToast('Pipeline updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const addStageMutation = useMutation({
    mutationFn: () =>
      addStage(addingStageForPipeline!.id, {
        name: stageForm.name,
        stageType: stageForm.stageType as 'open' | 'won' | 'lost',
        probability: Number(stageForm.probability) || 0,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pipelines'] });
      setAddingStageForPipeline(null);
      showToast('Stage added', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const updateStageMutation = useMutation({
    mutationFn: () =>
      updateStage(editingStage!.stage.id, {
        name: stageForm.name,
        stageType: stageForm.stageType as 'open' | 'won' | 'lost',
        probability: Number(stageForm.probability) || 0,
        isActive: stageForm.isActive,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['pipelines'] });
      setEditingStage(null);
      showToast('Stage updated', 'success');
    },
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const reorderMutation = useMutation({
    mutationFn: ({ pipelineId, stageIds }: { pipelineId: string; stageIds: string[] }) =>
      reorderStages(pipelineId, stageIds),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['pipelines'] }),
    onError: (err) => showToast(getApiErrorMessage(err), 'error'),
  });

  const openCreatePipeline = () => {
    setPipelineForm({ name: '', description: '', isDefault: false, staleDays: '14' });
    setShowPipelineForm(true);
  };

  const openEditPipeline = (p: PipelineDto) => {
    setEditingPipeline(p);
    setPipelineForm({
      name: p.name,
      description: p.description ?? '',
      isDefault: p.isDefault,
      staleDays: String(p.staleDays ?? 14),
    });
  };

  const openAddStage = (p: PipelineDto) => {
    setAddingStageForPipeline(p);
    setStageForm({ name: '', stageType: 'open', probability: '0', isActive: true });
  };

  const openEditStage = (pipelineId: string, stage: PipelineStageDto) => {
    setEditingStage({ pipelineId, stage });
    setStageForm({
      name: stage.name,
      stageType: stage.stageType,
      probability: String(stage.probability ?? 0),
      isActive: stage.isActive,
    });
  };

  const moveStage = (pipeline: PipelineDto, stageId: string, direction: -1 | 1) => {
    const ids = [...pipeline.stages].sort((a, b) => a.position - b.position).map((s) => s.id);
    const idx = ids.indexOf(stageId);
    if (idx < 0) return;
    const newIdx = idx + direction;
    if (newIdx < 0 || newIdx >= ids.length) return;
    [ids[idx], ids[newIdx]] = [ids[newIdx], ids[idx]];
    reorderMutation.mutate({ pipelineId: pipeline.id, stageIds: ids });
  };

  if (isLoading) return <LoadingBlock label="Loading pipelines…" />;
  if (error) return <ErrorBlock error={error} title="Could not load pipelines" onRetry={() => refetch()} />;

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Pipelines</h1>
        <button
          type="button"
          onClick={openCreatePipeline}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
        >
          New pipeline
        </button>
      </div>

      {pipelines.length === 0 ? (
        <EmptyBlock title="No pipelines yet" message="Create your first pipeline to start tracking deals." />
      ) : (
        <div className="space-y-6">
          {pipelines.map((pipeline) => {
            const sortedStages = [...pipeline.stages].sort((a, b) => a.position - b.position);
            return (
              <div key={pipeline.id} className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                <div className="px-6 py-4 flex items-center justify-between border-b border-gray-100">
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="text-base font-semibold text-gray-900">{pipeline.name}</h2>
                      {pipeline.isDefault && (
                        <span className="text-xs font-medium px-2 py-0.5 bg-primary-100 text-primary-700 rounded-full">Default</span>
                      )}
                    </div>
                    {pipeline.description && (
                      <p className="text-sm text-gray-500 mt-0.5">{pipeline.description}</p>
                    )}
                    <p className="text-xs text-gray-400 mt-0.5">Stale after {pipeline.staleDays ?? 14} days of inactivity</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => openAddStage(pipeline)}
                      className="px-3 py-1.5 text-sm font-medium text-primary-600 border border-primary-200 rounded-md hover:bg-primary-50"
                    >
                      Add stage
                    </button>
                    <button
                      type="button"
                      onClick={() => openEditPipeline(pipeline)}
                      className="px-3 py-1.5 text-sm font-medium text-gray-600 border border-gray-200 rounded-md hover:bg-gray-50"
                    >
                      Edit
                    </button>
                  </div>
                </div>

                {sortedStages.length === 0 ? (
                  <div className="px-6 py-4 text-sm text-gray-400">No stages yet.</div>
                ) : (
                  <table className="min-w-full divide-y divide-gray-100">
                    <thead className="bg-gray-50">
                      <tr>
                        <th className="px-6 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Stage</th>
                        <th className="px-6 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Type</th>
                        <th className="px-6 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Probability</th>
                        <th className="px-6 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                        <th className="px-6 py-2 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Order</th>
                        <th className="px-6 py-2" />
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {sortedStages.map((stage, idx) => (
                        <tr key={stage.id} className="hover:bg-gray-50">
                          <td className="px-6 py-3 text-sm font-medium text-gray-900">{stage.name}</td>
                          <td className="px-6 py-3 text-sm text-gray-600 capitalize">{stage.stageType}</td>
                          <td className="px-6 py-3 text-sm text-gray-600">{stage.probability ?? 0}%</td>
                          <td className="px-6 py-3">
                            <span className={`inline-flex px-2 py-0.5 text-xs font-medium rounded-full ${stage.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
                              {stage.isActive ? 'Active' : 'Inactive'}
                            </span>
                          </td>
                          <td className="px-6 py-3 text-right">
                            <div className="flex items-center justify-end gap-1">
                              <button
                                type="button"
                                onClick={() => moveStage(pipeline, stage.id, -1)}
                                disabled={idx === 0}
                                className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"
                                aria-label="Move up"
                              >▲</button>
                              <button
                                type="button"
                                onClick={() => moveStage(pipeline, stage.id, 1)}
                                disabled={idx === sortedStages.length - 1}
                                className="p-1 text-gray-400 hover:text-gray-700 disabled:opacity-30"
                                aria-label="Move down"
                              >▼</button>
                            </div>
                          </td>
                          <td className="px-6 py-3">
                            <button
                              type="button"
                              onClick={() => openEditStage(pipeline.id, stage)}
                              className="text-sm text-primary-600 hover:text-primary-800"
                            >
                              Edit
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Create pipeline modal */}
      <Modal isOpen={showPipelineForm} title="New pipeline" onClose={() => setShowPipelineForm(false)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={pipelineForm.name}
            onChange={(e) => setPipelineForm((f) => ({ ...f, name: e.target.value }))}
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea
              value={pipelineForm.description}
              onChange={(e) => setPipelineForm((f) => ({ ...f, description: e.target.value }))}
              rows={2}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <FormField
            label="Stale after (days)"
            type="number"
            min="1"
            value={pipelineForm.staleDays}
            onChange={(e) => setPipelineForm((f) => ({ ...f, staleDays: e.target.value }))}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={pipelineForm.isDefault}
              onChange={(e) => setPipelineForm((f) => ({ ...f, isDefault: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Set as default pipeline
          </label>
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setShowPipelineForm(false)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => createMutation.mutate()}
              disabled={!pipelineForm.name || createMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {createMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Create
            </button>
          </div>
        </div>
      </Modal>

      {/* Edit pipeline modal */}
      <Modal isOpen={!!editingPipeline} title="Edit pipeline" onClose={() => setEditingPipeline(null)}>
        <div className="space-y-4">
          <FormField
            label="Name"
            required
            value={pipelineForm.name}
            onChange={(e) => setPipelineForm((f) => ({ ...f, name: e.target.value }))}
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
            <textarea
              value={pipelineForm.description}
              onChange={(e) => setPipelineForm((f) => ({ ...f, description: e.target.value }))}
              rows={2}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            />
          </div>
          <FormField
            label="Stale after (days)"
            type="number"
            min="1"
            value={pipelineForm.staleDays}
            onChange={(e) => setPipelineForm((f) => ({ ...f, staleDays: e.target.value }))}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={pipelineForm.isDefault}
              onChange={(e) => setPipelineForm((f) => ({ ...f, isDefault: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Set as default pipeline
          </label>
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setEditingPipeline(null)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => updateMutation.mutate()}
              disabled={!pipelineForm.name || updateMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {updateMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Save
            </button>
          </div>
        </div>
      </Modal>

      {/* Add stage modal */}
      <Modal isOpen={!!addingStageForPipeline} title={`Add stage to ${addingStageForPipeline?.name ?? ''}`} onClose={() => setAddingStageForPipeline(null)}>
        <div className="space-y-4">
          <FormField
            label="Stage name"
            required
            value={stageForm.name}
            onChange={(e) => setStageForm((f) => ({ ...f, name: e.target.value }))}
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Stage type <span className="text-red-600">*</span></label>
            <select
              value={stageForm.stageType}
              onChange={(e) => setStageForm((f) => ({ ...f, stageType: e.target.value }))}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {STAGE_TYPES.map((t) => <option key={t} value={t}>{t.charAt(0).toUpperCase() + t.slice(1)}</option>)}
            </select>
          </div>
          <FormField
            label="Default probability (%)"
            type="number"
            min="0"
            max="100"
            value={stageForm.probability}
            onChange={(e) => setStageForm((f) => ({ ...f, probability: e.target.value }))}
          />
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setAddingStageForPipeline(null)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => addStageMutation.mutate()}
              disabled={!stageForm.name || addStageMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {addStageMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Add stage
            </button>
          </div>
        </div>
      </Modal>

      {/* Edit stage modal */}
      <Modal isOpen={!!editingStage} title={`Edit stage: ${editingStage?.stage.name ?? ''}`} onClose={() => setEditingStage(null)}>
        <div className="space-y-4">
          <FormField
            label="Stage name"
            required
            value={stageForm.name}
            onChange={(e) => setStageForm((f) => ({ ...f, name: e.target.value }))}
          />
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Stage type <span className="text-red-600">*</span></label>
            <select
              value={stageForm.stageType}
              onChange={(e) => setStageForm((f) => ({ ...f, stageType: e.target.value }))}
              className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
            >
              {STAGE_TYPES.map((t) => <option key={t} value={t}>{t.charAt(0).toUpperCase() + t.slice(1)}</option>)}
            </select>
          </div>
          <FormField
            label="Default probability (%)"
            type="number"
            min="0"
            max="100"
            value={stageForm.probability}
            onChange={(e) => setStageForm((f) => ({ ...f, probability: e.target.value }))}
          />
          <label className="flex items-center gap-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={stageForm.isActive}
              onChange={(e) => setStageForm((f) => ({ ...f, isActive: e.target.checked }))}
              className="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
            />
            Active
          </label>
          <div className="flex justify-end gap-3 pt-1">
            <button type="button" onClick={() => setEditingStage(null)} className="px-4 py-2 text-sm font-medium text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50">Cancel</button>
            <button
              type="button"
              onClick={() => updateStageMutation.mutate()}
              disabled={!stageForm.name || updateStageMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
            >
              {updateStageMutation.isPending && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
              Save
            </button>
          </div>
        </div>
      </Modal>
    </div>
  );
}

import { ReactFlowProvider } from '@xyflow/react';
import { useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useNavigate, useParams } from 'react-router-dom';
import { AgentsApi } from '../api/agents';
import { Header } from '../layouts/Header';
import { Sidebar } from '../layouts/Sidebar';
import { PropertiesPanel } from '../layouts/PropertiesPanel';
import { DesignerCanvas } from '../components/designer/DesignerCanvas';
import { SimulationPanel } from '../components/designer/SimulationPanel';
import { RootState } from '../store';
import {
  hydrateFromAgentDto,
  mapGraphToDesignerDto,
  markSaved,
  setLoading,
  setSaveError
} from '../store/slices/designerSlice';
import { DesignValidationIssue } from '../types/studio';

const TENANT_ID = 'default-tenant';

export default function DesignerPage() {
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const designerState = useSelector((state: RootState) => state.designer);
  const [isSaving, setIsSaving] = useState(false);
  const [validationIssues, setValidationIssues] = useState<DesignValidationIssue[]>([]);

  const isNew = useMemo(() => !id || id === 'new', [id]);

  useEffect(() => {
    if (isNew) {
      return;
    }

    const load = async () => {
      if (!id) return;
      dispatch(setLoading(true));
      try {
        const dto = await AgentsApi.getAgent(TENANT_ID, id);
        dispatch(hydrateFromAgentDto(dto));
      } catch (error) {
        dispatch(setSaveError(error instanceof Error ? error.message : 'Failed to load agent'));
      } finally {
        dispatch(setLoading(false));
      }
    };

    void load();
  }, [dispatch, id, isNew]);

  const onSave = async () => {
    setIsSaving(true);
    dispatch(setSaveError(null));

    const payload = mapGraphToDesignerDto(designerState);

    try {
      if (isNew) {
        const created = await AgentsApi.createAgent(TENANT_ID, payload);
        dispatch(hydrateFromAgentDto(created));
        navigate(`/studio/${created.id}`, { replace: true });
      } else if (id) {
        const updated = await AgentsApi.updateAgent(TENANT_ID, id, payload);
        dispatch(hydrateFromAgentDto(updated));
      }

      dispatch(markSaved());
    } catch (error) {
      dispatch(setSaveError(error instanceof Error ? error.message : 'Failed to save agent'));
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="app-layout">
      <Header onSave={onSave} isSaving={isSaving} hasBlockingIssues={validationIssues.some((issue) => issue.severity === 'error')} />
      <div className="content-area">
        <main className="main-canvas">
          <div className="floating floating-left">
            <Sidebar />
          </div>
          <div className="floating floating-right">
            <PropertiesPanel />
          </div>
          {validationIssues.length > 0 && (
            <div className="validation-banner">
              {validationIssues.map((issue) => (
                <div key={issue.id} className={`issue ${issue.severity}`}>
                  {issue.message}
                </div>
              ))}
            </div>
          )}
          <ReactFlowProvider>
            <DesignerCanvas onValidationChange={setValidationIssues} />
          </ReactFlowProvider>
          <SimulationPanel />
        </main>
      </div>

      <style>{`
        .app-layout {
          height: 100vh;
          display: flex;
          flex-direction: column;
          overflow: hidden;
          background: var(--bg-primary);
        }
        .content-area {
          flex: 1;
          display: block;
          overflow: hidden;
        }
        .main-canvas {
          height: 100%;
          position: relative;
          display: flex;
          flex-direction: column;
          padding: 12px;
          gap: 8px;
        }
        .floating {
          position: absolute;
          top: 16px;
          z-index: 20;
        }
        .floating-left {
          left: 16px;
        }
        .floating-right {
          right: 16px;
        }
        .validation-banner {
          padding: 8px 12px;
          border-bottom: 1px solid var(--border-light);
          background: var(--bg-secondary);
          display: flex;
          flex-direction: column;
          gap: 4px;
        }
        .issue { font-size: 12px; }
        .issue.error { color: #f87171; }
        .issue.warning { color: #fbbf24; }
      `}</style>
    </div>
  );
}

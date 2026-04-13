import { ReactFlowProvider } from '@xyflow/react';
import { useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useNavigate, useParams } from 'react-router-dom';
import { AgentsApi } from '../api/agents';
import { Header } from '../layouts/Header';
import { Sidebar } from '../layouts/Sidebar';
import { PropertiesPanel } from '../layouts/PropertiesPanel';
import { DesignerCanvas } from '../components/designer/DesignerCanvas';
import { RootState } from '../store';
import {
  hydrateFromAgentDto,
  mapGraphToDesignerDto,
  markSaved,
  setLoading,
  setSaveError
} from '../store/slices/designerSlice';

const TENANT_ID = 'default-tenant';

export default function DesignerPage() {
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const designerState = useSelector((state: RootState) => state.designer);
  const [isSaving, setIsSaving] = useState(false);

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

    load();
  }, [dispatch, id, isNew]);

  const onSave = async () => {
    setIsSaving(true);
    dispatch(setSaveError(null));

    const payload = mapGraphToDesignerDto(designerState);

    try {
      if (isNew) {
        const created = await AgentsApi.createAgent(TENANT_ID, payload);
        dispatch(hydrateFromAgentDto(created));
        navigate(`/designer/${created.id}`, { replace: true });
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
      <Header onSave={onSave} isSaving={isSaving} />
      <div className="content-area">
        <Sidebar />
        <main className="main-canvas">
          <ReactFlowProvider>
            <DesignerCanvas />
          </ReactFlowProvider>
        </main>
        <PropertiesPanel />
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
          display: flex;
          overflow: hidden;
        }
        .main-canvas {
          flex: 1;
          position: relative;
        }
      `}</style>
    </div>
  );
}

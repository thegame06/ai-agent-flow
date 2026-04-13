import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeft, PlayCircle } from 'lucide-react';
import { ExecutionsApi } from '../api/executions';
import { PreviewTimelineItem } from '../types/agent';
import { computeTotalTokens, mapStepToTimeline } from './sandbox/sandbox-utils';

const TENANT_ID = 'default-tenant';

export default function SandboxPage() {
  const { id: agentId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [prompt, setPrompt] = useState('Resume los pasos de verificación para un crédito personal de forma segura.');
  const [isRunning, setIsRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [executionId, setExecutionId] = useState<string | null>(null);
  const [status, setStatus] = useState<string>('Idle');
  const [finalResponse, setFinalResponse] = useState<string>('');
  const [timeline, setTimeline] = useState<PreviewTimelineItem[]>([]);

  const totalTokens = useMemo(
    () => computeTotalTokens(timeline),
    [timeline]
  );

  const runPreview = async () => {
    if (!agentId) return;

    setIsRunning(true);
    setError(null);
    setTimeline([]);
    setFinalResponse('');

    try {
      const preview = await ExecutionsApi.preview(TENANT_ID, agentId, prompt);
      setExecutionId(preview.executionId);
      setStatus(preview.status);
      setFinalResponse(preview.finalResponse ?? '');

      const details = await ExecutionsApi.getExecutionById(TENANT_ID, preview.executionId);
      setTimeline((details.steps ?? []).map(mapStepToTimeline));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Preview execution failed');
    } finally {
      setIsRunning(false);
    }
  };

  return (
    <div className="sandbox-page">
      <header className="sandbox-header">
        <button className="btn-secondary" onClick={() => navigate(`/designer/${agentId}`)}>
          <ArrowLeft size={16} /> Volver al diseñador
        </button>
        <h1>Sandbox Preview</h1>
      </header>

      <section className="prompt-panel">
        <label>Prompt de prueba</label>
        <textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} rows={4} />
        <button className="btn-primary" onClick={runPreview} disabled={isRunning || !agentId}>
          <PlayCircle size={16} /> {isRunning ? 'Ejecutando...' : 'Ejecutar preview'}
        </button>
      </section>

      {error && <p className="error">{error}</p>}

      <section className="summary-grid">
        <article>
          <h3>Status</h3>
          <p>{status}</p>
        </article>
        <article>
          <h3>Execution ID</h3>
          <p>{executionId ?? '-'}</p>
        </article>
        <article>
          <h3>Total tokens</h3>
          <p>{totalTokens}</p>
        </article>
      </section>

      <section className="final-response">
        <h3>Respuesta final</h3>
        <pre>{finalResponse || 'Sin respuesta final todavía.'}</pre>
      </section>

      <section>
        <h3>Timeline think / plan / act / observe</h3>
        <div className="timeline">
          {timeline.map((item) => (
            <article key={item.id} className="timeline-item">
              <div className="timeline-top">
                <strong>{item.type}</strong>
                <span>{item.durationMs} ms</span>
                <span>{item.tokensUsed ?? 0} tok</span>
              </div>
              <p>Tool: {item.toolName ?? '-'}</p>
              <details>
                <summary>Inspección de I/O</summary>
                <pre>input: {item.inputJson ?? '-'}</pre>
                <pre>output: {item.outputJson ?? '-'}</pre>
              </details>
              {!item.isSuccess && item.errorMessage && <p className="error">{item.errorMessage}</p>}
            </article>
          ))}
          {timeline.length === 0 && <p>Aún no hay pasos para mostrar.</p>}
        </div>
      </section>
    </div>
  );
}

import { useMemo, useState } from 'react';
import { useSelector } from 'react-redux';
import { RootState } from '../../store';
import { buildSimulationSteps } from '../../utils/studioValidation';

export const SimulationPanel = () => {
  const { nodes, edges } = useSelector((state: RootState) => state.designer.graph);
  const steps = useMemo(() => buildSimulationSteps(nodes, edges), [nodes, edges]);
  const [currentStep, setCurrentStep] = useState(0);

  const step = steps[currentStep];

  return (
    <section className="simulation-panel">
      <div className="top-row">
        <h4>Guided simulation</h4>
        <div className="actions">
          <button onClick={() => setCurrentStep((prev) => Math.max(prev - 1, 0))} disabled={currentStep === 0}>Prev</button>
          <button onClick={() => setCurrentStep((prev) => Math.min(prev + 1, Math.max(steps.length - 1, 0)))} disabled={currentStep >= steps.length - 1}>Next</button>
          <button onClick={() => setCurrentStep(0)} disabled={steps.length === 0}>Reset</button>
        </div>
      </div>

      {step ? (
        <>
          <div className="step-line">Step {currentStep + 1} / {steps.length}: <strong>{step.label}</strong></div>
          <div className="step-meta">
            <span>Tipo: <strong>{step.nodeType}</strong></span>
            <span>Transición: <strong>{step.transition ?? 'N/A'}</strong></span>
          </div>
          <div className="variables">
            {Object.entries(step.variables).map(([key, value]) => (
              <div key={key} className="var-row">
                <span>{key}</span>
                <code>{value}</code>
              </div>
            ))}
          </div>
          <div className="context-panel">
            <div className="context-title">Context inspection</div>
            <pre>{JSON.stringify(step.context, null, 2)}</pre>
          </div>
        </>
      ) : (
        <div className="empty">Add connections in the canvas to enable simulation.</div>
      )}

      <style>{`
        .simulation-panel { border-top: 1px solid var(--border-light); background: var(--bg-secondary); padding: 12px 16px; min-height: 150px; }
        .top-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
        .actions { display: flex; gap: 8px; }
        .actions button { background: var(--bg-tertiary); border: 1px solid var(--border-light); color: var(--fg-secondary); padding: 6px 10px; font-size: 12px; }
        .step-line { color: var(--fg-primary); font-size: 13px; margin-bottom: 8px; }
        .step-meta { display: flex; gap: 12px; color: var(--fg-secondary); font-size: 12px; margin-bottom: 8px; }
        .variables { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
        .var-row { display: flex; justify-content: space-between; background: var(--bg-primary); border: 1px solid var(--border-light); border-radius: 8px; padding: 6px 8px; font-size: 12px; }
        .context-panel { margin-top: 8px; border: 1px solid var(--border-light); border-radius: 8px; background: var(--bg-primary); }
        .context-title { font-size: 12px; padding: 6px 8px; border-bottom: 1px solid var(--border-light); color: var(--fg-secondary); }
        .context-panel pre { margin: 0; font-size: 11px; max-height: 120px; overflow: auto; padding: 8px; color: var(--fg-primary); }
        .empty { color: var(--fg-muted); font-size: 12px; }
      `}</style>
    </section>
  );
};

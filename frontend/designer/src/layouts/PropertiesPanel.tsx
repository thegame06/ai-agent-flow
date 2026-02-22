import { useSelector, useDispatch } from 'react-redux';
import { RootState } from '../store';
import { updateNodeData } from '../store/slices/designerSlice';
import { X, Info } from 'lucide-react';

export const PropertiesPanel = () => {
  const dispatch = useDispatch();
  const { nodes, selectedNodeId } = useSelector((state: RootState) => state.designer);
  const selectedNode = nodes.find((n: any) => n.id === selectedNodeId);

  if (!selectedNode) {
    return (
      <aside className="properties-panel empty">
        <div className="empty-state">
          <Info size={32} />
          <p>Select a node to edit its properties</p>
        </div>
        <style>{styles}</style>
      </aside>
    );
  }

  const handleChange = (field: string, value: any) => {
    dispatch(updateNodeData({ id: selectedNode.id, data: { [field]: value } }));
  };

  return (
    <aside className="properties-panel">
      <div className="panel-header">
        <h3>Node Configuration</h3>
        <button className="close-btn"><X size={18} /></button>
      </div>

      <div className="scroll-content">
        <div className="prop-section">
          <label>Display Name</label>
          <input 
            value={selectedNode.data.label as string} 
            onChange={(e) => handleChange('label', e.target.value)} 
          />
        </div>

        <div className="prop-section">
          <label>Internal Logic</label>
          <textarea rows={4} placeholder="Define instructions or logic here..." />
        </div>

        <div className="prop-section">
          <label>Risk Evaluation</label>
          <div className="risk-grid">
            {['Low', 'Med', 'High', 'Critical'].map(level => (
              <button 
                key={level} 
                className={`risk-btn ${level.toLowerCase()}`}
              >
                {level}
              </button>
            ))}
          </div>
        </div>
      </div>
      <style>{styles}</style>
    </aside>
  );
};

const styles = `
  .properties-panel {
    width: 320px;
    background: var(--bg-secondary);
    border-left: 1px solid var(--border-light);
    display: flex;
    flex-direction: column;
    z-index: 50;
  }
  .properties-panel.empty {
    justify-content: center;
    align-items: center;
    color: var(--fg-muted);
    text-align: center;
    padding: 40px;
  }
  .empty-state { display: flex; flex-direction: column; align-items: center; gap: 16px; }
  .empty-state p { font-size: 0.85rem; line-height: 1.5; }
  
  .panel-header {
    padding: 20px 24px;
    border-bottom: 1px solid var(--border-light);
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .panel-header h3 { font-size: 0.9rem; color: var(--fg-primary); font-weight: 600; }
  .close-btn { background: transparent; color: var(--fg-muted); }
  
  .scroll-content { padding: 24px; display: flex; flex-direction: column; gap: 24px; overflow-y: auto; }
  .prop-section { display: flex; flex-direction: column; gap: 8px; }
  .prop-section label { font-size: 0.75rem; color: var(--fg-secondary); font-weight: 600; text-transform: uppercase; }
  
  textarea { resize: none; font-size: 0.85rem; }
  
  .risk-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
  .risk-btn {
    padding: 8px;
    font-size: 0.75rem;
    background: var(--bg-tertiary);
    border: 1px solid var(--border-light);
    color: var(--fg-secondary);
  }
  .risk-btn.low:hover { border-color: var(--success); color: var(--success); }
  .risk-btn.high:hover { border-color: var(--danger); color: var(--danger); }
`;

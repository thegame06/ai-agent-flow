import { DragEvent } from 'react';
import { Plus, Search } from 'lucide-react';

const NODE_TYPES = [
  { type: 'start', label: 'Inicio', color: '#10b981' },
  { type: 'ai_agent', label: 'AI Agent', color: '#6366f1' },
  { type: 'message', label: 'Mensaje', color: '#0ea5e9' },
  { type: 'condition', label: 'Condición', color: '#f59e0b' },
  { type: 'api', label: 'API', color: '#22c55e' },
  { type: 'db', label: 'DB', color: '#3b82f6' },
  { type: 'webhook', label: 'Webhook', color: '#ec4899' },
  { type: 'human', label: 'Humano', color: '#8b5cf6' },
  { type: 'end', label: 'Fin', color: '#ef4444' }
];

export const Sidebar = () => {
  const onDragStart = (event: DragEvent, nodeType: string, label: string) => {
    event.dataTransfer.setData('application/reactflow', JSON.stringify({ nodeType, label }));
    event.dataTransfer.effectAllowed = 'move';
  };

  return (
    <aside className="designer-sidebar">
      <div className="search-box">
        <Search size={14} className="search-icon" />
        <input type="text" placeholder="Search nodes..." />
      </div>

      <div className="nodes-container">
        <div className="group-label">
          <span>Nodos estándar</span>
        </div>
        <div className="group-items">
          {NODE_TYPES.map(item => (
            <div
              key={item.label}
              className="draggable-item"
              draggable
              onDragStart={(e) => onDragStart(e, item.type, item.label)}
            >
              <div className="item-dot" style={{ background: item.color }} />
              <span className="item-label">{item.label}</span>
              <Plus size={14} className="add-icon" />
            </div>
          ))}
        </div>
      </div>

      <style>{`
        .designer-sidebar {
          width: 260px;
          background: var(--bg-secondary);
          border: 1px solid var(--border-light);
          border-radius: 12px;
          display: flex;
          flex-direction: column;
          padding: 14px;
          gap: 12px;
          box-shadow: var(--shadow-md);
        }
        .search-box {
          position: relative;
          display: flex;
          align-items: center;
        }
        .search-icon {
          position: absolute;
          left: 12px;
          color: var(--fg-muted);
        }
        .search-box input {
          width: 100%;
          padding-left: 36px;
          font-size: 0.85rem;
          background: var(--bg-primary);
        }
        .group-label {
          display: flex;
          align-items: center;
          gap: 10px;
          color: var(--fg-secondary);
          font-size: 0.75rem;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.05em;
          margin-bottom: 12px;
        }
        .group-items {
          display: flex;
          flex-direction: column;
          gap: 8px;
        }
        .draggable-item {
          background: var(--bg-tertiary);
          border: 1px solid var(--border-light);
          padding: 10px 14px;
          border-radius: var(--radius-md);
          display: flex;
          align-items: center;
          gap: 12px;
          cursor: grab;
          transition: all 0.2s ease;
        }
        .draggable-item:hover {
          background: var(--border-light);
          border-color: var(--accent-primary);
          transform: translateX(4px);
        }
        .item-dot { width: 6px; height: 6px; border-radius: 50%; }
        .item-label { font-size: 0.85rem; color: var(--fg-primary); flex: 1; }
        .add-icon { color: var(--fg-muted); opacity: 0; transition: opacity 0.2s; }
        .draggable-item:hover .add-icon { opacity: 1; }
      `}</style>
    </aside>
  );
};

import { DragEvent } from 'react';
import { Cpu, Database, ShieldCheck, Plus, Search } from 'lucide-react';

const NODE_TYPES = [
  { 
    group: 'Cognition', 
    icon: <Cpu size={16} />, 
    items: [
      { type: 'think', label: 'Reasoning Step', color: '#6366f1' },
      { type: 'output', label: 'Final Answer', color: '#10b981' }
    ] 
  },
  { 
    group: 'Capabilities', 
    icon: <Database size={16} />, 
    items: [
      { type: 'tool', label: 'Tool Invocation', color: '#f59e0b' },
      { type: 'memory', label: 'Knowledge Lookup', color: '#3b82f6' }
    ] 
  },
  { 
    group: 'Governance', 
    icon: <ShieldCheck size={16} />, 
    items: [
      { type: 'policy', label: 'Security Check', color: '#ef4444' },
      { type: 'human', label: 'Manual Review', color: '#8b5cf6' }
    ] 
  }
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
        {NODE_TYPES.map(group => (
          <div key={group.group} className="node-group">
            <div className="group-label">
              {group.icon}
              <span>{group.group}</span>
            </div>
            <div className="group-items">
              {group.items.map(item => (
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
        ))}
      </div>

      <style>{`
        .designer-sidebar {
          width: 280px;
          background: var(--bg-secondary);
          border-right: 1px solid var(--border-light);
          display: flex;
          flex-direction: column;
          padding: 20px;
          gap: 24px;
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

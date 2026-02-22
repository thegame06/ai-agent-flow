import { useSelector } from 'react-redux';
import { Zap, Save, Play, Settings } from 'lucide-react';
import { RootState } from '../store';

export const Header = () => {
  const { agentName, version, isDirty } = useSelector((state: RootState) => state.designer);

  return (
    <header className="app-header">
      <div className="header-left">
        <div className="logo-container">
          <div className="logo-icon"><Zap size={20} fill="#6366f1" /></div>
          <span className="logo-text">AgentFlow <span className="logo-accent">Designer</span></span>
        </div>
        <div className="v-divider" />
        <div className="agent-meta">
          <span className="agent-title">{agentName}</span>
          <span className="badge">{version}</span>
          {isDirty && <span className="dirty-indicator">Draft</span>}
        </div>
      </div>
      
      <div className="header-actions">
        <button className="btn-icon"><Settings size={18} /></button>
        <button className="btn-secondary">
          <Save size={18} />
          <span>Save</span>
        </button>
        <button className="btn-primary">
          <Play size={18} fill="currentColor" />
          <span>Deploy</span>
        </button>
      </div>

      <style>{`
        .app-header {
          height: 64px;
          background: var(--bg-secondary);
          border-bottom: 1px solid var(--border-light);
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 0 24px;
          z-index: 100;
          backdrop-filter: var(--glass);
        }
        .header-left { display: flex; align-items: center; gap: 20px; }
        .logo-container { display: flex; align-items: center; gap: 10px; cursor: pointer; }
        .logo-icon { background: var(--accent-soft); padding: 6px; border-radius: 8px; display: flex; }
        .logo-text { color: var(--fg-primary); font-family: var(--font-display); font-size: 1.1rem; font-weight: 600; }
        .logo-accent { color: var(--accent-primary); opacity: 0.9; }
        .v-divider { width: 1px; height: 24px; background: var(--border-light); }
        .agent-meta { display: flex; align-items: center; gap: 12px; }
        .agent-title { color: var(--fg-primary); font-weight: 500; font-size: 0.9rem; }
        .badge { background: var(--bg-tertiary); color: var(--fg-secondary); font-size: 0.7rem; padding: 2px 8px; border-radius: 99px; border: 1px solid var(--border-light); }
        .dirty-indicator { color: var(--warning); font-size: 0.7rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; }
        .header-actions { display: flex; align-items: center; gap: 12px; }
        
        .btn-icon { background: transparent; color: var(--fg-secondary); padding: 8px; }
        .btn-icon:hover { color: var(--fg-primary); background: var(--border-light); }
        
        .btn-secondary {
          background: var(--bg-tertiary);
          color: var(--fg-primary);
          padding: 8px 16px;
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 0.85rem;
          border: 1px solid var(--border-light);
        }
        .btn-secondary:hover { background: var(--border-strong); }
        
        .btn-primary {
          background: var(--accent-primary);
          color: white;
          padding: 8px 16px;
          display: flex;
          align-items: center;
          gap: 8px;
          font-size: 0.85rem;
          box-shadow: 0 4px 12px rgba(99, 102, 241, 0.3);
        }
        .btn-primary:hover { background: var(--accent-hover); transform: translateY(-1px); }
      `}</style>
    </header>
  );
};

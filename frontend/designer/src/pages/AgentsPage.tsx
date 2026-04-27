import { Plus, Search, MoreVertical, Play, Edit3, Trash2 } from 'lucide-react';
import { useNavigate } from 'react-router-dom';

const MOCK_AGENTS = [
  { id: '1', name: 'LoanEligibilityAgent', version: '1.2.0', status: 'Deployed', lastModified: '2026-02-19' },
  { id: '2', name: 'FraudDetectionBot', version: '0.9.5', status: 'Draft', lastModified: '2026-02-20' },
  { id: '3', name: 'SupportRouter', version: '2.1.0', status: 'Deployed', lastModified: '2026-02-15' },
];

export default function AgentsPage() {
  const navigate = useNavigate();

  return (
    <div className="page-container">
      <header className="page-header">
        <div>
          <h1>Agent Registry</h1>
          <p>Manage and deploy your autonomous AI agents</p>
        </div>
        <button className="btn-primary" onClick={() => navigate('/studio/new')}>
          <Plus size={18} />
          Create New Agent
        </button>
      </header>

      <div className="toolbar">
        <div className="search-bar">
          <Search size={18} />
          <input type="text" placeholder="Search agents by name, version or status..." />
        </div>
      </div>

      <div className="agents-grid">
        {MOCK_AGENTS.map(agent => (
          <div key={agent.id} className="agent-card animate-fade">
            <div className="card-header">
              <span className={`status-pill ${agent.status.toLowerCase()}`}>{agent.status}</span>
              <button className="btn-icon"><MoreVertical size={18} /></button>
            </div>
            <div className="card-body">
              <h3>{agent.name}</h3>
              <p className="version">Version {agent.version}</p>
              <div className="meta">
                <span>Modified: {agent.lastModified}</span>
              </div>
            </div>
            <div className="card-footer">
              <button className="btn-action" onClick={() => navigate(`/studio/${agent.id}`)}>
                <Edit3 size={16} />
                Edit
              </button>
              <button className="btn-action" onClick={() => navigate(`/sandbox/${agent.id}`)}>
                <Play size={16} />
                Test
              </button>
              <button className="btn-action danger">
                <Trash2 size={16} />
              </button>
            </div>
          </div>
        ))}
      </div>

      <style>{`
        .page-container {
          padding: 40px;
          max-width: 1200px;
          margin: 0 auto;
          overflow-y: auto;
          height: 100vh;
        }
        .page-header {
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
          margin-bottom: 40px;
        }
        .page-header h1 { font-size: 2rem; margin-bottom: 8px; }
        .page-header p { color: var(--fg-secondary); }
        
        .toolbar { margin-bottom: 32px; }
        .search-bar {
          background: var(--bg-secondary);
          border: 1px solid var(--border-light);
          border-radius: var(--radius-md);
          display: flex;
          align-items: center;
          padding: 0 16px;
          max-width: 500px;
        }
        .search-bar input {
          background: transparent;
          border: none;
          width: 100%;
          outline: none !important;
        }
        .search-bar svg { color: var(--fg-muted); }

        .agents-grid {
          display: grid;
          grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
          gap: 24px;
        }
        .agent-card {
          background: var(--bg-secondary);
          border: 1px solid var(--border-light);
          border-radius: var(--radius-lg);
          padding: 24px;
          transition: all 0.3s ease;
        }
        .agent-card:hover { border-color: var(--accent-primary); transform: translateY(-4px); box-shadow: var(--shadow-lg); }
        
        .card-header { display: flex; justify-content: space-between; margin-bottom: 16px; }
        .status-pill { font-size: 0.7rem; font-weight: 600; padding: 4px 10px; border-radius: 99px; text-transform: uppercase; }
        .status-pill.deployed { background: var(--accent-soft); color: var(--accent-primary); }
        .status-pill.draft { background: var(--bg-tertiary); color: var(--fg-muted); }
        
        .card-body h3 { font-size: 1.25rem; margin-bottom: 4px; }
        .version { color: var(--fg-muted); font-size: 0.85rem; margin-bottom: 16px; }
        .meta { font-size: 0.8rem; color: var(--fg-secondary); }
        
        .card-footer { border-top: 1px solid var(--border-light); margin-top: 24px; padding-top: 20px; display: flex; gap: 8px; }
        .btn-action {
          flex: 1;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 8px;
          background: var(--bg-tertiary);
          color: var(--fg-primary);
          padding: 8px;
          font-size: 0.85rem;
        }
        .btn-action:hover { background: var(--border-light); }
        .btn-action.danger { flex: 0 0 40px; color: var(--danger); }
        .btn-action.danger:hover { background: rgba(239, 68, 68, 0.1); }
        
        .btn-primary {
          background: var(--accent-primary);
          color: white;
          padding: 12px 24px;
          display: flex;
          align-items: center;
          gap: 10px;
          border-radius: var(--radius-md);
          font-weight: 600;
        }
      `}</style>
    </div>
  );
}

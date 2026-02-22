import { NavLink, Outlet } from 'react-router-dom';
import { LayoutDashboard, Users, Cpu, ShieldCheck, Activity, Settings, LogOut } from 'lucide-react';

export default function AppLayout() {
  return (
    <div className="main-shell">
      <aside className="global-sidebar">
        <div className="sidebar-brand">
          <div className="brand-logo">A</div>
          <span>AgentFlow</span>
        </div>

        <nav className="nav-group">
          <div className="nav-label">Management</div>
          <NavLink to="/dashboard" className={({isActive}) => `nav-item ${isActive ? 'active' : ''}`}>
            <LayoutDashboard size={20} />
            <span>Dashboard</span>
          </NavLink>
          <NavLink to="/agents" className={({isActive}) => `nav-item ${isActive ? 'active' : ''}`}>
            <Users size={20} />
            <span>Agent Registry</span>
          </NavLink>
          <NavLink to="/tools" className={({isActive}) => `nav-item ${isActive ? 'active' : ''}`}>
            <Cpu size={20} />
            <span>Tools Library</span>
          </NavLink>
        </nav>

        <nav className="nav-group">
          <div className="nav-label">Governance</div>
          <NavLink to="/policies" className={({isActive}) => `nav-item ${isActive ? 'active' : ''}`}>
            <ShieldCheck size={20} />
            <span>Policy Sets</span>
          </NavLink>
          <NavLink to="/monitoring" className={({isActive}) => `nav-item ${isActive ? 'active' : ''}`}>
            <Activity size={20} />
            <span>Monitoring</span>
          </NavLink>
        </nav>

        <div className="sidebar-footer">
          <NavLink to="/settings" className="nav-item">
            <Settings size={20} />
            <span>Settings</span>
          </NavLink>
          <button className="nav-item logout">
            <LogOut size={20} />
            <span>Logout</span>
          </button>
        </div>
      </aside>

      <main className="content-root">
        <Outlet />
      </main>

      <style>{`
        .main-shell {
          display: flex;
          height: 100vh;
          width: 100vw;
          background: var(--bg-primary);
          color: var(--fg-primary);
        }
        .global-sidebar {
          width: 260px;
          background: var(--bg-secondary);
          border-right: 1px solid var(--border-light);
          display: flex;
          flex-direction: column;
          padding: 24px 16px;
        }
        .sidebar-brand {
          display: flex;
          align-items: center;
          gap: 12px;
          margin-bottom: 40px;
          padding: 0 8px;
        }
        .brand-logo {
          background: var(--accent-primary);
          color: white;
          width: 32px;
          height: 32px;
          display: flex;
          align-items: center;
          justify-content: center;
          border-radius: 8px;
          font-weight: 800;
          font-family: var(--font-display);
        }
        .sidebar-brand span {
          font-family: var(--font-display);
          font-weight: 700;
          font-size: 1.25rem;
          letter-spacing: -0.02em;
        }

        .nav-group { margin-bottom: 32px; }
        .nav-label {
          font-size: 0.65rem;
          text-transform: uppercase;
          letter-spacing: 0.1em;
          color: var(--fg-muted);
          margin-bottom: 12px;
          padding-left: 12px;
          font-weight: 600;
        }

        .nav-item {
          display: flex;
          align-items: center;
          gap: 12px;
          padding: 12px;
          color: var(--fg-secondary);
          text-decoration: none;
          border-radius: var(--radius-md);
          font-size: 0.9rem;
          font-weight: 500;
          transition: all 0.2s ease;
          margin-bottom: 4px;
        }
        .nav-item:hover {
          background: var(--accent-soft);
          color: var(--accent-primary);
        }
        .nav-item.active {
          background: var(--accent-primary);
          color: white;
        }
        .nav-item svg { min-width: 20px; }

        .sidebar-footer {
          margin-top: auto;
          border-top: 1px solid var(--border-light);
          padding-top: 20px;
        }
        .logout { color: var(--danger); width: 100%; border: none; background: transparent; cursor: pointer; text-align: left; }
        .logout:hover { background: rgba(239, 68, 68, 0.1); color: var(--danger); }

        .content-root {
          flex: 1;
          overflow: hidden;
          position: relative;
        }
      `}</style>
    </div>
  );
}

import { ReactFlowProvider } from '@xyflow/react';
import { Header } from '../layouts/Header';
import { Sidebar } from '../layouts/Sidebar';
import { PropertiesPanel } from '../layouts/PropertiesPanel';
import { DesignerCanvas } from '../components/designer/DesignerCanvas';

export default function DesignerPage() {
  return (
    <div className="app-layout">
      <Header />
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

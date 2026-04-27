import { Provider } from 'react-redux';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { store } from './store';
import AppLayout from './layouts/AppLayout';
import DesignerPage from './pages/DesignerPage';
import AgentsPage from './pages/AgentsPage';
import PoliciesPage from './pages/PoliciesPage';
import PolicyDetailsPage from './pages/PolicyDetailsPage';
import SandboxPage from './pages/SandboxPage';
import '@xyflow/react/dist/style.css';

const Placeholder = ({ title }: { title: string }) => (
  <div style={{ padding: 40 }}>
    <h1>{title}</h1>
    <p>Module implementation in progress...</p>
  </div>
);

function ApplicationRouter() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/agents" element={<AgentsPage />} />
          <Route path="/dashboard" element={<Placeholder title="Dashboard" />} />
          <Route path="/tools" element={<Placeholder title="Tools Library" />} />
          <Route path="/policies" element={<PoliciesPage />} />
          <Route path="/policies/:id" element={<PolicyDetailsPage />} />
          <Route path="/monitoring" element={<Placeholder title="Monitoring" />} />
          <Route path="/settings" element={<Placeholder title="Settings" />} />
        </Route>

        <Route path="/studio/:id" element={<DesignerPage />} />
        <Route path="/designer/:id" element={<DesignerPage />} />
        <Route path="/sandbox/:id" element={<SandboxPage />} />

        <Route path="/" element={<Navigate to="/agents" replace />} />
      </Routes>
    </BrowserRouter>
  );
}

export default function App() {
  return (
    <Provider store={store}>
      <ApplicationRouter />
    </Provider>
  );
}

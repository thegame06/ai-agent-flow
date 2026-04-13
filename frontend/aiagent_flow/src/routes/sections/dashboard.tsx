import type { RouteObject } from 'react-router';

import { Outlet } from 'react-router';
import { lazy, Suspense } from 'react';

import { CONFIG } from 'src/global-config';
import { DashboardLayout } from 'src/layouts/dashboard';

import { LoadingScreen } from 'src/components/loading-screen';

import { AuthGuard } from 'src/auth/guard';

import { usePathname } from '../hooks';

// ----------------------------------------------------------------------
// Lazy-loaded pages
// ----------------------------------------------------------------------

const OverviewPage = lazy(() => import('src/aiagentflow/pages/overview/OverviewPage'));
const AgentsPage = lazy(() => import('src/aiagentflow/pages/agents/AgentsPage'));
const AgentDetailPage = lazy(() => import('src/aiagentflow/pages/agents/Detail/AgentDetailPage'));
const AgentDesignerPage = lazy(() => import('src/aiagentflow/pages/agents/Designer/AgentDesignerPage'));
const ChatPage = lazy(() => import('src/aiagentflow/pages/ChatPage'));
const ExecutionsPage = lazy(() => import('src/aiagentflow/pages/executions/ExecutionsPage'));
const ExecutionDetailPage = lazy(() => import('src/aiagentflow/pages/executions/Detail/ExecutionDetailPage'));
const CheckpointsPage = lazy(() => import('src/aiagentflow/pages/checkpoints/CheckpointsPage'));
const ToolsPage = lazy(() => import('src/aiagentflow/pages/tools/ToolsPage'));
const MarketplacePage = lazy(() => import('src/aiagentflow/pages/tools/MarketplacePage'));
const PoliciesPage = lazy(() => import('src/aiagentflow/pages/policies/PoliciesPage'));
const AuditLogPage = lazy(() => import('src/aiagentflow/pages/audit/AuditPage'));
const ModelsPage = lazy(() => import('src/aiagentflow/pages/models/ModelsPage'));
const AuthProfilesPage = lazy(() => import('src/aiagentflow/pages/system/AuthProfilesPage'));
const McpPage = lazy(() => import('src/aiagentflow/pages/system/McpPage'));
const ChannelsPage = lazy(() => import('src/aiagentflow/pages/channels/ChannelsPage'));
const SegmentRoutingPage = lazy(() => import('src/aiagentflow/pages/system/SegmentRoutingPage'));
const FeatureFlagsPage = lazy(() => import('src/aiagentflow/pages/system/FeatureFlagsPage'));
const SettingsPage = lazy(() => import('src/aiagentflow/pages/settings/SettingsPage'));
const ManagerOrchestrationPage = lazy(() => import('src/aiagentflow/pages/orchestration/ManagerOrchestrationPage'));
const ThreadsPage = lazy(() => import('src/aiagentflow/pages/threads/ThreadsPage'));
const EvaluationsPage = lazy(() => import('src/aiagentflow/pages/evaluations/EvaluationsPage'));

// ----------------------------------------------------------------------

function SuspenseOutlet() {
  const pathname = usePathname();
  return (
    <Suspense key={pathname} fallback={<LoadingScreen />}>
      <Outlet />
    </Suspense>
  );
}

const dashboardLayout = () => (
  <DashboardLayout>
    <SuspenseOutlet />
  </DashboardLayout>
);

export const dashboardRoutes: RouteObject[] = [
  {
    path: 'dashboard',
    element: CONFIG.auth.skip ? dashboardLayout() : <AuthGuard>{dashboardLayout()}</AuthGuard>,
    children: [
      { element: <OverviewPage />, index: true },
      { path: 'overview', element: <OverviewPage /> },
      { path: 'agents', element: <AgentsPage /> },
      { path: 'agents/:id', element: <AgentDetailPage /> },
      { path: 'agents/:agentId/chat', element: <ChatPage /> },
      { path: 'agents/designer', element: <AgentDesignerPage /> },
      { path: 'agents/designer/:agentId', element: <AgentDesignerPage /> },
      { path: 'executions', element: <ExecutionsPage /> },
      { path: 'executions/:executionId', element: <ExecutionDetailPage /> },
      { path: 'checkpoints', element: <CheckpointsPage /> },
      { path: 'tools', element: <ToolsPage /> },
      { path: 'marketplace', element: <MarketplacePage /> },
      { path: 'orchestration', element: <ManagerOrchestrationPage /> },
      { path: 'threads', element: <ThreadsPage /> },
      { path: 'evaluations', element: <EvaluationsPage /> },
      {
        path: 'governance',
        children: [
          { element: <PoliciesPage />, index: true },
          { path: 'policies', element: <PoliciesPage /> },
          { path: 'audit', element: <AuditLogPage /> },
        ],
      },
      {
        path: 'system',
        children: [
          { element: <ModelsPage />, index: true },
          { path: 'models', element: <ModelsPage /> },
          { path: 'auth-profiles', element: <AuthProfilesPage /> },
          { path: 'mcp', element: <McpPage /> },
          { path: 'channels', element: <ChannelsPage /> },
          { path: 'segment-routing', element: <SegmentRoutingPage /> },
          { path: 'feature-flags', element: <FeatureFlagsPage /> },
          { path: 'settings', element: <SettingsPage /> },
        ],
      },
    ],
  },
];

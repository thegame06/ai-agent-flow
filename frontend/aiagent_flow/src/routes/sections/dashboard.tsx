import type { RouteObject } from 'react-router';

import { lazy, Suspense } from 'react';
import { Outlet, Navigate } from 'react-router';

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
const AgentsListDetailPage = lazy(() => import('src/aiagentflow/pages/agents/AgentsListDetailPage'));
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
const OperationsPage = lazy(() => import('src/aiagentflow/pages/operations/OperationsPage'));
const ModelsPage = lazy(() => import('src/aiagentflow/pages/models/ModelsPage'));
const AuthProfilesPage = lazy(() => import('src/aiagentflow/pages/system/AuthProfilesPage'));
const McpPage = lazy(() => import('src/aiagentflow/pages/system/McpPage'));
const ChannelsPage = lazy(() => import('src/aiagentflow/pages/channels/ChannelsPage'));
const FeatureFlagsPage = lazy(() => import('src/aiagentflow/pages/system/FeatureFlagsPage'));
const SettingsPage = lazy(() => import('src/aiagentflow/pages/settings/SettingsPage'));
const AgentContextSettingsPage = lazy(() => import('src/aiagentflow/pages/settings/AgentContextSettingsPage'));
const SettingsLayoutPage = lazy(() => import('src/aiagentflow/pages/settings/SettingsLayoutPage'));
const WorkforcePage = lazy(() => import('src/aiagentflow/pages/system/WorkforcePage'));
const ThreadsPage = lazy(() => import('src/aiagentflow/pages/threads/ThreadsPage'));
const CommerceAdminPage = lazy(() => import('src/aiagentflow/pages/commerce/CommerceAdminPage'));
const CampaignsPage = lazy(() => import('src/aiagentflow/pages/campaigns/CampaignsPage'));
const EvaluationsPage = lazy(() => import('src/aiagentflow/pages/evaluations/EvaluationsPage'));
const KycPaymentsPage = lazy(() => import('src/aiagentflow/pages/kyc/KycPaymentsPage'));
const WorkflowsPage = lazy(() => import('src/aiagentflow/pages/workflows/WorkflowsPage'));
const AutomationWizardPage = lazy(() => import('src/aiagentflow/pages/automation/AutomationWizardPage'));
const RuntimeStudioPage = lazy(() => import('src/aiagentflow/pages/runtime/RuntimeStudioPage'));
const RuntimeTestStudioPage = lazy(() => import('src/aiagentflow/pages/runtime/RuntimeTestStudioPage'));

// Intent Routing Pages
const IntentsPage = lazy(() => import('src/aiagentflow/pages/intents/IntentsPage'));
const PlaygroundPage = lazy(() => import('src/aiagentflow/pages/intents/PlaygroundPage'));
const InboxPage = lazy(() => import('src/aiagentflow/pages/intents/InboxPage'));

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
      { element: <Navigate to="/dashboard/annonai" replace />, index: true },
      { path: 'annonai', element: <AutomationWizardPage /> },
      { path: 'overview', element: <OverviewPage /> },
      { path: 'agents', element: <AgentsPage /> },
      { path: 'agents/list-detail', element: <AgentsListDetailPage /> },
      { path: 'agents/:id', element: <AgentDetailPage /> },
      { path: 'agents/:agentId/chat', element: <ChatPage /> },
      { path: 'agents/designer', element: <AgentDesignerPage /> },
      { path: 'agents/designer/:agentId', element: <AgentDesignerPage /> },
      { path: 'executions', element: <ExecutionsPage /> },
      { path: 'executions/:executionId', element: <ExecutionDetailPage /> },
      { path: 'checkpoints', element: <CheckpointsPage /> },
      { path: 'tools', element: <ToolsPage /> },
      { path: 'marketplace', element: <MarketplacePage /> },
      { path: 'orchestration', element: <Navigate to="/dashboard/intents" replace /> },
      { path: 'threads', element: <ThreadsPage /> },
      { path: 'commerce', element: <CommerceAdminPage /> },
      { path: 'campaigns', element: <CampaignsPage /> },
      { path: 'evaluations', element: <EvaluationsPage /> },
      { path: 'kyc-payments', element: <KycPaymentsPage /> },
      { path: 'studio/workflows', element: <WorkflowsPage /> },
      { path: 'runtime/:runtimeKind', element: <RuntimeStudioPage /> },
      { path: 'runtime/:runtimeKind/test-studio', element: <RuntimeTestStudioPage /> },
      { path: 'automation/new', element: <AutomationWizardPage /> },
      {
        path: 'settings',
        element: <SettingsLayoutPage />,
        children: [
          { element: <Navigate to="/dashboard/settings/general" replace />, index: true },
          { path: 'general', element: <SettingsPage /> },
          { path: 'agent-contexts', element: <AgentContextSettingsPage /> },
          { path: 'models', element: <ModelsPage /> },
          { path: 'auth-profiles', element: <AuthProfilesPage /> },
          { path: 'feature-flags', element: <FeatureFlagsPage /> },
          { path: 'workforce', element: <WorkforcePage /> },
          { path: 'policies', element: <PoliciesPage /> },
          { path: 'audit', element: <AuditLogPage /> },
          { path: 'operations', element: <OperationsPage /> },
        ],
      },
      // Intent Routing
      { path: 'intents', element: <IntentsPage /> },
      { path: 'intents/playground', element: <PlaygroundPage /> },
      { path: 'inbox', element: <InboxPage /> },
      {
        path: 'governance',
        children: [
          { element: <Navigate to="/dashboard/settings/policies" replace />, index: true },
          { path: 'policies', element: <Navigate to="/dashboard/settings/policies" replace /> },
          { path: 'audit', element: <Navigate to="/dashboard/settings/audit" replace /> },
          { path: 'operations', element: <Navigate to="/dashboard/settings/operations" replace /> },
        ],
      },
      {
        path: 'system',
        children: [
          { element: <Navigate to="/dashboard/settings/models" replace />, index: true },
          { path: 'models', element: <Navigate to="/dashboard/settings/models" replace /> },
          { path: 'auth-profiles', element: <Navigate to="/dashboard/settings/auth-profiles" replace /> },
          { path: 'mcp', element: <McpPage /> },
          { path: 'channels', element: <ChannelsPage /> },
          { path: 'feature-flags', element: <Navigate to="/dashboard/settings/feature-flags" replace /> },
          { path: 'settings', element: <Navigate to="/dashboard/settings/general" replace /> },
          { path: 'workforce', element: <Navigate to="/dashboard/settings/workforce" replace /> },
        ],
      },
    ],
  },
];

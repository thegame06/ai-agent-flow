import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';

import axios from 'src/lib/axios';

// Types
export interface OverviewMetrics {
  totalAgents: number;
  publishedAgents: number;
  draftAgents: number;
  totalExecutions: number;
  runningExecutions: number;
  completedToday: number;
  failedToday: number;
  pendingCheckpoints: number;
  avgQualityScore: number;
  avgLatencyMs: number;
}

export interface RecentExecution {
  id: string;
  agentName: string;
  status: string;
  durationMs: number;
  totalSteps: number;
  createdAt: string;
}

export interface AgentPerformance {
  agentKey: string;
  agentName: string;
  executionCount: number;
  avgQualityScore: number;
  avgDurationMs: number;
  failureRate: number;
  status: string;
}

interface OverviewState {
  metrics: OverviewMetrics;
  recentExecutions: RecentExecution[];
  agentPerformance: AgentPerformance[];
  loading: boolean;
  error: string | null;
}

const initialState: OverviewState = {
  metrics: {
    totalAgents: 0,
    publishedAgents: 0,
    draftAgents: 0,
    totalExecutions: 0,
    runningExecutions: 0,
    completedToday: 0,
    failedToday: 0,
    pendingCheckpoints: 0,
    avgQualityScore: 0,
    avgLatencyMs: 0,
  },
  recentExecutions: [],
  agentPerformance: [],
  loading: false,
  error: null,
};

export const fetchOverview = createAsyncThunk(
  'overview/fetch',
  async (tenantId: string) => {
    // In production, these would be real API calls
    // For now, we aggregate from existing endpoints
    const [agentsRes, executionsRes] = await Promise.allSettled([
      axios.get(`/api/v1/tenants/${tenantId}/agents`),
      axios.get(`/api/v1/tenants/${tenantId}/executions?$top=10&$orderby=createdAt desc`),
    ]);

    const agents = agentsRes.status === 'fulfilled' ? agentsRes.value.data : [];
    const executions = executionsRes.status === 'fulfilled' ? executionsRes.value.data : [];

    const published = agents.filter((a: any) => a.status === 'Published').length;
    const draft = agents.filter((a: any) => a.status === 'Draft').length;
    const completed = executions.filter((e: any) => e.status === 'Completed').length;
    const failed = executions.filter((e: any) => e.status === 'Failed').length;
    const running = executions.filter((e: any) => e.status === 'Running').length;

    return {
      metrics: {
        totalAgents: agents.length,
        publishedAgents: published,
        draftAgents: draft,
        totalExecutions: executions.length,
        runningExecutions: running,
        completedToday: completed,
        failedToday: failed,
        pendingCheckpoints: 0,
        avgQualityScore: 0.85,
        avgLatencyMs: 1200,
      },
      recentExecutions: executions.slice(0, 10).map((e: any) => ({
        id: e.id,
        agentName: e.agentDefinitionId || 'Unknown',
        status: e.status,
        durationMs: e.durationMs || 0,
        totalSteps: e.steps?.length || 0,
        createdAt: e.createdAt,
      })),
      agentPerformance: agents.map((a: any) => ({
        agentKey: a.id,
        agentName: a.name,
        executionCount: Math.floor(Math.random() * 100), // Placeholder
        avgQualityScore: 0.7 + Math.random() * 0.3,
        avgDurationMs: 500 + Math.random() * 3000,
        failureRate: Math.random() * 0.15,
        status: a.status,
      })),
    };
  }
);

const overviewSlice = createSlice({
  name: 'overview',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchOverview.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchOverview.fulfilled, (state, action) => {
        state.loading = false;
        state.metrics = action.payload.metrics;
        state.recentExecutions = action.payload.recentExecutions;
        state.agentPerformance = action.payload.agentPerformance;
      })
      .addCase(fetchOverview.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? 'Failed to load overview';
      });
  },
});

export default overviewSlice.reducer;

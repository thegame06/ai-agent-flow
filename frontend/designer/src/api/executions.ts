import { AgentExecutionDetailsDto, PreviewExecutionResponse } from '../types/agent';
import { fetchWithAuth } from './client';

export const ExecutionsApi = {
  preview: async (tenantId: string, agentId: string, message: string): Promise<PreviewExecutionResponse> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/agents/${agentId}/preview`, {
      method: 'POST',
      body: JSON.stringify({ message })
    });

    return response.json();
  },

  getExecutionById: async (tenantId: string, executionId: string): Promise<AgentExecutionDetailsDto> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/executions/${executionId}`);
    return response.json();
  }
};

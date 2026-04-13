import { AgentDesignerDto } from '../types/agent';
import { fetchWithAuth } from './client';

export const AgentsApi = {
  getAgent: async (tenantId: string, agentId: string): Promise<AgentDesignerDto & { id: string }> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/agents/${agentId}`);
    return response.json();
  },

  createAgent: async (tenantId: string, payload: AgentDesignerDto): Promise<AgentDesignerDto & { id: string }> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/agents`, {
      method: 'POST',
      body: JSON.stringify(payload)
    });

    return response.json();
  },

  updateAgent: async (tenantId: string, agentId: string, payload: AgentDesignerDto): Promise<AgentDesignerDto & { id: string }> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/agents/${agentId}`, {
      method: 'PUT',
      body: JSON.stringify(payload)
    });

    return response.json();
  }
};

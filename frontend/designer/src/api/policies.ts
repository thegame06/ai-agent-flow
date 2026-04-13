import { PolicySetDefinition } from '../types/policies';
import { fetchWithAuth } from './client';

export const PoliciesApi = {
  getPolicies: async (tenantId: string): Promise<PolicySetDefinition[]> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/policies`);
    return response.json();
  },

  getPolicySet: async (tenantId: string, policySetId: string): Promise<PolicySetDefinition> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/policies/${policySetId}`);
    return response.json();
  },

  updatePolicySet: async (tenantId: string, policySetId: string, policySet: Partial<PolicySetDefinition>): Promise<PolicySetDefinition> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/policies/${policySetId}`, {
      method: 'PUT',
      body: JSON.stringify(policySet)
    });
    return response.json();
  },

  createPolicySet: async (tenantId: string, policySet: Partial<PolicySetDefinition>): Promise<PolicySetDefinition> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/policies`, {
      method: 'POST',
      body: JSON.stringify(policySet)
    });
    return response.json();
  }
};

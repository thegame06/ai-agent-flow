import { PolicySetDefinition } from '../types/policies';

// Base API URL configuration
const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5183/api/v1';

async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  const token = localStorage.getItem('auth_token'); // Assuming token storage
  const headers = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...options.headers,
  };

  const response = await fetch(`${API_BASE_URL}${url}`, {
    ...options,
    headers,
  });

  if (!response.ok) {
    if (response.status === 401) {
      // Handle unauthorized (e.g., redirect to login)
      window.location.href = '/login';
    }
    throw new Error(`API Error: ${response.status} ${response.statusText}`);
  }

  return response;
}

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
      body: JSON.stringify(policySet),
    });
    return response.json();
  },

  // Stub for creating a policy set (assuming backend supports it or will support it)
  createPolicySet: async (tenantId: string, policySet: Partial<PolicySetDefinition>): Promise<PolicySetDefinition> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/policies`, {
      method: 'POST',
      body: JSON.stringify(policySet),
    });
    return response.json();
  }
};

import { 
  FeatureFlagCheckRequest, 
  FeatureFlagCheckResponse, 
  SegmentRoutingConfiguration, 
  SegmentRoutingPreviewRequest, 
  SegmentRoutingPreviewResponse 
} from '../types/evaluation';

const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5183/api/v1';

// Reusing same fetch wrapper logic - ideally this should be a shared utility
async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
  const token = localStorage.getItem('auth_token');
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
     if (response.status === 401) window.location.href = '/login';
    throw new Error(`API Error: ${response.status} ${response.statusText}`);
  }

  return response;
}

export const EvaluationApi = {
  // Feature Flags
  checkFeatureFlag: async (tenantId: string, flagKey: string, request: FeatureFlagCheckRequest): Promise<FeatureFlagCheckResponse> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/feature-flags/${flagKey}/check`, {
      method: 'POST',
      body: JSON.stringify(request),
    });
    return response.json();
  },

  getEnabledFeatures: async (tenantId: string, request: FeatureFlagCheckRequest): Promise<string[]> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/feature-flags/enabled`, {
      method: 'POST',
      body: JSON.stringify(request),
    });
    const data = await response.json();
    return data.enabledFeatures;
  },

  // Segment Routing
  getSegmentRoutingConfig: async (tenantId: string, agentId: string): Promise<SegmentRoutingConfiguration> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/segment-routing/agents/${agentId}`);
    return response.json();
  },

  previewRouting: async (tenantId: string, agentId: string, request: SegmentRoutingPreviewRequest): Promise<SegmentRoutingPreviewResponse> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/segment-routing/agents/${agentId}/preview`, {
      method: 'POST',
      body: JSON.stringify(request),
    });
    return response.json();
  }
};

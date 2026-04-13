import {
  FeatureFlagCheckRequest,
  FeatureFlagCheckResponse,
  SegmentRoutingConfiguration,
  SegmentRoutingPreviewRequest,
  SegmentRoutingPreviewResponse
} from '../types/evaluation';
import { fetchWithAuth } from './client';

export const EvaluationApi = {
  checkFeatureFlag: async (tenantId: string, flagKey: string, request: FeatureFlagCheckRequest): Promise<FeatureFlagCheckResponse> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/feature-flags/${flagKey}/check`, {
      method: 'POST',
      body: JSON.stringify(request)
    });
    return response.json();
  },

  getEnabledFeatures: async (tenantId: string, request: FeatureFlagCheckRequest): Promise<string[]> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/feature-flags/enabled`, {
      method: 'POST',
      body: JSON.stringify(request)
    });
    const data = await response.json();
    return data.enabledFeatures;
  },

  getSegmentRoutingConfig: async (tenantId: string, agentId: string): Promise<SegmentRoutingConfiguration> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/segment-routing/agents/${agentId}`);
    return response.json();
  },

  previewRouting: async (tenantId: string, agentId: string, request: SegmentRoutingPreviewRequest): Promise<SegmentRoutingPreviewResponse> => {
    const response = await fetchWithAuth(`/tenants/${tenantId}/segment-routing/agents/${agentId}/preview`, {
      method: 'POST',
      body: JSON.stringify(request)
    });
    return response.json();
  }
};

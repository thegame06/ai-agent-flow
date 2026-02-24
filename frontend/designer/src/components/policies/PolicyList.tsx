import { PoliciesApi } from '../../api/policies';
import { useEffect, useState } from 'react';
import { Shield, ShieldAlert, ShieldCheck, Search, Plus } from 'lucide-react';

export interface PolicySetSummary {
  id: string;
  name: string; // "Policy Set 1"
  description: string;
  version: string;
  status: 'Published' | 'Draft';
  policyCount: number;
  severity: string; // "High", "Critical"
  createdAt: string;
}

import { useNavigate } from 'react-router-dom';

export function PolicyList({ tenantId }: { tenantId: string }) {
  const navigate = useNavigate();
  const [policies, setPolicies] = useState<PolicySetSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadPolicies();
  }, [tenantId]);

  const loadPolicies = async () => {
    try {
      setLoading(true);
      // The API currently returns a summary object, so we cast it or need a specific DTO type
      const data = await PoliciesApi.getPolicies(tenantId) as unknown as PolicySetSummary[];
      setPolicies(data);
    } catch (err: any) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  if (loading) return <div className="p-4 text-center">Loading policies...</div>;
  if (error) return <div className="p-4 text-red-500">Error: {error}</div>;

  return (
    <div className="bg-white rounded-lg shadow-sm border border-gray-200">
      <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-gray-50 rounded-t-lg">
        <div>
          <h2 className="text-lg font-semibold text-gray-900 flex items-center gap-2">
            <Shield className="h-5 w-5 text-blue-600" />
            Governance Policies
          </h2>
          <p className="text-sm text-gray-500">Manage compliance rules and guardrails</p>
        </div>
        <button 
          onClick={() => navigate('/policies/new-draft')}
          className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 flex items-center gap-2"
        >
          <Plus className="h-4 w-4" />
          New Policy Set
        </button>
      </div>
      
      <div className="divide-y divide-gray-200">
        {policies.length === 0 ? (
          <div className="p-8 text-center text-gray-500">
            No policies defined yet. Governance is currently disabled.
          </div>
        ) : (
          policies.map((policy) => (
            <div 
              key={policy.id} 
              onClick={() => navigate(`/policies/${policy.id}`)}
              className="p-4 hover:bg-gray-50 transition-colors flex items-center justify-between cursor-pointer group"
            >
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-1">
                  <h3 className="text-sm font-medium text-gray-900 group-hover:text-blue-600">{policy.name || 'Untitled Policy Set'}</h3>
                  <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${
                    policy.status === 'Published' 
                      ? 'bg-green-100 text-green-800' 
                      : 'bg-yellow-100 text-yellow-800'
                  }`}>
                    {policy.status}
                  </span>
                  <span className="text-xs text-gray-400">v{policy.version}</span>
                </div>
                <p className="text-sm text-gray-500 line-clamp-1">{policy.description}</p>
              </div>
              
              <div className="flex items-center gap-6">
                 <div className="text-right">
                    <span className="block text-xs text-gray-500">Rules</span>
                    <span className="text-sm font-medium text-gray-900">{policy.policyCount}</span>
                 </div>
                 
                 <div className="text-right min-w-[80px]">
                    <span className="block text-xs text-gray-500">Max Severity</span>
                    <div className="flex items-center justify-end gap-1">
                       {getSeverityIcon(policy.severity)}
                       <span className={`text-sm font-medium ${getSeverityColor(policy.severity)}`}>
                         {policy.severity}
                       </span>
                    </div>
                 </div>
                 
                 <button className="text-gray-400 hover:text-gray-600">
                    <Search className="h-5 w-5" />
                 </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}

function getSeverityIcon(severity: string) {
  switch (severity) {
    case 'Critical': return <ShieldAlert className="h-4 w-4 text-red-600" />;
    case 'High': return <ShieldAlert className="h-4 w-4 text-orange-500" />;
    case 'Medium': return <ShieldCheck className="h-4 w-4 text-yellow-500" />;
    default: return <ShieldCheck className="h-4 w-4 text-green-500" />;
  }
}

function getSeverityColor(severity: string) {
  switch (severity) {
    case 'Critical': return 'text-red-600';
    case 'High': return 'text-orange-600';
    case 'Medium': return 'text-yellow-600';
    default: return 'text-green-600';
  }
}

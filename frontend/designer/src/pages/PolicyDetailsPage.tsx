import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { 
  PolicySetDefinition, 
  PolicyDefinition,
  PolicyCheckpoint,
  PolicyAction,
  PolicySeverity
} from '../types/policies';
import { PolicyRuleForm } from '../components/policies/PolicyRuleForm';
import { 
  ArrowLeft, 
  Save, 
  Plus, 
  Trash2, 
  Edit2, 
  Shield, 
  AlertTriangle,
  Activity
} from 'lucide-react';

export default function PolicyDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [policySet, setPolicySet] = useState<PolicySetDefinition | null>(null);
  const [loading, setLoading] = useState(true);
  const [isEditingRule, setIsEditingRule] = useState(false);
  const [currentRule, setCurrentRule] = useState<Partial<PolicyDefinition> | null>(null);

  useEffect(() => {
    if (id) loadPolicySet(id);
  }, [id]);

  const loadPolicySet = async (_policySetId: string) => {
    try {
      setLoading(true);
      // Mocking fetch as the backend ID might not exist yet, 
      // replace with PoliciesApi.getPolicySet(tenantId, policySetId) in production
      // const data = await PoliciesApi.getPolicySet(tenantId, policySetId);
      
      // MOCK DATA for Polishing Demo
      const data: PolicySetDefinition = {
        policySetId: 'ps-001',
        name: 'Financial Agent Governance (v1)',
        description: 'Strict compliance rules for loan officer agents. Enforces PII redaction and transaction limits.',
        version: '1.0.0',
        tenantId: 'default-tenant',
        isPublished: true,
        policies: [
          {
            policyId: 'pol-101',
            policyType: 'pii-redaction',
            description: 'Redact credit card numbers from user input',
            appliesAt: PolicyCheckpoint.PreLLM,
            action: PolicyAction.Block,
            severity: PolicySeverity.High,
            isEnabled: true,
            config: { types: 'CreditCard,SSN' },
            targetSegments: []
          },
          {
            policyId: 'pol-102',
            policyType: 'input-size-limit',
            description: 'Limit prompt injection risk by capping input size',
            appliesAt: PolicyCheckpoint.PreAgent,
            action: PolicyAction.Warn,
            severity: PolicySeverity.Medium,
            isEnabled: true,
            config: { maxChars: '1000' },
            targetSegments: []
          }
        ]
      };
      
      setPolicySet(data);
    } catch (err) {
      console.error('Failed to load policy set:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleSaveRule = (rule: PolicyDefinition) => {
    if (!policySet) return;
    
    let newPolicies;
    if (rule.policyId) {
      newPolicies = policySet.policies.map((p: PolicyDefinition) => p.policyId === rule.policyId ? rule : p);
    } else {
      newPolicies = [...policySet.policies, { ...rule, policyId: `new-${Date.now()}` }];
    }
    
    setPolicySet({ ...policySet, policies: newPolicies });
    setIsEditingRule(false);
    setCurrentRule(null);
  };

  const handleDeleteRule = (policyId: string) => {
    if (!policySet) return;
    if (confirm('Are you sure you want to remove this rule?')) {
      setPolicySet({ 
        ...policySet, 
        policies: policySet.policies.filter((p: PolicyDefinition) => p.policyId !== policyId) 
      });
    }
  };

  if (loading) return <div className="p-8 text-center">Loading policy details...</div>;
  if (!policySet) return <div className="p-8 text-center text-red-600">Policy Set not found</div>;

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      {/* Header */}
      <header className="bg-white border-b border-gray-200 px-8 py-4 sticky top-0 z-10">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <div className="flex items-center gap-4">
            <button onClick={() => navigate('/policies')} className="text-gray-400 hover:text-gray-600">
              <ArrowLeft className="h-6 w-6" />
            </button>
            <div>
              <h1 className="text-xl font-bold text-gray-900 flex items-center gap-2">
                <Shield className="h-5 w-5 text-blue-600" />
                {policySet.name}
              </h1>
              <p className="text-sm text-gray-500">v{policySet.version} • {policySet.isPublished ? 'Published' : 'Draft'}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
             <button className="px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50">
               Publish Version
             </button>
             <button className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 flex items-center gap-2">
               <Save className="h-4 w-4" />
               Save Changes
             </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1 max-w-7xl mx-auto w-full p-8 grid grid-cols-1 lg:grid-cols-3 gap-8">
        
        {/* Left Col: Rules List */}
        <div className="lg:col-span-2 space-y-6">
          <div className="bg-white rounded-lg border border-gray-200 shadow-sm overflow-hidden">
             <div className="px-6 py-4 border-b border-gray-200 bg-gray-50 flex items-center justify-between">
               <h2 className="text-lg font-medium text-gray-900">Governance Rules</h2>
               <button 
                 onClick={() => { setCurrentRule(null); setIsEditingRule(true); }}
                 className="text-sm text-blue-600 hover:text-blue-800 font-medium flex items-center gap-1"
               >
                 <Plus className="h-4 w-4" />
                 Add Rule
               </button>
             </div>
             
             <div className="divide-y divide-gray-200">
               {policySet.policies.map((policy: PolicyDefinition) => (
                 <div key={policy.policyId} className={`p-6 hover:bg-slate-50 transition-colors group ${!policy.isEnabled ? 'opacity-60 grayscale' : ''}`}>
                    <div className="flex items-start justify-between">
                       <div className="flex items-start gap-4">
                          <div className={`mt-1 p-2 rounded-lg ${getSeverityBg(policy.severity)}`}>
                             {getSeverityIcon(policy.severity)}
                          </div>
                          <div>
                             <div className="flex items-center gap-2">
                                <h3 className="text-sm font-semibold text-gray-900">{policy.policyType}</h3>
                                <span className={`px-2 py-0.5 text-xs font-mono rounded-full border ${getActionColor(policy.action)}`}>
                                   {policy.action.toUpperCase()}
                                </span>
                                {!policy.isEnabled && <span className="text-xs bg-gray-100 text-gray-500 px-2 py-0.5 rounded">DISABLED</span>}
                             </div>
                             <p className="mt-1 text-sm text-gray-600">{policy.description}</p>
                             
                             <div className="mt-3 flex items-center gap-4 text-xs text-gray-400">
                                <span className="flex items-center gap-1 bg-gray-100 px-2 py-1 rounded">
                                   <Activity className="h-3 w-3" />
                                   {getCheckpointLabel(policy.appliesAt)}
                                </span>
                                {Object.keys(policy.config).length > 0 && (
                                  <span>Config: {JSON.stringify(policy.config)}</span>
                                )}
                             </div>
                          </div>
                       </div>
                       
                       <div className="flex items-center gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                          <button 
                            onClick={() => { setCurrentRule(policy); setIsEditingRule(true); }}
                            className="p-2 text-gray-400 hover:text-blue-600 rounded-full hover:bg-blue-50"
                          >
                             <Edit2 className="h-4 w-4" />
                          </button>
                          <button 
                             onClick={() => handleDeleteRule(policy.policyId)}
                             className="p-2 text-gray-400 hover:text-red-600 rounded-full hover:bg-red-50"
                          >
                             <Trash2 className="h-4 w-4" />
                          </button>
                       </div>
                    </div>
                 </div>
               ))}
               
               {policySet.policies.length === 0 && (
                 <div className="p-12 text-center text-gray-500">
                    <Shield className="h-12 w-12 text-gray-300 mx-auto mb-3" />
                    <p>No rules defined. This policy set is empty.</p>
                 </div>
               )}
             </div>
          </div>
        </div>

        {/* Right Col: Metadata & Summary */}
        <div className="space-y-6">
           <div className="bg-white rounded-lg border border-gray-200 shadow-sm p-6">
              <h3 className="text-sm font-medium text-gray-900 mb-4 uppercase tracking-wider">Configuration</h3>
              <div className="space-y-4">
                 <div>
                    <label className="text-xs font-medium text-gray-500">Description</label>
                    <textarea 
                      className="mt-1 w-full text-sm border-gray-300 rounded-md shadow-sm border p-2 text-gray-700"
                      rows={4}
                      value={policySet.description}
                      onChange={e => setPolicySet({...policySet, description: e.target.value})}
                    />
                 </div>
                 
                 <div className="grid grid-cols-2 gap-4 pt-4 border-t border-gray-100">
                    <div>
                       <span className="block text-2xl font-bold text-gray-900">{policySet.policies.length}</span>
                       <span className="text-xs text-gray-500">Active Rules</span>
                    </div>
                    <div>
                       <span className="block text-2xl font-bold text-red-600">
                         {policySet.policies.filter((p: PolicyDefinition) => p.severity === 'Critical' || p.severity === 'High').length}
                       </span>
                       <span className="text-xs text-gray-500">Critical/High Risks</span>
                    </div>
                 </div>
              </div>
           </div>
        </div>
      </main>

      {/* Side Overlay for Editing */}
      {isEditingRule && (
        <div className="fixed inset-0 bg-black bg-opacity-30 z-50 flex justify-end">
           <div className="bg-white w-full max-w-md h-full shadow-2xl overflow-y-auto transform transition-transform">
              <div className="p-6 border-b border-gray-200 flex justify-between items-center bg-gray-50">
                 <h2 className="text-lg font-bold text-gray-900">
                   {currentRule?.policyId ? 'Edit Rule' : 'New Rule'}
                 </h2>
                 <button onClick={() => setIsEditingRule(false)} className="text-gray-400 hover:text-gray-600">
                    <div className="sr-only">Close</div>
                    <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                    </svg>
                 </button>
              </div>
              <div className="p-6">
                 <PolicyRuleForm 
                   initialData={currentRule || {}} 
                   onSave={handleSaveRule}
                   onCancel={() => setIsEditingRule(false)}
                 />
              </div>
           </div>
        </div>
      )}
    </div>
  );
}

// Helpers
function getSeverityIcon(s: PolicySeverity | string) {
  if (s === PolicySeverity.Critical || s === 'Critical') return <AlertTriangle className="h-5 w-5 text-red-700" />;
  if (s === PolicySeverity.High || s === 'High') return <AlertTriangle className="h-5 w-5 text-orange-600" />;
  return <Shield className="h-5 w-5 text-blue-600" />;
}

function getSeverityBg(s: PolicySeverity | string) {
  if (s === PolicySeverity.Critical || s === 'Critical') return 'bg-red-100';
  if (s === PolicySeverity.High || s === 'High') return 'bg-orange-100';
  return 'bg-blue-100';
}

function getActionColor(a: PolicyAction | string) {
  if (a === PolicyAction.Block || a === 'Block') return 'bg-red-50 text-red-700 border-red-200';
  if (a === PolicyAction.Warn || a === 'Warn') return 'bg-yellow-50 text-yellow-700 border-yellow-200';
  if (a === PolicyAction.Allow || a === 'Allow') return 'bg-green-50 text-green-700 border-green-200';
  return 'bg-gray-50 text-gray-600 border-gray-200';
}

function getCheckpointLabel(c: number) {
  return PolicyCheckpoint[c] || 'Unknown';
}

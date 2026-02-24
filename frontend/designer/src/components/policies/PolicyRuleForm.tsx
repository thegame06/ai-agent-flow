import { useState } from 'react';
import { 
  PolicyDefinition, 
  PolicyAction, 
  PolicySeverity, 
  PolicyCheckpoint 
} from '../../types/policies';
import { Shield, AlertTriangle, Eye, XOctagon, Bell, Info } from 'lucide-react';

interface PolicyRuleFormProps {
  initialData?: Partial<PolicyDefinition>;
  onSave: (rule: PolicyDefinition) => void;
  onCancel: () => void;
}

export function PolicyRuleForm({ initialData, onSave, onCancel }: PolicyRuleFormProps) {
  const [formData, setFormData] = useState<Partial<PolicyDefinition>>(initialData || {
    isEnabled: true,
    action: PolicyAction.Block,
    severity: PolicySeverity.High,
    appliesAt: PolicyCheckpoint.PreLLM,
    targetSegments: [],
    config: {}
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    // In a real app, validation would happen here
    onSave(formData as PolicyDefinition);
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div className="space-y-4">
        {/* Basic Info */}
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Policy Type</label>
          <select 
            className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm p-2 border"
            value={formData.policyType || ''}
            onChange={e => setFormData({...formData, policyType: e.target.value})}
            required
          >
            <option value="" disabled>Select a policy type...</option>
            <option value="pii-redaction">PII Redaction (Sensitive Data)</option>
            <option value="prompt-injection">Prompt Injection Defense</option>
            <option value="topic-blacklist">Topic Filters / Blacklist</option>
            <option value="input-size-limit">Input Size Limiter</option>
            <option value="regex-match">Custom Regex Pattern</option>
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
          <textarea 
            className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm p-2 border"
            rows={2}
            value={formData.description || ''}
            onChange={e => setFormData({...formData, description: e.target.value})}
            placeholder="e.g., Block any credit card numbers in user input"
            required
          />
        </div>

        {/* Action & Severity */}
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Action</label>
            <div className="grid grid-cols-1 gap-2">
              {Object.values(PolicyAction).map(action => (
                <label key={action} className={`
                  flex items-center p-3 border rounded-lg cursor-pointer transition-colors
                  ${formData.action === action ? 'bg-blue-50 border-blue-200 ring-1 ring-blue-500' : 'hover:bg-gray-50 border-gray-200'}
                `}>
                  <input 
                    type="radio" 
                    name="action" 
                    value={action}
                    checked={formData.action === action}
                    onChange={() => setFormData({...formData, action})}
                    className="sr-only" 
                  />
                  <div className="flex items-center gap-2">
                    {getActionIcon(action)}
                    <span className="text-sm font-medium text-gray-900">{action}</span>
                  </div>
                </label>
              ))}
            </div>
          </div>

          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Severity</label>
              <select 
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm p-2 border"
                value={formData.severity || PolicySeverity.Medium}
                onChange={e => setFormData({...formData, severity: e.target.value as PolicySeverity})}
              >
                {Object.values(PolicySeverity).map(s => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Checkpoint</label>
              <select 
                className="w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm p-2 border"
                value={formData.appliesAt ?? PolicyCheckpoint.PreLLM}
                onChange={e => setFormData({...formData, appliesAt: parseInt(e.target.value)})}
              >
                <option value={PolicyCheckpoint.PreAgent}>Pre-Agent (Request)</option>
                <option value={PolicyCheckpoint.PreLLM}>Pre-LLM (Prompt)</option>
                <option value={PolicyCheckpoint.PostLLM}>Post-LLM (Response)</option>
                <option value={PolicyCheckpoint.PreTool}>Pre-Tool (Parameters)</option>
                <option value={PolicyCheckpoint.PostTool}>Post-Tool (Result)</option>
                <option value={PolicyCheckpoint.PreResponse}>Pre-Response (Final)</option>
              </select>
            </div>
            
            <div className="flex items-center mt-6">
              <input
                id="isEnabled"
                type="checkbox"
                checked={formData.isEnabled}
                onChange={e => setFormData({...formData, isEnabled: e.target.checked})}
                className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
              />
              <label htmlFor="isEnabled" className="ml-2 block text-sm text-gray-900">
                Rule Enabled
              </label>
            </div>
          </div>
        </div>

        {/* Configuration (Dynamic based on type) */}
        {formData.policyType === 'regex-match' && (
          <div className="p-4 bg-gray-50 rounded-lg border border-gray-200">
            <h4 className="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-3">Regex Configuration</h4>
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-gray-700">Pattern</label>
                <input 
                  type="text" 
                  className="mt-1 block w-full border-gray-300 rounded-md shadow-sm focus:ring-blue-500 focus:border-blue-500 sm:text-sm font-mono p-1 border"
                  placeholder="^SK-[A-Z0-9]{8}$"
                  value={formData.config?.pattern || ''}
                  onChange={e => setFormData({
                    ...formData, 
                    config: {...formData.config, pattern: e.target.value}
                  })}
                />
              </div>
            </div>
          </div>
        )}
      </div>

      <div className="flex justify-end gap-3 pt-4 border-t border-gray-200">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 border border-gray-300 shadow-sm text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
        >
          Cancel
        </button>
        <button
          type="submit"
          className="px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
        >
          Save Rule
        </button>
      </div>
    </form>
  );
}

function getActionIcon(action: PolicyAction) {
  switch (action) {
    case PolicyAction.Block: return <XOctagon className="h-4 w-4 text-red-600" />;
    case PolicyAction.Warn: return <AlertTriangle className="h-4 w-4 text-orange-500" />;
    case PolicyAction.Shadow: return <Eye className="h-4 w-4 text-gray-500" />;
    case PolicyAction.Escalate: return <Bell className="h-4 w-4 text-purple-500" />;
    case PolicyAction.Allow: return <Shield className="h-4 w-4 text-green-500" />;
    default: return <Info className="h-4 w-4 text-gray-400" />;
  }
}

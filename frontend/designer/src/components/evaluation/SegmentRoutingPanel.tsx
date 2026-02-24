import { useState, useEffect } from 'react';
import { EvaluationApi } from '../../api/evaluation';
import { SegmentRoutingConfiguration, SegmentRoutingPreviewResponse } from '../../types/evaluation';
import { GitBranch, Layers, Play, CheckCircle, AlertTriangle } from 'lucide-react';

export function SegmentRoutingPanel({ tenantId, agentId }: { tenantId: string, agentId: string }) {
  const [config, setConfig] = useState<SegmentRoutingConfiguration | null>(null);
  const [previewUserId, setPreviewUserId] = useState('user-123');
  const [previewResult, setPreviewResult] = useState<SegmentRoutingPreviewResponse | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadConfig();
  }, [tenantId, agentId]);

  const loadConfig = async () => {
    try {
      const data = await EvaluationApi.getSegmentRoutingConfig(tenantId, agentId);
      setConfig(data);
    } catch (err) {
      console.error('Failed to load segment routing config', err);
    }
  };

  const handlePreview = async () => {
    setLoading(true);
    try {
      const result = await EvaluationApi.previewRouting(tenantId, agentId, {
        userId: previewUserId,
        userSegments: ['premium', 'beta_testers'], // stubborn mock for now
      });
      setPreviewResult(result);
    } catch (err) {
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  if (!config) return <div className="p-4 text-gray-500">No routing rules configured.</div>;

  return (
    <div className="space-y-6">
      {/* Configuration View */}
      <div className="bg-white rounded-lg border border-gray-200 shadow-sm">
        <div className="p-4 border-b border-gray-200 bg-gray-50 rounded-t-lg flex items-center justify-between">
          <h3 className="font-medium text-gray-900 flex items-center gap-2">
            <GitBranch className="h-4 w-4 text-purple-600" />
            Segment Routing Rules
          </h3>
          <span className={`px-2 py-1 rounded text-xs font-semibold ${config.isEnabled ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'}`}>
            {config.isEnabled ? 'ACTIVE' : 'DISABLED'}
          </span>
        </div>
        
        <div className="p-4 space-y-3">
           {config.rules.map((rule, idx) => (
             <div key={idx} className="flex items-center justify-between p-3 bg-gray-50 rounded border border-gray-100">
                <div className="flex items-center gap-3">
                   <div className="h-6 w-6 rounded-full bg-purple-100 text-purple-600 flex items-center justify-center text-xs font-bold">
                     {rule.priority}
                   </div>
                   <div>
                     <div className="text-sm font-medium text-gray-900">{rule.ruleName}</div>
                     <div className="text-xs text-gray-500 flex gap-1">
                       Matches: {rule.matchSegments.map(s => (
                         <span key={s} className="bg-white border border-gray-200 px-1 rounded">{s}</span>
                       ))}
                     </div>
                   </div>
                </div>
                <div className="flex items-center gap-2">
                   <span className="text-xs text-gray-400">Routes to</span>
                   <span className="text-sm font-mono bg-white px-2 py-1 rounded border border-gray-200">
                     {rule.targetAgentId}
                   </span>
                </div>
             </div>
           ))}
           
           <div className="mt-4 pt-4 border-t border-gray-100 flex justify-between items-center text-sm">
              <span className="text-gray-500">Default fallback:</span>
              <span className="font-mono text-gray-700">{config.defaultTargetAgentId || '(Original)'}</span>
           </div>
        </div>
      </div>

      {/* Preview/Test Panel */}
      <div className="bg-slate-50 rounded-lg border border-slate-200 p-4">
        <h4 className="text-sm font-medium text-slate-700 mb-3 flex items-center gap-2">
          <Layers className="h-4 w-4" />
          Test Routing Logic
        </h4>
        
        <div className="flex gap-2 mb-4">
          <input 
            type="text" 
            value={previewUserId} 
            onChange={e => setPreviewUserId(e.target.value)}
            className="flex-1 px-3 py-2 border border-gray-300 rounded text-sm"
            placeholder="User ID..."
          />
          <button 
            onClick={handlePreview}
            disabled={loading}
            className="bg-purple-600 text-white px-4 py-2 rounded text-sm hover:bg-purple-700 flex items-center gap-2"
          >
            <Play className="h-3 w-3" />
            Simulate
          </button>
        </div>

        {previewResult && (
          <div className={`p-3 rounded-md text-sm border ${
            previewResult.wasRouted ? 'bg-purple-50 border-purple-200' : 'bg-gray-100 border-gray-200'
          }`}>
             <div className="flex items-start gap-2">
               {previewResult.wasRouted 
                 ? <CheckCircle className="h-5 w-5 text-purple-600 shrink-0" />
                 : <AlertTriangle className="h-5 w-5 text-gray-500 shrink-0" />
               }
               <div>
                 <div className="font-medium text-gray-900">
                    Decision: {previewResult.selectedAgentId} 
                    {previewResult.wasRouted && <span className="ml-2 text-xs text-purple-600">(Routed via {previewResult.matchedRuleName})</span>}
                 </div>
                 <p className="text-xs text-gray-500 mt-1">{previewResult.reason}</p>
                 <div className="mt-2 text-xs text-slate-500">
                   Segments evaluated: {previewResult.evaluatedSegments.join(', ')}
                 </div>
               </div>
             </div>
          </div>
        )}
      </div>
    </div>
  );
}

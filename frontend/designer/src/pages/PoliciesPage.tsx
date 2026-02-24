import { PolicyList } from '../components/policies/PolicyList';
import { Shield } from 'lucide-react';

export default function PoliciesPage() {
  const currentTenantId = 'default-tenant'; // Placeholder, should come from auth

  return (
    <div className="p-8 max-w-7xl mx-auto space-y-6">
      <div className="flex justify-between items-center mb-8">
        <div>
           <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">
             <Shield className="h-8 w-8 text-blue-600" />
             Endpoint Protection Policies
           </h1>
           <p className="mt-1 text-gray-500">
             Define guardrails for AI agents. PII Redaction, DLP, and sovereign checks apply here.
           </p>
        </div>
      </div>
      
      <PolicyList tenantId={currentTenantId} />
    </div>
  );
}

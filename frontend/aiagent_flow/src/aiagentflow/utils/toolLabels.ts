const TOOL_LABEL_OVERRIDES: Record<string, string> = {
  af_list_workflows: 'Listar flujos',
  af_trigger_workflow: 'Ejecutar flujo',
  af_get_workflow_status: 'Ver estado de flujo',
  af_route_intent: 'Enrutar intencion',
  af_commerce_resolve_party: 'Resolver cliente',
  af_commerce_assert_active_session: 'Validar sesion activa',
  af_commerce_search_inventory: 'Buscar inventario',
  af_commerce_calculate_sale: 'Calcular venta',
  af_commerce_create_sale: 'Crear venta',
  af_commerce_create_invoice: 'Crear factura',
  af_commerce_send_invoice_whatsapp: 'Enviar factura por WhatsApp',
  af_commerce_send_conversation_message: 'Enviar mensaje al cliente',
  af_list_campaigns: 'Listar campañas',
  af_get_campaign: 'Ver campaña',
  af_list_campaign_segments: 'Listar segmentos',
  af_get_campaign_segment: 'Ver segmento',
  af_preview_campaign_segment: 'Previsualizar segmento',
  af_get_campaign_metrics: 'Ver metricas de campaña',
  af_draft_campaign_from_prompt: 'Borrador de campaña desde prompt',
  af_refine_campaign_draft: 'Refinar borrador de campaña',
  af_validate_campaign_draft: 'Validar borrador de campaña',
  af_create_campaign: 'Crear campaña',
  af_update_campaign: 'Actualizar campaña',
  af_publish_campaign: 'Publicar campaña',
  af_pause_campaign: 'Pausar campaña',
  af_resume_campaign: 'Reanudar campaña',
  af_run_campaign_now: 'Ejecutar campaña ahora',
  af_list_campaign_runs: 'Listar corridas de campaña',
  af_get_campaign_run: 'Ver corrida de campaña',
  af_retry_campaign_failures: 'Reintentar fallos de campaña',
  af_get_campaign_contact_results: 'Ver resultados por contacto',
  af_list_campaign_call_playbooks: 'Listar playbooks de llamada',
  af_get_campaign_call_playbook: 'Ver playbook de llamada',
  af_create_campaign_call_playbook: 'Crear playbook de llamada',
  af_update_campaign_call_playbook: 'Actualizar playbook de llamada',
  af_list_campaign_call_outcomes: 'Listar resultados de llamada',
  af_get_campaign_call_outcome: 'Ver resultado de llamada',
  af_create_campaign_call_outcome: 'Crear resultado de llamada',
  af_update_campaign_call_outcome: 'Actualizar resultado de llamada',
};

const toWords = (raw: string) =>
  raw
    .replace(/^mcp:[^:]+:/i, '')
    .replace(/^af_/, '')
    .replace(/[_\-.]+/g, ' ')
    .trim()
    .replace(/\b\w/g, (char) => char.toUpperCase());

export const normalizeToolLabel = (tool: unknown): string => {
  if (typeof tool === 'string') {
    const key = tool.trim();
    if (!key) return 'Herramienta';
    return TOOL_LABEL_OVERRIDES[key] || toWords(key);
  }

  if (tool && typeof tool === 'object') {
    const candidate = tool as Record<string, unknown>;
    const key =
      String(candidate.displayName || candidate.toolName || candidate.name || candidate.key || candidate.toolId || '').trim();
    if (!key) return 'Herramienta';
    return TOOL_LABEL_OVERRIDES[key] || toWords(key);
  }

  return 'Herramienta';
};

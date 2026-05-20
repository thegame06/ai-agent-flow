const TOOL_LABEL_OVERRIDES: Record<string, string> = {
  af_list_workflows: 'Listar flujos',
  af_trigger_workflow: 'Ejecutar flujo',
  af_get_workflow_status: 'Ver estado de flujo',
  af_route_intent: 'Enrutar intención',
  af_commerce_resolve_party: 'Resolver cliente',
  af_commerce_assert_active_session: 'Validar sesión activa',
  af_commerce_search_inventory: 'Buscar inventario',
  af_commerce_calculate_sale: 'Calcular venta',
  af_commerce_create_sale: 'Crear venta',
  af_commerce_create_invoice: 'Crear factura',
  af_commerce_send_invoice_whatsapp: 'Enviar factura por WhatsApp',
  af_commerce_send_conversation_message: 'Enviar mensaje al cliente',
};

const toWords = (raw: string) =>
  raw
    .replace(/^mcp:[^:]+:/i, '')
    .replace(/^af_/, '')
    .replace(/[_\-.]+/g, ' ')
    .trim()
    .replace(/\b\w/g, (c) => c.toUpperCase());

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

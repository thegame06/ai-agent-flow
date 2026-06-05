import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import List from '@mui/material/List';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Drawer from '@mui/material/Drawer';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import ListItem from '@mui/material/ListItem';
import MenuItem from '@mui/material/MenuItem';
import Accordion from '@mui/material/Accordion';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import ListItemText from '@mui/material/ListItemText';
import ListItemButton from '@mui/material/ListItemButton';
import AccordionSummary from '@mui/material/AccordionSummary';
import AccordionDetails from '@mui/material/AccordionDetails';
import CircularProgress from '@mui/material/CircularProgress';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { MessageTimeline } from 'src/aiagentflow/pages/threads/components/MessageTimeline';
import { SaleEditorDialog } from 'src/aiagentflow/pages/threads/components/SaleEditorDialog';
import { InvoicePreviewDialog } from 'src/aiagentflow/pages/threads/components/InvoicePreviewDialog';

import { Iconify } from 'src/components/iconify';

const MODULE_IDS = {
  communicationInbox: 'communication-inbox',
  inventory: 'inventory',
  salesPos: 'sales-pos',
  billing: 'billing',
} as const;

type SessionRow = {
  id: string;
  channelId: string;
  channelType: string;
  identifier: string;
  agentId?: string;
  threadId?: string;
  status: string;
  createdAt: string;
  lastActivityAt: string;
  expiresAt?: string;
  windowOpen?: boolean;
  displayName?: string;
  customerKind?: string;
  unreadCount?: number;
  replyPending?: boolean;
  lastCustomerMessage?: string;
  lastAgentMessage?: string;
  lastError?: string;
  lastFailureLevel?: string;
  routingWorkflowId?: string;
  routingStage?: string;
  routingFallbackState?: string;
  requiresHumanReview?: boolean;
  operationalState?: string;
  spamReputationStatus?: string;
  spamSignalCount?: number;
  spamLastReasonCode?: string;
};

type SessionMessage = {
  id: string;
  direction: string;
  content: string;
  createdAt: string;
  actor?: string;
  deliveryState?: string;
  errorMessage?: string;
  metadata?: Record<string, string>;
};

type AgentOption = {
  id: string;
  name: string;
};
type ConversationEvent = {
  key: string;
  label: string;
  time: string;
  color: 'default' | 'info' | 'secondary' | 'success' | 'warning';
};

type CommerceIdentityLink = {
  channel: string;
  identifier: string;
};

type CommerceParty = {
  id: string;
  kind: string;
  channel: string;
  identifier: string;
  displayName?: string;
  fullName?: string;
  email?: string;
  phone?: string;
  linkedIdentities?: CommerceIdentityLink[];
};

type ConversationContext = {
  id: string;
  channelType: string;
  identifier: string;
  threadId?: string;
  status: string;
  expiresAt?: string;
  isExpired: boolean;
  unread?: number;
  commercialState?: string;
  party?: CommerceParty;
};

type InventoryItem = {
  id: string;
  sku: string;
  name: string;
  itemType?: string;
  unitOfMeasure?: string;
  tracksInventory?: boolean;
  unitPrice: number;
  onHand: number;
  active: boolean;
};

type CartItem = {
  sku: string;
  name: string;
  unitPrice: number;
  quantity: number;
};

type SaleRow = {
  id: string;
  state: string;
  subtotal: number;
  discount: number;
  tax: number;
  total: number;
  currency: string;
  paymentMethod: string;
  createdAt: string;
};

type InvoiceRow = {
  id: string;
  number: string;
  status: string;
  total: number;
  currency: string;
  saleId?: string;
  orderId?: string;
  issuedAt?: string;
  createdAt: string;
};

type CustomerDraft = {
  displayName: string;
  fullName: string;
  phone: string;
  email: string;
  kind: string;
};

type SaleComposer = {
  paymentMethod: string;
  applyTax: boolean;
  taxRate: string;
  discountAmount: string;
};

const commercialStateColor = (state?: string) => {
  if (state === 'paid') return 'success';
  if (state === 'invoiced') return 'secondary';
  if (state === 'sale_created') return 'warning';
  if (state === 'closed') return 'default';
  return 'info';
};

const operationalStateLabel = (state?: string) => {
  switch (state) {
    case 'awaiting_classification':
      return 'Esperando contexto';
    case 'pending_human_review':
      return 'Revision humana';
    case 'escalated_human':
      return 'Escalado';
    case 'spam_review':
      return 'Spam review';
    default:
      return 'Atendido';
  }
};

const operationalStateColor = (state?: string) => {
  switch (state) {
    case 'awaiting_classification':
      return 'info';
    case 'pending_human_review':
      return 'warning';
    case 'escalated_human':
      return 'secondary';
    case 'spam_review':
      return 'error';
    default:
      return 'success';
  }
};

const spamReputationLabel = (status?: string) => {
  switch (status) {
    case 'suspected':
      return 'Spam sospechoso';
    case 'confirmed_spam':
      return 'Spam confirmado';
    case 'cleared':
      return 'Spam limpiado';
    default:
      return null;
  }
};

const spamReputationColor = (status?: string) => {
  switch (status) {
    case 'suspected':
      return 'warning';
    case 'confirmed_spam':
      return 'error';
    case 'cleared':
      return 'success';
    default:
      return 'default';
  }
};

export default function ThreadsPage() {
  const tenantId = useTenantId();

  const [rows, setRows] = useState<SessionRow[]>([]);
  const [totalRows, setTotalRows] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('Active');
  const [loading, setLoading] = useState(false);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [activePanel, setActivePanel] = useState<'customer' | 'sales' | 'billing'>('customer');
  const [contextDrawerOpen, setContextDrawerOpen] = useState(false);

  const [context, setContext] = useState<ConversationContext | null>(null);
  const [customerDraft, setCustomerDraft] = useState<CustomerDraft>({
    displayName: '',
    fullName: '',
    phone: '',
    email: '',
    kind: 'customer',
  });
  const [messages, setMessages] = useState<SessionMessage[]>([]);
  const [messagesCursor, setMessagesCursor] = useState<string | null>(null);
  const [hasMoreMessages, setHasMoreMessages] = useState(false);
  const [loadingMessages, setLoadingMessages] = useState(false);
  const [messageDraft, setMessageDraft] = useState('');
  const [inventoryQuery, setInventoryQuery] = useState('');
  const [inventoryQueryDebounced, setInventoryQueryDebounced] = useState('');
  const [customerLookup, setCustomerLookup] = useState('');
  const [customerLookupDebounced, setCustomerLookupDebounced] = useState('');
  const [customerMatches, setCustomerMatches] = useState<CommerceParty[]>([]);
  const [customerLookupLoading, setCustomerLookupLoading] = useState(false);
  const [inventory, setInventory] = useState<InventoryItem[]>([]);
  const [cart, setCart] = useState<CartItem[]>([]);
  const [sales, setSales] = useState<SaleRow[]>([]);
  const [invoices, setInvoices] = useState<InvoiceRow[]>([]);
  const [lastSaleId, setLastSaleId] = useState('');
  const [lastOrderId, setLastOrderId] = useState('');
  const [saleComposer, setSaleComposer] = useState<SaleComposer>({
    paymentMethod: 'cash',
    applyTax: true,
    taxRate: '0.15',
    discountAmount: '0',
  });
  const [salePreview, setSalePreview] = useState<{ subtotal: number; discount: number; tax: number; total: number } | null>(null);
  const [editingSaleId, setEditingSaleId] = useState<string | null>(null);
  const [invoicePreviewOpen, setInvoicePreviewOpen] = useState(false);
  const [actionError, setActionError] = useState('');
  const [actionOk, setActionOk] = useState('');
  const [enabledModules, setEnabledModules] = useState<Record<string, boolean>>({});
  const [agents, setAgents] = useState<AgentOption[]>([]);

  const inboxEnabled = Boolean(enabledModules[MODULE_IDS.communicationInbox]);
  const inventoryEnabled = Boolean(enabledModules[MODULE_IDS.inventory]);
  const salesEnabled = Boolean(enabledModules[MODULE_IDS.salesPos]);
  const billingEnabled = Boolean(enabledModules[MODULE_IDS.billing]);

  const cartSubtotal = useMemo(() => cart.reduce((acc, item) => acc + item.unitPrice * item.quantity, 0), [cart]);
  const groupedRows = useMemo(() => {
    const today = new Date();
    const isSameDay = (d: Date, base: Date) =>
      d.getFullYear() === base.getFullYear() &&
      d.getMonth() === base.getMonth() &&
      d.getDate() === base.getDate();
    const yesterday = new Date(today);
    yesterday.setDate(today.getDate() - 1);

    const groups: Record<'Hoy' | 'Ayer' | 'Anteriores', SessionRow[]> = {
      Hoy: [],
      Ayer: [],
      Anteriores: [],
    };

    rows.forEach((row) => {
      const dt = new Date(row.lastActivityAt || row.createdAt);
      if (isSameDay(dt, today)) groups.Hoy.push(row);
      else if (isSameDay(dt, yesterday)) groups.Ayer.push(row);
      else groups.Anteriores.push(row);
    });
    return groups;
  }, [rows]);
  const agentNameById = useMemo(
    () => new Map(agents.map((agent) => [agent.id, agent.name])),
    [agents]
  );
  const selectedRow = useMemo(
    () => rows.find((row) => row.id === selectedSessionId) || null,
    [rows, selectedSessionId]
  );
  const conversationEvents = useMemo<ConversationEvent[]>(() => {
    const events: ConversationEvent[] = [];
    const fmt = (ts?: string) => {
      if (!ts) return '';
      const dt = new Date(ts);
      if (Number.isNaN(dt.getTime())) return '';
      return dt.toLocaleTimeString();
    };

    const workflowHandoff = messages.find((message) => message.metadata?.event_type === 'workflow_handoff');
    if (workflowHandoff) {
      events.push({
        key: `wf-handoff-${workflowHandoff.id}`,
        label: 'Routing decidido',
        time: fmt(workflowHandoff.createdAt),
        color: 'info',
      });
      events.push({
        key: `wf-start-${workflowHandoff.id}`,
        label: 'Workflow iniciado',
        time: fmt(workflowHandoff.createdAt),
        color: 'secondary',
      });
    }

    const firstAgentReply = messages.find(
      (message) =>
        message.direction === 'Outgoing' &&
        message.metadata?.actor_agent_id &&
        message.deliveryState !== 'suppressed'
    );
    if (firstAgentReply) {
      const agentId = firstAgentReply.metadata?.actor_agent_id || '';
      const name = agentNameById.get(agentId);
      events.push({
        key: `agent-reply-${firstAgentReply.id}`,
        label: `Agente responde${name ? `: ${name}` : ''}`,
        time: fmt(firstAgentReply.createdAt),
        color: 'success',
      });
    }

    const fallback = messages.find(
      (message) =>
        message.direction === 'Outgoing' &&
        message.deliveryState !== 'suppressed' &&
        message.content.includes('No se pudo identificar la intención')
    );
    if (fallback) {
      events.push({
        key: `fallback-${fallback.id}`,
        label: 'Sin clasificación (bandeja humana)',
        time: fmt(fallback.createdAt),
        color: 'warning',
      });
    }

    return events;
  }, [messages, agentNameById]);

  const loadSessions = async () => {
    setLoading(true);
    setActionError('');
    try {
      const qs = new URLSearchParams();
      qs.set('page', String(page));
      qs.set('pageSize', String(pageSize));
      if (statusFilter) qs.set('status', statusFilter);
      if (search.trim()) qs.set('query', search.trim());
      const res = await axios.get(`${endpoints.agentflow.channelSessions.list(tenantId)}?${qs.toString()}`);
      setRows(res.data?.items ?? []);
      setTotalRows(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar la bandeja.');
    } finally {
      setLoading(false);
    }
  };

  const loadCommerceRows = async (partyId: string) => {
    try {
      const [salesRes, invoicesRes] = await Promise.all([
        axios.get(`${endpoints.agentflow.commerce.salesSearch(tenantId)}?partyId=${encodeURIComponent(partyId)}&page=0&pageSize=10`),
        axios.get(`${endpoints.agentflow.commerce.invoicesSearch(tenantId)}?partyId=${encodeURIComponent(partyId)}&page=0&pageSize=10`),
      ]);
      setSales(salesRes.data?.items ?? []);
      setInvoices(invoicesRes.data?.items ?? []);
    } catch {
      setSales([]);
      setInvoices([]);
    }
  };

  useEffect(() => {
    const timeout = setTimeout(() => loadSessions(), 300);
    return () => clearTimeout(timeout);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, page, pageSize, statusFilter, search]);

  useEffect(() => {
    const timeout = setTimeout(() => setInventoryQueryDebounced(inventoryQuery), 300);
    return () => clearTimeout(timeout);
  }, [inventoryQuery]);

  useEffect(() => {
    const timeout = setTimeout(() => setCustomerLookupDebounced(customerLookup), 300);
    return () => clearTimeout(timeout);
  }, [customerLookup]);

  useEffect(() => {
    const loadModuleStates = async () => {
      try {
        const res = await axios.get(`/api/v1/extensions/tenants/${tenantId}/states`);
        setEnabledModules(res.data ?? {});
      } catch {
        setEnabledModules({});
      }
    };
    loadModuleStates();
  }, [tenantId]);

  useEffect(() => {
    const loadAgents = async () => {
      try {
        const res = await axios.get(endpoints.agentflow.agents.list(tenantId));
        setAgents((res.data || []).map((a: any) => ({ id: String(a.id), name: String(a.name || a.id) })));
      } catch {
        setAgents([]);
      }
    };
    loadAgents();
  }, [tenantId]);

  useEffect(() => {
    if (!inventoryEnabled) return;
    const searchInventory = async () => {
      try {
        const res = await axios.get(`${endpoints.agentflow.commerce.inventorySearch(tenantId)}?query=${encodeURIComponent(inventoryQueryDebounced)}&limit=20`);
        setInventory(res.data ?? []);
      } catch (e: any) {
        setActionError(e?.message ?? 'No se pudo cargar inventario.');
      }
    };
    searchInventory();
  }, [inventoryEnabled, inventoryQueryDebounced, tenantId]);

  useEffect(() => {
    if (!salesEnabled || cart.length === 0) {
      setSalePreview(null);
      return;
    }
    const calculate = async () => {
      try {
        const res = await axios.post(endpoints.agentflow.commerce.calculateSale(tenantId), {
          items: cart,
          applyTax: saleComposer.applyTax,
          taxRate: Number(saleComposer.taxRate || 0),
          discountAmount: Number(saleComposer.discountAmount || 0),
        });
        setSalePreview(res.data);
      } catch {
        setSalePreview(null);
      }
    };
    calculate();
  }, [cart, saleComposer.applyTax, saleComposer.discountAmount, saleComposer.taxRate, salesEnabled, tenantId]);

  useEffect(() => {
    if (!inboxEnabled) return;
    const lookupCustomers = async () => {
      const term = customerLookupDebounced.trim();
      if (term.length < 2) {
        setCustomerMatches([]);
        return;
      }
      try {
        setCustomerLookupLoading(true);
        const res = await axios.get(
          `${endpoints.agentflow.commerce.customers(tenantId)}?query=${encodeURIComponent(term)}&page=0&pageSize=8`
        );
        setCustomerMatches(res.data?.items ?? []);
      } catch {
        setCustomerMatches([]);
      } finally {
        setCustomerLookupLoading(false);
      }
    };
    lookupCustomers();
  }, [customerLookupDebounced, inboxEnabled, tenantId]);

  const applyContext = async (nextContext: ConversationContext) => {
    setContext(nextContext);
    setCustomerDraft({
      displayName: nextContext.party?.displayName ?? '',
      fullName: nextContext.party?.fullName ?? '',
      phone: nextContext.party?.phone ?? nextContext.identifier,
      email: nextContext.party?.email ?? '',
      kind: nextContext.party?.kind ?? 'customer',
    });
    if (nextContext.party?.id) {
      await loadCommerceRows(nextContext.party.id);
    } else {
      setSales([]);
      setInvoices([]);
    }
  };

  const loadContextAndMessages = async (sessionId: string, cursor?: string | null) => {
    setActionError('');
    setActionOk('');
    if (!cursor) {
      setMessages([]);
      setMessagesCursor(null);
      setHasMoreMessages(false);
      setContext(null);
      setCustomerLookup('');
      setCustomerMatches([]);
    }
    try {
      if (!cursor) {
        const contextRes = await axios.get(endpoints.agentflow.commerce.contextBySession(tenantId, sessionId));
        await applyContext(contextRes.data);
      }
      setLoadingMessages(true);
      const msgQs = new URLSearchParams();
      if (cursor) msgQs.set('cursor', cursor);
      else msgQs.set('page', '0');
      msgQs.set('pageSize', '30');
      const msgRes = await axios.get(`${endpoints.agentflow.channelSessions.messages(tenantId, sessionId)}?${msgQs.toString()}`);
      const newItems: SessionMessage[] = msgRes.data?.items ?? [];
      setMessages((prev) => {
        const merged = [...prev, ...newItems];
        const unique = Array.from(new Map(merged.map((m) => [m.id, m])).values());
        return unique.sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime());
      });
      setMessagesCursor(msgRes.data?.nextCursor ?? null);
      setHasMoreMessages(Boolean(msgRes.data?.hasMore));
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar el contexto de la conversacion.');
    } finally {
      setLoadingMessages(false);
    }
  };

  const saveCustomer = async () => {
    if (!context || !inboxEnabled) return;
    try {
      if (context.party?.id) {
        const res = await axios.put(endpoints.agentflow.commerce.customerById(tenantId, context.party.id), customerDraft);
        await applyContext({ ...context, party: res.data });
        setActionOk('Cliente actualizado.');
      } else {
        const res = await axios.post(endpoints.agentflow.commerce.resolveParty(tenantId), {
          channel: context.channelType,
          identifier: context.identifier,
          sessionId: context.id,
          displayName: customerDraft.displayName,
          fullName: customerDraft.fullName,
          phone: customerDraft.phone,
          email: customerDraft.email,
          kind: customerDraft.kind,
        });
        await applyContext({ ...context, party: res.data });
        setActionOk('Cliente creado y vinculado.');
      }
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar el cliente.');
    }
  };

  const sendConversationMessage = async () => {
    if (!context || !messageDraft.trim() || !inboxEnabled) return;
    try {
      await axios.post(endpoints.agentflow.commerce.sendConversationMessage(tenantId, context.id), {
        content: messageDraft.trim(),
      });
      setMessageDraft('');
      setActionOk('Mensaje enviado.');
      await loadContextAndMessages(context.id);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo enviar el mensaje.');
    }
  };

  const closeConversation = async () => {
    if (!context || !inboxEnabled) return;
    try {
      await axios.post(endpoints.agentflow.commerce.closeConversation(tenantId, context.id));
      setActionOk('Conversacion cerrada.');
      await loadSessions();
      await loadContextAndMessages(context.id);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cerrar la conversacion.');
    }
  };

  const addToCart = (item: InventoryItem) => {
    if (!inventoryEnabled || !salesEnabled) return;
    setCart((prev) => {
      const existing = prev.find((row) => row.sku === item.sku);
      if (existing) return prev.map((row) => (row.sku === item.sku ? { ...row, quantity: row.quantity + 1 } : row));
      return [...prev, { sku: item.sku, name: item.name, unitPrice: item.unitPrice, quantity: 1 }];
    });
  };

  const createSale = async () => {
    if (!context?.party?.id || !salesEnabled || cart.length === 0) return;
    try {
      const res = await axios.post(endpoints.agentflow.commerce.createSale(tenantId), {
        partyId: context.party.id,
        sessionId: context.id,
        threadId: context.threadId,
        currency: 'USD',
        paymentMethod: saleComposer.paymentMethod,
        applyTax: saleComposer.applyTax,
        taxRate: Number(saleComposer.taxRate || 0),
        discountAmount: Number(saleComposer.discountAmount || 0),
        items: cart,
      });
      setLastSaleId(res.data.id);
      setActionOk(`Venta creada por $${Number(salePreview?.total ?? cartSubtotal).toFixed(2)}.`);
      await loadCommerceRows(context.party.id);
      setActivePanel('sales');
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo crear venta.');
    }
  };

  const createOrder = async () => {
    if (!context?.party?.id || !salesEnabled || cart.length === 0) return;
    try {
      const res = await axios.post(endpoints.agentflow.commerce.createOrder(tenantId), {
        partyId: context.party.id,
        sessionId: context.id,
        threadId: context.threadId,
        currency: 'USD',
        items: cart,
      });
      setLastOrderId(res.data.id);
      setActionOk(`Orden creada: ${res.data.id}`);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo crear orden.');
    }
  };

  const createInvoice = async () => {
    if (!context?.party?.id || !billingEnabled) return;
    try {
      const res = await axios.post(endpoints.agentflow.commerce.createInvoice(tenantId), {
        partyId: context.party.id,
        saleId: lastSaleId || undefined,
        orderId: lastOrderId || undefined,
        total: Number(salePreview?.total ?? cartSubtotal),
        currency: 'USD',
        sessionId: context.id,
        threadId: context.threadId,
      });
      setActionOk(`Factura emitida: ${res.data.id}`);
      await loadCommerceRows(context.party.id);
      const refresh = await axios.get(endpoints.agentflow.commerce.contextBySession(tenantId, context.id));
      await applyContext(refresh.data);
      setActivePanel('billing');
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo emitir factura.');
    }
  };

  const markInvoicePaid = async (invoiceId: string) => {
    if (!billingEnabled || !context?.party?.id) return;
    try {
      await axios.put(endpoints.agentflow.commerce.invoiceStatus(tenantId, invoiceId), { status: 'paid' });
      setActionOk('Factura marcada como pagada.');
      await loadCommerceRows(context.party.id);
      const refresh = await axios.get(endpoints.agentflow.commerce.contextBySession(tenantId, context.id));
      await applyContext(refresh.data);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo actualizar la factura.');
    }
  };

  const sendInvoiceWhatsApp = async (invoiceId: string) => {
    if (!billingEnabled) return;
    try {
      await axios.post(endpoints.agentflow.commerce.invoiceSendWhatsApp(tenantId, invoiceId));
      setActionOk('Factura enviada por WhatsApp.');
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo enviar la factura.');
    }
  };

  const downloadInvoice = async (invoiceId: string) => {
    try {
      const res = await axios.get(endpoints.agentflow.commerce.invoicePdf(tenantId, invoiceId), { responseType: 'blob' });
      const blob = new Blob([res.data], { type: res.headers['content-type'] || 'application/octet-stream' });
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', `${invoiceId}.txt`);
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo descargar la factura.');
    }
  };

  const handleSaleSaved = async () => {
    if (!context?.party?.id) return;
    await loadCommerceRows(context.party.id);
    const refresh = await axios.get(endpoints.agentflow.commerce.contextBySession(tenantId, context.id));
    await applyContext(refresh.data);
    setActionOk('Venta actualizada.');
  };

  const applyCustomerMatch = (match: CommerceParty) => {
    setCustomerDraft({
      displayName: match.displayName ?? '',
      fullName: match.fullName ?? '',
      phone: match.phone ?? context?.identifier ?? '',
      email: match.email ?? '',
      kind: match.kind ?? 'customer',
    });
    setCustomerLookup(match.fullName || match.displayName || match.phone || match.identifier);
    setActionOk('Cliente cargado. Guarda para vincular este canal al registro existente.');
  };

  return (
    <>
      <Helmet>
        <title>Bandeja de entrada | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper variant="outlined" sx={{ p: 2, mb: 2, borderRadius: 2 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ bgcolor: 'primary.lighter', color: 'primary.main' }}>
                <Iconify icon="mdi:chat-processing-outline" />
              </Avatar>
              <Box>
                <Typography variant="h4">Bandeja de entrada</Typography>
                <Typography variant="body2" color="text.secondary">
                  Conversaciones omnicanal en un flujo unico de atencion.
                </Typography>
              </Box>
            </Stack>
            <Stack direction="row" spacing={1}>
              <Button variant="outlined" href={paths.dashboard.commerce} startIcon={<Iconify icon="mdi:store-cog-outline" />}>
                Commerce admin
              </Button>
              <Button variant="outlined" onClick={loadSessions} startIcon={<Iconify icon="solar:refresh-line-duotone" />}>
                Actualizar
              </Button>
            </Stack>
          </Stack>
        </Paper>

        {actionError && <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert>}
        {actionOk && <Alert severity="success" sx={{ mb: 2 }}>{actionOk}</Alert>}

        <Grid container spacing={2}>
          <Grid item xs={12} md={4}>
            <Card sx={{ p: 1.5, minHeight: 620 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ mb: 1.5 }}>
                <TextField size="small" fullWidth placeholder="Buscar cliente, telefono o identificador..." value={search} onChange={(e) => setSearch(e.target.value)} />
                <TextField select size="small" value={statusFilter} onChange={(e) => { setStatusFilter(e.target.value); setPage(0); }} sx={{ minWidth: 150 }}>
                  <MenuItem value="">Todos</MenuItem>
                  <MenuItem value="Active">Active</MenuItem>
                  <MenuItem value="Paused">Paused</MenuItem>
                  <MenuItem value="Closed">Closed</MenuItem>
                  <MenuItem value="Expired">Expired</MenuItem>
                </TextField>
                <Button variant="contained" onClick={() => { setPage(0); loadSessions(); }}>Buscar</Button>
              </Stack>

              <Box sx={{ minHeight: 520 }}>
                {loading ? (
                  <Stack alignItems="center" justifyContent="center" sx={{ py: 8 }}>
                    <CircularProgress size={26} />
                  </Stack>
                ) : (
                  <List dense sx={{ p: 0 }}>
                    {(['Hoy', 'Ayer', 'Anteriores'] as const).map((section) => (
                      <Box key={section} sx={{ mb: 1 }}>
                        <Typography variant="overline" color="text.secondary" sx={{ px: 1, fontSize: 11 }}>
                          {section}
                        </Typography>
                        {groupedRows[section].map((row) => {
                      const isSelected = selectedSessionId === row.id;
                      const hasUnread = (row.unreadCount ?? 0) > 0;
                      const lastEvent = row.lastError
                        ? 'Error de entrega'
                        : operationalStateLabel(row.operationalState);
                      const spamLabel = spamReputationLabel(row.spamReputationStatus);
                      return (
                        <ListItem key={row.id} disablePadding sx={{ mb: 0.5 }}>
                          <ListItemButton
                            selected={isSelected}
                            onClick={() => {
                              setSelectedSessionId(row.id);
                              setContextDrawerOpen(false);
                              loadContextAndMessages(row.id);
                            }}
                            sx={{ borderRadius: 1.5, alignItems: 'flex-start', py: 1.2 }}
                          >
                            <Avatar sx={{ width: 34, height: 34, mr: 1.25 }}>
                              {(row.displayName || row.identifier).slice(0, 1).toUpperCase()}
                            </Avatar>
                            <ListItemText
                              primary={
                                <Stack direction="row" justifyContent="space-between" alignItems="center" spacing={1}>
                                  <Typography variant="subtitle2" noWrap sx={{ maxWidth: 170 }}>
                                    {row.displayName || row.identifier}
                                  </Typography>
                                  <Typography variant="caption" color="text.secondary">
                                    {new Date(row.lastActivityAt).toLocaleTimeString()}
                                  </Typography>
                                </Stack>
                              }
                              secondary={
                                <Stack spacing={0.5} sx={{ mt: 0.25 }}>
                                  <Typography variant="caption" color="text.secondary" noWrap>
                                    {row.lastCustomerMessage || row.lastAgentMessage || row.identifier}
                                  </Typography>
                                  <Stack direction="row" spacing={0.75} useFlexGap flexWrap="wrap" sx={{ minWidth: 0, maxWidth: '100%' }}>
                                    <Chip size="small" variant="outlined" label={row.channelType} />
                                    <Chip size="small" label={row.status} color={row.status === 'Active' ? 'success' : 'default'} />
                                    <Chip size="small" color={operationalStateColor(row.operationalState) as any} variant="outlined" label={lastEvent} />
                                    {spamLabel && (
                                      <Chip
                                        size="small"
                                        color={spamReputationColor(row.spamReputationStatus) as any}
                                        label={
                                          row.spamSignalCount && row.spamSignalCount > 0
                                            ? `${spamLabel} (${row.spamSignalCount})`
                                            : spamLabel
                                        }
                                      />
                                    )}
                                    {(row.replyPending || hasUnread) && <Chip size="small" color="warning" label="Por responder" />}
                                    {hasUnread && <Chip size="small" color="primary" label={String(row.unreadCount)} />}
                                  </Stack>
                                </Stack>
                              }
                            />
                          </ListItemButton>
                        </ListItem>
                      );
                        })}
                      </Box>
                    ))}
                    {!rows.length && (
                      <ListItem>
                        <ListItemText primary="No hay conversaciones para este filtro." />
                      </ListItem>
                    )}
                  </List>
                )}
                <Stack direction="row" spacing={1} justifyContent="flex-end" sx={{ mt: 1 }}>
                  <Button size="small" onClick={() => setPage((prev) => Math.max(0, prev - 1))} disabled={page === 0}>
                    Anterior
                  </Button>
                  <Button
                    size="small"
                    onClick={() => setPage((prev) => prev + 1)}
                    disabled={(page + 1) * pageSize >= totalRows}
                  >
                    Siguiente
                  </Button>
                </Stack>
              </Box>
            </Card>
          </Grid>

          <Grid item xs={12} md={8}>
            <Card sx={{ p: 2, minHeight: 620, display: 'flex', flexDirection: 'column' }}>
              {!selectedSessionId && <Typography color="text.secondary">Selecciona una conversacion para seguimiento comercial.</Typography>}
              {selectedSessionId && (
                <Stack spacing={1.5} sx={{ height: '100%' }}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Stack direction="row" spacing={1.2} alignItems="center" sx={{ minWidth: 0 }}>
                      <Avatar sx={{ width: 36, height: 36 }}>
                        {(context?.party?.fullName || context?.party?.displayName || context?.identifier || '?').slice(0, 1).toUpperCase()}
                      </Avatar>
                      <Box sx={{ minWidth: 0 }}>
                        <Typography variant="subtitle1" noWrap>
                          {context?.party?.fullName || context?.party?.displayName || context?.identifier || selectedSessionId}
                        </Typography>
                        <Typography variant="caption" color={context?.isExpired ? 'error.main' : 'text.secondary'} noWrap>
                          {context
                            ? `${context.channelType} · ${context.identifier} · no leidos ${context.unread ?? 0}${context.isExpired ? ' · ventana expirada (24h)' : ''}`
                            : 'Cargando contexto...'}
                        </Typography>
                      </Box>
                    </Stack>
                    <Stack direction="row" spacing={0.75} alignItems="center">
                      <Chip size="small" label={context?.commercialState || 'lead'} color={commercialStateColor(context?.commercialState) as any} />
                      <Chip size="small" label={context?.status || '...'} variant="outlined" />
                      {selectedRow?.routingWorkflowId && (
                        <Chip size="small" color="secondary" variant="outlined" label={`Workflow ${selectedRow.routingWorkflowId}`} />
                      )}
                      {selectedRow?.operationalState && (
                        <Chip
                          size="small"
                          color={operationalStateColor(selectedRow.operationalState) as any}
                          variant="outlined"
                          label={operationalStateLabel(selectedRow.operationalState)}
                        />
                      )}
                      {spamReputationLabel(selectedRow?.spamReputationStatus) && (
                        <Chip
                          size="small"
                          color={spamReputationColor(selectedRow?.spamReputationStatus) as any}
                          label={
                            selectedRow?.spamSignalCount && selectedRow.spamSignalCount > 0
                              ? `${spamReputationLabel(selectedRow.spamReputationStatus)} (${selectedRow.spamSignalCount})`
                              : spamReputationLabel(selectedRow?.spamReputationStatus)
                          }
                        />
                      )}
                      <Chip
                        size="small"
                        variant="outlined"
                        label={`Atiende: ${agentNameById.get(selectedRow?.agentId || '') || selectedRow?.agentId || 'sin agente'}`}
                      />
                      <IconButton size="small" onClick={() => { setActivePanel('customer'); setContextDrawerOpen(true); }}>
                        <Iconify icon="mdi:account-details-outline" />
                      </IconButton>
                      <IconButton size="small" onClick={() => { setActivePanel('sales'); setContextDrawerOpen(true); }} disabled={!salesEnabled}>
                        <Iconify icon="mdi:cart-outline" />
                      </IconButton>
                      <IconButton size="small" onClick={() => { setActivePanel('billing'); setContextDrawerOpen(true); }} disabled={!billingEnabled}>
                        <Iconify icon="mdi:file-document-outline" />
                      </IconButton>
                      <IconButton size="small" onClick={() => selectedSessionId && loadContextAndMessages(selectedSessionId)}>
                        <Iconify icon="solar:refresh-line-duotone" />
                      </IconButton>
                      <IconButton size="small" color="warning" onClick={closeConversation} disabled={!inboxEnabled || context?.status === 'Closed'}>
                        <Iconify icon="mdi:close-circle-outline" />
                      </IconButton>
                    </Stack>
                  </Stack>
                  {conversationEvents.length > 0 && (
                    <Stack direction="row" spacing={1} alignItems="center" sx={{ px: 0.5, flexWrap: 'wrap' }} useFlexGap>
                      {conversationEvents.map((event) => (
                        <Chip
                          key={event.key}
                          size="small"
                          color={event.color}
                          variant="outlined"
                          label={event.time ? `${event.label} · ${event.time}` : event.label}
                        />
                      ))}
                    </Stack>
                  )}

                  <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                    <MessageTimeline
                      messages={messages}
                      loading={loadingMessages}
                      hasMore={hasMoreMessages}
                      onLoadMore={() => selectedSessionId && loadContextAndMessages(selectedSessionId, messagesCursor)}
                      resolveAgentName={(agentId) => agentNameById.get(agentId)}
                    />
                    <Stack direction="row" spacing={1} sx={{ mt: 1.5, pt: 1, borderTop: '1px solid', borderColor: 'divider' }}>
                      <TextField
                        size="small"
                        fullWidth
                        placeholder="Escribe un mensaje..."
                        value={messageDraft}
                        onChange={(e) => setMessageDraft(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && !e.shiftKey) {
                            e.preventDefault();
                            sendConversationMessage();
                          }
                        }}
                        disabled={!inboxEnabled}
                      />
                      <IconButton color="primary" onClick={sendConversationMessage} disabled={!inboxEnabled || !messageDraft.trim()}>
                        <Iconify icon="solar:plain-bold" />
                      </IconButton>
                    </Stack>
                  </Box>
                  <Drawer
                    anchor="right"
                    open={contextDrawerOpen}
                    onClose={() => setContextDrawerOpen(false)}
                    ModalProps={{ keepMounted: true }}
                    PaperProps={{ sx: { width: { xs: '100%', sm: 420 }, p: 2 } }}
                  >
                    <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1.5 }}>
                      <Typography variant="subtitle1">
                        {activePanel === 'customer' ? 'Cliente y contexto' : activePanel === 'sales' ? 'Ventas y POS' : 'Facturas'}
                      </Typography>
                      <IconButton size="small" onClick={() => setContextDrawerOpen(false)}>
                        <Iconify icon="mdi:close" />
                      </IconButton>
                    </Stack>

                    {activePanel === 'customer' && (
                      <Stack spacing={1.5}>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Informacion</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                            <Stack spacing={0.75}>
                              <Typography variant="body2"><b>Canal:</b> {context?.channelType || '-'}</Typography>
                              <Typography variant="body2"><b>Identificador:</b> {context?.identifier || '-'}</Typography>
                              <Typography variant="body2"><b>Estado:</b> {context?.status || '-'}</Typography>
                              <Typography variant="body2"><b>Hilo:</b> {context?.threadId || '-'}</Typography>
                            </Stack>
                          </AccordionDetails>
                        </Accordion>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Contacto</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                        <TextField
                          size="small"
                          label="Buscar cliente existente"
                          placeholder="Nombre, telefono o email"
                          value={customerLookup}
                          onChange={(e) => setCustomerLookup(e.target.value)}
                          disabled={!inboxEnabled}
                        />
                        {(customerLookupLoading || customerMatches.length > 0) && (
                          <List dense sx={{ maxHeight: 140, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                            {customerMatches.map((item) => (
                              <ListItem
                                key={item.id}
                                secondaryAction={
                                  <Button size="small" onClick={() => applyCustomerMatch(item)} disabled={!inboxEnabled}>
                                    Usar
                                  </Button>
                                }
                              >
                                <ListItemText
                                  primary={item.fullName || item.displayName || item.phone || item.identifier}
                                  secondary={[item.phone, item.email, item.kind].filter(Boolean).join(' - ')}
                                />
                              </ListItem>
                            ))}
                          </List>
                        )}
                        <TextField size="small" label="Alias" value={customerDraft.displayName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, displayName: e.target.value }))} disabled={!inboxEnabled} />
                        <TextField size="small" label="Nombre completo" value={customerDraft.fullName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, fullName: e.target.value }))} disabled={!inboxEnabled} />
                        <TextField size="small" label="Telefono" value={customerDraft.phone} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, phone: e.target.value }))} disabled={!inboxEnabled} />
                        <TextField size="small" label="Email" value={customerDraft.email} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, email: e.target.value }))} disabled={!inboxEnabled} />
                        <TextField select size="small" label="Tipo" value={customerDraft.kind} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, kind: e.target.value }))} disabled={!inboxEnabled}>
                          <MenuItem value="lead">Lead</MenuItem>
                          <MenuItem value="customer">Customer</MenuItem>
                        </TextField>
                        <Button size="small" variant="outlined" onClick={saveCustomer} disabled={!inboxEnabled}>
                          {context?.party?.id ? 'Guardar cliente' : 'Crear cliente'}
                        </Button>
                          </AccordionDetails>
                        </Accordion>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Historial comercial</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                              <Chip size="small" label={`Ventas: ${sales.length}`} />
                              <Chip size="small" label={`Facturas: ${invoices.length}`} />
                            </Stack>
                            <Typography variant="caption" color="text.secondary">
                              Gestiona productos, carrito y acciones comerciales en el panel de Ventas y POS.
                            </Typography>
                          </AccordionDetails>
                        </Accordion>
                      </Stack>
                    )}

                    {activePanel === 'sales' && (
                      <Stack spacing={1.5}>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Catálogo e inventario</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                            <Stack spacing={1}>
                              <Stack direction="row" spacing={1}>
                                <TextField size="small" fullWidth placeholder="Buscar SKU o nombre" value={inventoryQuery} onChange={(e) => setInventoryQuery(e.target.value)} disabled={!inventoryEnabled} />
                                <Button variant="outlined" disabled={!inventoryEnabled}>Buscar</Button>
                              </Stack>
                              <List dense sx={{ maxHeight: 170, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                                {inventory.map((item) => (
                                  <ListItem key={item.id} secondaryAction={<Button size="small" onClick={() => addToCart(item)} disabled={!inventoryEnabled || !salesEnabled}>Agregar</Button>}>
                                    <ListItemText
                                      primary={`${item.sku} - ${item.name}`}
                                      secondary={`$${item.unitPrice} - ${item.itemType || 'physical'} / ${item.unitOfMeasure || 'unit'}${item.tracksInventory === false ? ' - sin stock' : ` - stock ${item.onHand}`}`}
                                    />
                                  </ListItem>
                                ))}
                              </List>
                            </Stack>
                          </AccordionDetails>
                        </Accordion>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Carrito y cobro</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                            <Stack spacing={1}>
                              <List dense sx={{ maxHeight: 120, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                                {cart.map((item) => (
                                  <ListItem key={item.sku}>
                                    <ListItemText primary={`${item.sku} - ${item.name}`} secondary={`${item.quantity} x $${item.unitPrice}`} />
                                  </ListItem>
                                ))}
                                {!cart.length && <ListItem><ListItemText primary="Sin productos en carrito." /></ListItem>}
                              </List>
                              <Grid container spacing={1}>
                                <Grid item xs={12} sm={6}>
                                  <TextField select size="small" fullWidth label="Pago" value={saleComposer.paymentMethod} onChange={(e) => setSaleComposer((prev) => ({ ...prev, paymentMethod: e.target.value }))}>
                                    <MenuItem value="cash">cash</MenuItem>
                                    <MenuItem value="card">card</MenuItem>
                                    <MenuItem value="transfer">transfer</MenuItem>
                                  </TextField>
                                </Grid>
                                <Grid item xs={6} sm={3}>
                                  <TextField size="small" fullWidth label="IVA" value={saleComposer.taxRate} onChange={(e) => setSaleComposer((prev) => ({ ...prev, taxRate: e.target.value }))} disabled={!saleComposer.applyTax} />
                                </Grid>
                                <Grid item xs={6} sm={3}>
                                  <TextField size="small" fullWidth label="Descuento" value={saleComposer.discountAmount} onChange={(e) => setSaleComposer((prev) => ({ ...prev, discountAmount: e.target.value }))} />
                                </Grid>
                              </Grid>
                              <Button size="small" variant={saleComposer.applyTax ? 'contained' : 'outlined'} sx={{ alignSelf: 'flex-start' }} onClick={() => setSaleComposer((prev) => ({ ...prev, applyTax: !prev.applyTax }))}>
                                {saleComposer.applyTax ? 'IVA activo' : 'IVA inactivo'}
                              </Button>
                              <Box sx={{ p: 1.5, border: '1px solid', borderColor: 'divider', borderRadius: 2, bgcolor: 'background.neutral' }}>
                                <Typography variant="subtitle2" sx={{ mb: 1 }}>Resumen POS</Typography>
                                <Stack direction="row" spacing={2} flexWrap="wrap">
                                  <Typography variant="body2">Subtotal: ${Number(salePreview?.subtotal ?? cartSubtotal).toFixed(2)}</Typography>
                                  <Typography variant="body2">Descuento: ${Number(salePreview?.discount ?? 0).toFixed(2)}</Typography>
                                  <Typography variant="body2">IVA: ${Number(salePreview?.tax ?? 0).toFixed(2)}</Typography>
                                  <Typography variant="body2" fontWeight={700}>Total: ${Number(salePreview?.total ?? cartSubtotal).toFixed(2)}</Typography>
                                </Stack>
                              </Box>
                              <Stack direction="row" spacing={1} flexWrap="wrap">
                                <Button size="small" variant="contained" onClick={createSale} disabled={!salesEnabled || !cart.length}>Crear venta</Button>
                                <Button size="small" variant="outlined" onClick={createOrder} disabled={!salesEnabled || !cart.length}>Crear orden</Button>
                                <Button size="small" variant="contained" color="secondary" onClick={() => setInvoicePreviewOpen(true)} disabled={!billingEnabled || !cart.length || (!lastSaleId && !lastOrderId)}>Vista previa factura</Button>
                              </Stack>
                            </Stack>
                          </AccordionDetails>
                        </Accordion>
                        <Accordion defaultExpanded disableGutters>
                          <AccordionSummary expandIcon={<Iconify icon="mdi:chevron-down" width={18} />}>
                            <Typography variant="subtitle2">Ventas creadas</Typography>
                          </AccordionSummary>
                          <AccordionDetails sx={{ pt: 0 }}>
                            <List dense sx={{ maxHeight: 220, overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                              {sales.map((sale) => (
                                <ListItem
                                  key={sale.id}
                                  secondaryAction={<Button size="small" onClick={() => setEditingSaleId(sale.id)}>Gestionar</Button>}
                                >
                                  <ListItemText primary={`${sale.state} - $${sale.total} ${sale.currency}`} secondary={`${sale.paymentMethod} - ${new Date(sale.createdAt).toLocaleString()}`} />
                                </ListItem>
                              ))}
                              {!sales.length && <ListItem><ListItemText primary="Sin ventas registradas." /></ListItem>}
                            </List>
                          </AccordionDetails>
                        </Accordion>
                      </Stack>
                    )}

                    {activePanel === 'billing' && (
                      <List dense sx={{ maxHeight: '70vh', overflow: 'auto', border: '1px solid', borderColor: 'divider', borderRadius: 1 }}>
                        {invoices.map((invoice) => (
                          <ListItem
                            key={invoice.id}
                            secondaryAction={
                              <Stack direction="row" spacing={0.5}>
                                <IconButton size="small" onClick={() => downloadInvoice(invoice.id)}>
                                  <Iconify icon="solar:download-minimalistic-bold" />
                                </IconButton>
                                <IconButton size="small" onClick={() => sendInvoiceWhatsApp(invoice.id)} disabled={!billingEnabled}>
                                  <Iconify icon="mdi:whatsapp" />
                                </IconButton>
                                <IconButton size="small" onClick={() => markInvoicePaid(invoice.id)} disabled={invoice.status === 'paid'}>
                                  <Iconify icon="solar:check-circle-bold" />
                                </IconButton>
                              </Stack>
                            }
                          >
                            <ListItemText primary={`${invoice.number || invoice.id} - $${invoice.total} ${invoice.currency}`} secondary={`${invoice.status} - ${new Date(invoice.createdAt).toLocaleString()}`} />
                          </ListItem>
                        ))}
                        {!invoices.length && <ListItem><ListItemText primary="Sin facturas emitidas." /></ListItem>}
                      </List>
                    )}
                  </Drawer>
                </Stack>
              )}
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>

      <SaleEditorDialog
        open={Boolean(editingSaleId)}
        saleId={editingSaleId}
        tenantId={tenantId}
        onClose={() => setEditingSaleId(null)}
        onSaved={handleSaleSaved}
      />

      <InvoicePreviewDialog
        open={invoicePreviewOpen}
        onClose={() => setInvoicePreviewOpen(false)}
        onConfirm={async () => {
          setInvoicePreviewOpen(false);
          await createInvoice();
        }}
        customerName={context?.party?.fullName || context?.party?.displayName || context?.identifier || ''}
        currency="USD"
        items={cart}
        calculation={salePreview}
        disabled={!billingEnabled || (!lastSaleId && !lastOrderId)}
      />
    </>
  );
}

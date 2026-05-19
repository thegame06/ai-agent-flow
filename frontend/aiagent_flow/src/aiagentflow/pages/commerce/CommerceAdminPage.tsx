import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import Avatar from '@mui/material/Avatar';
import Divider from '@mui/material/Divider';
import { DataGrid } from '@mui/x-data-grid';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

import { Iconify } from 'src/components/iconify';

type PartyRow = {
  id: string;
  kind: string;
  displayName?: string;
  fullName?: string;
  phone?: string;
  email?: string;
  identifier: string;
  channel: string;
};

type InventoryRow = {
  id: string;
  sku: string;
  name: string;
  itemType: string;
  unitOfMeasure: string;
  tracksInventory: boolean;
  unitPrice: number;
  onHand: number;
  active: boolean;
};

type InventoryMovementRow = {
  id: string;
  sku: string;
  delta: number;
  balance: number;
  reason: string;
  referenceId?: string;
  createdAt: string;
};

type SaleRow = {
  id: string;
  partyId: string;
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
  partyId: string;
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

type InventoryDraft = {
  sku: string;
  name: string;
  itemType: string;
  unitOfMeasure: string;
  tracksInventory: boolean;
  unitPrice: string;
  onHand: string;
  active: boolean;
};

type InventoryAdjustmentDraft = {
  sku: string;
  delta: string;
  reason: string;
  referenceId: string;
};

type InvoiceDraft = {
  number: string;
  total: string;
  currency: string;
  status: string;
  issuedAt: string;
};

const EMPTY_CUSTOMER: CustomerDraft = {
  displayName: '',
  fullName: '',
  phone: '',
  email: '',
  kind: 'customer',
};

const EMPTY_INVENTORY: InventoryDraft = {
  sku: '',
  name: '',
  itemType: 'physical',
  unitOfMeasure: 'unit',
  tracksInventory: true,
  unitPrice: '',
  onHand: '',
  active: true,
};

const EMPTY_ADJUSTMENT: InventoryAdjustmentDraft = {
  sku: '',
  delta: '',
  reason: 'manual_adjustment',
  referenceId: '',
};

const EMPTY_INVOICE: InvoiceDraft = {
  number: '',
  total: '',
  currency: 'USD',
  status: 'issued',
  issuedAt: '',
};

const MODULE_IDS = {
  communicationInbox: 'communication-inbox',
  inventory: 'inventory',
  salesPos: 'sales-pos',
  billing: 'billing',
} as const;

export default function CommerceAdminPage() {
  const tenantId = useTenantId();

  const [tab, setTab] = useState<'customers' | 'inventory' | 'movements' | 'sales' | 'invoices'>('customers');
  const [actionError, setActionError] = useState('');
  const [actionOk, setActionOk] = useState('');

  const [customerQuery, setCustomerQuery] = useState('');
  const [customers, setCustomers] = useState<PartyRow[]>([]);
  const [customersTotal, setCustomersTotal] = useState(0);
  const [customerPage, setCustomerPage] = useState(0);
  const [customerPageSize, setCustomerPageSize] = useState(25);
  const [selectedCustomer, setSelectedCustomer] = useState<PartyRow | null>(null);
  const [customerDraft, setCustomerDraft] = useState<CustomerDraft>(EMPTY_CUSTOMER);
  const [customerDialogOpen, setCustomerDialogOpen] = useState(false);

  const [inventoryQuery, setInventoryQuery] = useState('');
  const [inventory, setInventory] = useState<InventoryRow[]>([]);
  const [inventoryDraft, setInventoryDraft] = useState<InventoryDraft>(EMPTY_INVENTORY);
  const [inventoryDialogOpen, setInventoryDialogOpen] = useState(false);
  const [inventoryAdjustmentDraft, setInventoryAdjustmentDraft] = useState<InventoryAdjustmentDraft>(EMPTY_ADJUSTMENT);
  const [inventoryAdjustmentOpen, setInventoryAdjustmentOpen] = useState(false);

  const [movements, setMovements] = useState<InventoryMovementRow[]>([]);
  const [movementsTotal, setMovementsTotal] = useState(0);
  const [movementSku, setMovementSku] = useState('');
  const [movementPage, setMovementPage] = useState(0);
  const [movementPageSize, setMovementPageSize] = useState(25);

  const [sales, setSales] = useState<SaleRow[]>([]);
  const [salesTotal, setSalesTotal] = useState(0);
  const [salesPage, setSalesPage] = useState(0);
  const [salesPageSize, setSalesPageSize] = useState(25);
  const [salesState, setSalesState] = useState('');

  const [invoices, setInvoices] = useState<InvoiceRow[]>([]);
  const [invoicesTotal, setInvoicesTotal] = useState(0);
  const [invoicePage, setInvoicePage] = useState(0);
  const [invoicePageSize, setInvoicePageSize] = useState(25);
  const [invoiceStatusFilter, setInvoiceStatusFilter] = useState('');
  const [selectedInvoice, setSelectedInvoice] = useState<InvoiceRow | null>(null);
  const [invoiceDraft, setInvoiceDraft] = useState<InvoiceDraft>(EMPTY_INVOICE);
  const [invoiceDialogOpen, setInvoiceDialogOpen] = useState(false);
  const [invoicePreviewOpen, setInvoicePreviewOpen] = useState(false);
  const [invoicePreviewUrl, setInvoicePreviewUrl] = useState('');
  const [enabledModules, setEnabledModules] = useState<Record<string, boolean>>({});

  const customersEnabled = Boolean(enabledModules[MODULE_IDS.communicationInbox]);
  const inventoryEnabled = Boolean(enabledModules[MODULE_IDS.inventory]);
  const salesEnabled = Boolean(enabledModules[MODULE_IDS.salesPos]);
  const billingEnabled = Boolean(enabledModules[MODULE_IDS.billing]);

  const availableTabs = useMemo(() => {
    const tabs: Array<{ value: 'customers' | 'inventory' | 'movements' | 'sales' | 'invoices'; label: string }> = [];
    if (customersEnabled) tabs.push({ value: 'customers', label: 'Clientes' });
    if (inventoryEnabled) tabs.push({ value: 'inventory', label: 'Inventario' });
    if (inventoryEnabled) tabs.push({ value: 'movements', label: 'Movimientos' });
    if (salesEnabled) tabs.push({ value: 'sales', label: 'Ventas' });
    if (billingEnabled) tabs.push({ value: 'invoices', label: 'Facturas' });
    return tabs;
  }, [billingEnabled, customersEnabled, inventoryEnabled, salesEnabled]);

  const refreshAll = async () => {
    const tasks: Array<Promise<void>> = [];
    if (customersEnabled) tasks.push(loadCustomers());
    if (inventoryEnabled) tasks.push(loadInventory(), loadMovements());
    if (salesEnabled) tasks.push(loadSales());
    if (billingEnabled) tasks.push(loadInvoices());
    await Promise.all(tasks);
  };

  const loadCustomers = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(customerPage),
        pageSize: String(customerPageSize),
      });
      if (customerQuery.trim()) qs.set('query', customerQuery.trim());
      const res = await axios.get(`${endpoints.agentflow.commerce.customers(tenantId)}?${qs.toString()}`);
      setCustomers(res.data?.items ?? []);
      setCustomersTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar los clientes.');
    }
  };

  const loadInventory = async () => {
    try {
      const qs = new URLSearchParams();
      if (inventoryQuery.trim()) qs.set('query', inventoryQuery.trim());
      qs.set('limit', '100');
      const res = await axios.get(`${endpoints.agentflow.commerce.inventorySearch(tenantId)}?${qs.toString()}`);
      setInventory(res.data ?? []);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar el inventario.');
    }
  };

  const loadMovements = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(movementPage),
        pageSize: String(movementPageSize),
      });
      if (movementSku.trim()) qs.set('sku', movementSku.trim());
      const res = await axios.get(`${endpoints.agentflow.commerce.inventoryMovements(tenantId)}?${qs.toString()}`);
      setMovements(res.data?.items ?? []);
      setMovementsTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar los movimientos de inventario.');
    }
  };

  const loadSales = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(salesPage),
        pageSize: String(salesPageSize),
      });
      if (salesState) qs.set('state', salesState);
      const res = await axios.get(`${endpoints.agentflow.commerce.salesSearch(tenantId)}?${qs.toString()}`);
      setSales(res.data?.items ?? []);
      setSalesTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar las ventas.');
    }
  };

  const loadInvoices = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(invoicePage),
        pageSize: String(invoicePageSize),
      });
      if (invoiceStatusFilter) qs.set('status', invoiceStatusFilter);
      const res = await axios.get(`${endpoints.agentflow.commerce.invoicesSearch(tenantId)}?${qs.toString()}`);
      setInvoices(res.data?.items ?? []);
      setInvoicesTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar las facturas.');
    }
  };

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
    if (availableTabs.length === 0) return;
    if (!availableTabs.some((entry) => entry.value === tab)) {
      setTab(availableTabs[0].value);
    }
  }, [availableTabs, tab]);

  useEffect(() => {
    if (!customersEnabled) return undefined;
    const timeout = setTimeout(() => { loadCustomers(); }, 250);
    return () => clearTimeout(timeout);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, customerQuery, customerPage, customerPageSize, customersEnabled]);

  useEffect(() => {
    if (!inventoryEnabled) return undefined;
    const timeout = setTimeout(() => { loadInventory(); }, 250);
    return () => clearTimeout(timeout);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, inventoryQuery, inventoryEnabled]);

  useEffect(() => {
    if (!inventoryEnabled) return;
    loadMovements();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, movementSku, movementPage, movementPageSize, inventoryEnabled]);

  useEffect(() => {
    if (!salesEnabled) return;
    loadSales();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, salesPage, salesPageSize, salesState, salesEnabled]);

  useEffect(() => {
    if (!billingEnabled) return;
    loadInvoices();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, invoicePage, invoicePageSize, invoiceStatusFilter, billingEnabled]);

  useEffect(() => () => {
    if (invoicePreviewUrl) window.URL.revokeObjectURL(invoicePreviewUrl);
  }, [invoicePreviewUrl]);

  const openCustomerDialog = (row: PartyRow) => {
    setSelectedCustomer(row);
    setCustomerDraft({
      displayName: row.displayName ?? '',
      fullName: row.fullName ?? '',
      phone: row.phone ?? '',
      email: row.email ?? '',
      kind: row.kind ?? 'customer',
    });
    setCustomerDialogOpen(true);
  };

  const saveCustomer = async () => {
    if (!selectedCustomer) return;
    try {
      await axios.put(endpoints.agentflow.commerce.customerById(tenantId, selectedCustomer.id), customerDraft);
      setActionOk('Cliente actualizado.');
      setCustomerDialogOpen(false);
      await loadCustomers();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar el cliente.');
    }
  };

  const deleteCustomer = async (partyId: string) => {
    try {
      await axios.delete(endpoints.agentflow.commerce.customerById(tenantId, partyId));
      setActionOk('Cliente eliminado.');
      await loadCustomers();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo eliminar el cliente.');
    }
  };

  const openInventoryDialog = (row?: InventoryRow) => {
    if (row) {
      setInventoryDraft({
        sku: row.sku,
        name: row.name,
        itemType: row.itemType ?? 'physical',
        unitOfMeasure: row.unitOfMeasure ?? 'unit',
        tracksInventory: row.tracksInventory ?? true,
        unitPrice: String(row.unitPrice),
        onHand: String(row.onHand),
        active: row.active,
      });
    } else {
      setInventoryDraft(EMPTY_INVENTORY);
    }
    setInventoryDialogOpen(true);
  };

  const saveInventory = async () => {
    try {
      await axios.put(endpoints.agentflow.commerce.inventoryItemBySku(tenantId, inventoryDraft.sku), {
        name: inventoryDraft.name,
        itemType: inventoryDraft.itemType,
        unitOfMeasure: inventoryDraft.unitOfMeasure,
        tracksInventory: inventoryDraft.tracksInventory,
        unitPrice: Number(inventoryDraft.unitPrice || 0),
        onHand: inventoryDraft.tracksInventory ? Number(inventoryDraft.onHand || 0) : 0,
        active: inventoryDraft.active,
      });
      setActionOk('Producto guardado.');
      setInventoryDialogOpen(false);
      await loadInventory();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar el producto.');
    }
  };

  const openInventoryAdjustment = (row: InventoryRow) => {
    setInventoryAdjustmentDraft({
      sku: row.sku,
      delta: '',
      reason: 'manual_adjustment',
      referenceId: '',
    });
    setInventoryAdjustmentOpen(true);
  };

  const saveInventoryAdjustment = async () => {
    try {
      await axios.post(endpoints.agentflow.commerce.inventoryAdjust(tenantId, inventoryAdjustmentDraft.sku), {
        delta: Number(inventoryAdjustmentDraft.delta || 0),
        reason: inventoryAdjustmentDraft.reason,
        referenceId: inventoryAdjustmentDraft.referenceId || undefined,
      });
      setActionOk('Stock ajustado.');
      setInventoryAdjustmentOpen(false);
      await Promise.all([loadInventory(), loadMovements()]);
      setTab('movements');
      setMovementSku(inventoryAdjustmentDraft.sku);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo ajustar el inventario.');
    }
  };

  const openInvoiceDialog = async (row: InvoiceRow) => {
    try {
      const res = await axios.get(endpoints.agentflow.commerce.invoiceById(tenantId, row.id));
      const invoice = res.data as InvoiceRow;
      setSelectedInvoice(row);
      setInvoiceDraft({
        number: invoice.number ?? row.number,
        total: String(invoice.total ?? row.total),
        currency: invoice.currency ?? row.currency,
        status: invoice.status ?? row.status,
        issuedAt: invoice.issuedAt ? new Date(invoice.issuedAt).toISOString().slice(0, 16) : '',
      });
      setInvoiceDialogOpen(true);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar la factura.');
    }
  };

  const saveInvoice = async () => {
    if (!selectedInvoice) return;
    try {
      await axios.put(endpoints.agentflow.commerce.updateInvoice(tenantId, selectedInvoice.id), {
        number: invoiceDraft.number,
        total: Number(invoiceDraft.total || 0),
        currency: invoiceDraft.currency,
        status: invoiceDraft.status,
        issuedAt: invoiceDraft.issuedAt ? new Date(invoiceDraft.issuedAt).toISOString() : undefined,
      });
      setActionOk('Factura actualizada.');
      setInvoiceDialogOpen(false);
      await loadInvoices();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo actualizar la factura.');
    }
  };

  const openInvoicePreview = async (invoiceId: string) => {
    try {
      if (invoicePreviewUrl) window.URL.revokeObjectURL(invoicePreviewUrl);
      const res = await axios.get(endpoints.agentflow.commerce.invoicePdf(tenantId, invoiceId), { responseType: 'blob' });
      const blob = new Blob([res.data], { type: 'application/pdf' });
      const url = window.URL.createObjectURL(blob);
      setInvoicePreviewUrl(url);
      setInvoicePreviewOpen(true);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo generar la vista previa PDF.');
    }
  };

  const movementSummary = useMemo(() => {
    const totalDelta = movements.reduce((acc, row) => acc + row.delta, 0);
    return { totalDelta };
  }, [movements]);

  return (
    <>
      <Helmet>
        <title>Ventas y cobros | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper variant="outlined" sx={{ p: 2.5, mb: 2, borderRadius: 2 }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} justifyContent="space-between">
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ bgcolor: 'secondary.lighter', color: 'secondary.main' }}>
                <Iconify icon="mdi:store-cog-outline" />
              </Avatar>
              <Box>
                <Typography variant="h4">Ventas y cobros</Typography>
                <Typography variant="body2" color="text.secondary">
                  Clientes, inventario, movimientos, ventas y facturas en una superficie administrativa separada del inbox.
                </Typography>
              </Box>
            </Stack>
            <Button variant="outlined" onClick={refreshAll} startIcon={<Iconify icon="solar:refresh-line-duotone" />}>
              Actualizar
            </Button>
          </Stack>
        </Paper>

        {actionError && <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert>}
        {actionOk && <Alert severity="success" sx={{ mb: 2 }}>{actionOk}</Alert>}

        <Card sx={{ p: 2 }}>
          {availableTabs.length > 0 ? (
            <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ mb: 2 }} variant="scrollable">
              {availableTabs.map((entry) => (
                <Tab key={entry.value} value={entry.value} label={entry.label} />
              ))}
            </Tabs>
          ) : (
            <Alert severity="info" sx={{ mb: 2 }}>
              No hay modulos de comercio habilitados para este tenant.
            </Alert>
          )}

          {tab === 'customers' && (
            <Stack spacing={2}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar por nombre, telefono o email" value={customerQuery} onChange={(e) => { setCustomerQuery(e.target.value); setCustomerPage(0); }} />
                <Button variant="outlined" href={paths.dashboard.threads}>Abrir inbox</Button>
              </Stack>
              <Box sx={{ height: 560 }}>
                <DataGrid
                  rows={customers}
                  rowCount={customersTotal}
                  paginationMode="server"
                  paginationModel={{ page: customerPage, pageSize: customerPageSize }}
                  onPaginationModelChange={(next) => {
                    setCustomerPage(next.page);
                    setCustomerPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'fullName', headerName: 'Nombre', flex: 1, minWidth: 180, valueGetter: (_, row) => row.fullName || row.displayName || row.identifier },
                    { field: 'phone', headerName: 'Telefono', width: 150 },
                    { field: 'email', headerName: 'Email', flex: 1, minWidth: 200 },
                    { field: 'kind', headerName: 'Tipo', width: 120, renderCell: (params) => <Chip size="small" label={params.value} color={params.value === 'lead' ? 'info' : 'success'} /> },
                    { field: 'channel', headerName: 'Canal base', width: 130 },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 180,
                      sortable: false,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1}>
                          <Button size="small" onClick={() => openCustomerDialog(params.row)}>Editar</Button>
                          <Button size="small" color="error" onClick={() => deleteCustomer(params.row.id)}>Eliminar</Button>
                        </Stack>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'inventory' && (
            <Stack spacing={2}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar SKU o nombre" value={inventoryQuery} onChange={(e) => setInventoryQuery(e.target.value)} />
                <Button variant="contained" onClick={() => openInventoryDialog()}>Nuevo producto</Button>
              </Stack>
              <Box sx={{ height: 560 }}>
                <DataGrid
                  rows={inventory}
                  columns={[
                    { field: 'sku', headerName: 'SKU', width: 150 },
                    { field: 'name', headerName: 'Producto', flex: 1, minWidth: 240 },
                    { field: 'itemType', headerName: 'Tipo', width: 120, renderCell: (params) => <Chip size="small" label={params.value} variant="outlined" /> },
                    { field: 'unitOfMeasure', headerName: 'Unidad', width: 110 },
                    { field: 'unitPrice', headerName: 'Precio', width: 120 },
                    { field: 'onHand', headerName: 'Stock', width: 110, valueGetter: (_, row) => row.tracksInventory ? row.onHand : '-' },
                    { field: 'tracksInventory', headerName: 'Controla stock', width: 130, renderCell: (params) => <Chip size="small" label={params.value ? 'Si' : 'No'} color={params.value ? 'success' : 'default'} /> },
                    { field: 'active', headerName: 'Activo', width: 100, renderCell: (params) => <Chip size="small" label={params.value ? 'Si' : 'No'} color={params.value ? 'success' : 'default'} /> },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 220,
                      sortable: false,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1}>
                          <Button size="small" onClick={() => openInventoryDialog(params.row)}>Editar</Button>
                          <Button size="small" color="secondary" onClick={() => openInventoryAdjustment(params.row)}>Ajustar</Button>
                        </Stack>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'movements' && (
            <Stack spacing={2}>
              <Grid container spacing={1}>
                <Grid item xs={12} md={4}>
                  <TextField size="small" fullWidth label="SKU" value={movementSku} onChange={(e) => { setMovementSku(e.target.value); setMovementPage(0); }} />
                </Grid>
                <Grid item xs={12} md={8}>
                  <Paper variant="outlined" sx={{ p: 1.5, height: '100%' }}>
                    <Typography variant="body2" color="text.secondary">Delta acumulado de la pagina actual</Typography>
                    <Typography variant="h6">{movementSummary.totalDelta >= 0 ? '+' : ''}{movementSummary.totalDelta}</Typography>
                  </Paper>
                </Grid>
              </Grid>
              <Box sx={{ height: 560 }}>
                <DataGrid
                  rows={movements}
                  rowCount={movementsTotal}
                  paginationMode="server"
                  paginationModel={{ page: movementPage, pageSize: movementPageSize }}
                  onPaginationModelChange={(next) => {
                    setMovementPage(next.page);
                    setMovementPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'sku', headerName: 'SKU', width: 140 },
                    { field: 'delta', headerName: 'Delta', width: 100, renderCell: (params) => <Chip size="small" label={params.value > 0 ? `+${params.value}` : params.value} color={params.value > 0 ? 'success' : 'warning'} /> },
                    { field: 'balance', headerName: 'Balance', width: 100 },
                    { field: 'reason', headerName: 'Razon', width: 180 },
                    { field: 'referenceId', headerName: 'Referencia', width: 180 },
                    { field: 'createdAt', headerName: 'Fecha', width: 180, valueFormatter: (value) => new Date(value as string).toLocaleString() },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'sales' && (
            <Stack spacing={2}>
              <Grid container spacing={1}>
                <Grid item xs={12} md={4}>
                  <TextField select size="small" fullWidth label="Estado" value={salesState} onChange={(e) => { setSalesState(e.target.value); setSalesPage(0); }}>
                    <MenuItem value="">Todos</MenuItem>
                    <MenuItem value="sale_created">sale_created</MenuItem>
                    <MenuItem value="invoiced">invoiced</MenuItem>
                    <MenuItem value="paid">paid</MenuItem>
                  </TextField>
                </Grid>
              </Grid>
              <Box sx={{ height: 560 }}>
                <DataGrid
                  rows={sales}
                  rowCount={salesTotal}
                  paginationMode="server"
                  paginationModel={{ page: salesPage, pageSize: salesPageSize }}
                  onPaginationModelChange={(next) => {
                    setSalesPage(next.page);
                    setSalesPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'id', headerName: 'Venta', width: 180 },
                    { field: 'state', headerName: 'Estado', width: 130, renderCell: (params) => <Chip size="small" label={params.value} color={params.value === 'paid' ? 'success' : 'warning'} /> },
                    { field: 'paymentMethod', headerName: 'Pago', width: 120 },
                    { field: 'total', headerName: 'Total', width: 120, valueGetter: (_, row) => `${row.total} ${row.currency}` },
                    { field: 'createdAt', headerName: 'Creada', width: 180, valueFormatter: (value) => new Date(value as string).toLocaleString() },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'invoices' && (
            <Stack spacing={2}>
              <Grid container spacing={1}>
                <Grid item xs={12} md={4}>
                  <TextField select size="small" fullWidth label="Estado" value={invoiceStatusFilter} onChange={(e) => { setInvoiceStatusFilter(e.target.value); setInvoicePage(0); }}>
                    <MenuItem value="">Todos</MenuItem>
                    <MenuItem value="issued">issued</MenuItem>
                    <MenuItem value="paid">paid</MenuItem>
                    <MenuItem value="void">void</MenuItem>
                  </TextField>
                </Grid>
              </Grid>
              <Box sx={{ height: 560 }}>
                <DataGrid
                  rows={invoices}
                  rowCount={invoicesTotal}
                  paginationMode="server"
                  paginationModel={{ page: invoicePage, pageSize: invoicePageSize }}
                  onPaginationModelChange={(next) => {
                    setInvoicePage(next.page);
                    setInvoicePageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'number', headerName: 'Numero', width: 210 },
                    { field: 'status', headerName: 'Estado', width: 120, renderCell: (params) => <Chip size="small" label={params.value} color={params.value === 'paid' ? 'success' : 'secondary'} /> },
                    { field: 'total', headerName: 'Total', width: 130, valueGetter: (_, row) => `${row.total} ${row.currency}` },
                    { field: 'issuedAt', headerName: 'Emitida', width: 180, valueFormatter: (value) => value ? new Date(value as string).toLocaleString() : '-' },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 220,
                      sortable: false,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1}>
                          <Button size="small" onClick={() => openInvoiceDialog(params.row)}>Editar</Button>
                          <Button size="small" color="secondary" onClick={() => openInvoicePreview(params.row.id)}>Preview PDF</Button>
                        </Stack>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}
        </Card>
      </DashboardContent>

      <Dialog open={customerDialogOpen} onClose={() => setCustomerDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Editar cliente</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Alias" value={customerDraft.displayName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, displayName: e.target.value }))} />
            <TextField label="Nombre completo" value={customerDraft.fullName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, fullName: e.target.value }))} />
            <TextField label="Telefono" value={customerDraft.phone} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, phone: e.target.value }))} />
            <TextField label="Email" value={customerDraft.email} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, email: e.target.value }))} />
            <TextField select label="Tipo" value={customerDraft.kind} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, kind: e.target.value }))}>
              <MenuItem value="lead">Lead</MenuItem>
              <MenuItem value="customer">Customer</MenuItem>
            </TextField>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCustomerDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveCustomer}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={inventoryDialogOpen} onClose={() => setInventoryDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Producto</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="SKU" value={inventoryDraft.sku} onChange={(e) => setInventoryDraft((prev) => ({ ...prev, sku: e.target.value }))} />
            <TextField label="Nombre" value={inventoryDraft.name} onChange={(e) => setInventoryDraft((prev) => ({ ...prev, name: e.target.value }))} />
            <Grid container spacing={2}>
              <Grid item xs={12} md={6}>
                <TextField select label="Tipo" fullWidth value={inventoryDraft.itemType} onChange={(e) => setInventoryDraft((prev) => ({
                  ...prev,
                  itemType: e.target.value,
                  tracksInventory: ['physical', 'combo', 'kit'].includes(e.target.value) ? prev.tracksInventory : false,
                  unitOfMeasure: e.target.value === 'service' && prev.unitOfMeasure === 'unit' ? 'hour' : prev.unitOfMeasure,
                }))}>
                  <MenuItem value="physical">Fisico</MenuItem>
                  <MenuItem value="intangible">Intangible</MenuItem>
                  <MenuItem value="service">Servicio</MenuItem>
                  <MenuItem value="combo">Combo</MenuItem>
                  <MenuItem value="kit">Kit</MenuItem>
                </TextField>
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField select label="Unidad" fullWidth value={inventoryDraft.unitOfMeasure} onChange={(e) => setInventoryDraft((prev) => ({ ...prev, unitOfMeasure: e.target.value }))}>
                  <MenuItem value="unit">Unidad</MenuItem>
                  <MenuItem value="set">Set</MenuItem>
                  <MenuItem value="pack">Pack</MenuItem>
                  <MenuItem value="box">Caja</MenuItem>
                  <MenuItem value="hour">Hora</MenuItem>
                  <MenuItem value="day">Dia</MenuItem>
                  <MenuItem value="week">Semana</MenuItem>
                  <MenuItem value="month">Mes</MenuItem>
                  <MenuItem value="minute">Minuto</MenuItem>
                  <MenuItem value="kg">Kilogramo</MenuItem>
                  <MenuItem value="g">Gramo</MenuItem>
                  <MenuItem value="lb">Libra</MenuItem>
                  <MenuItem value="liter">Litro</MenuItem>
                  <MenuItem value="ml">Mililitro</MenuItem>
                  <MenuItem value="meter">Metro</MenuItem>
                  <MenuItem value="cm">Centimetro</MenuItem>
                </TextField>
              </Grid>
            </Grid>
            <Grid container spacing={2}>
              <Grid item xs={12} md={6}>
                <TextField label="Precio" fullWidth value={inventoryDraft.unitPrice} onChange={(e) => setInventoryDraft((prev) => ({ ...prev, unitPrice: e.target.value }))} />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField label="Stock" fullWidth value={inventoryDraft.onHand} disabled={!inventoryDraft.tracksInventory} helperText={inventoryDraft.tracksInventory ? 'Disponible para venta.' : 'No aplica para servicios o intangibles.'} onChange={(e) => setInventoryDraft((prev) => ({ ...prev, onHand: e.target.value }))} />
              </Grid>
            </Grid>
            <Divider />
            <Button variant={inventoryDraft.tracksInventory ? 'contained' : 'outlined'} onClick={() => setInventoryDraft((prev) => ({ ...prev, tracksInventory: !prev.tracksInventory, onHand: !prev.tracksInventory ? prev.onHand : '0' }))} sx={{ alignSelf: 'flex-start' }}>
              {inventoryDraft.tracksInventory ? 'Controla inventario' : 'Sin control de inventario'}
            </Button>
            <Button variant={inventoryDraft.active ? 'contained' : 'outlined'} onClick={() => setInventoryDraft((prev) => ({ ...prev, active: !prev.active }))} sx={{ alignSelf: 'flex-start' }}>
              {inventoryDraft.active ? 'Activo' : 'Inactivo'}
            </Button>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setInventoryDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveInventory}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={inventoryAdjustmentOpen} onClose={() => setInventoryAdjustmentOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Ajustar inventario</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="SKU" value={inventoryAdjustmentDraft.sku} disabled />
            <TextField label="Delta" value={inventoryAdjustmentDraft.delta} onChange={(e) => setInventoryAdjustmentDraft((prev) => ({ ...prev, delta: e.target.value }))} helperText="Usa positivo para ingreso y negativo para salida." />
            <TextField label="Razon" value={inventoryAdjustmentDraft.reason} onChange={(e) => setInventoryAdjustmentDraft((prev) => ({ ...prev, reason: e.target.value }))} />
            <TextField label="Referencia" value={inventoryAdjustmentDraft.referenceId} onChange={(e) => setInventoryAdjustmentDraft((prev) => ({ ...prev, referenceId: e.target.value }))} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setInventoryAdjustmentOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveInventoryAdjustment}>Aplicar ajuste</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={invoiceDialogOpen} onClose={() => setInvoiceDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Editar factura</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Numero" value={invoiceDraft.number} onChange={(e) => setInvoiceDraft((prev) => ({ ...prev, number: e.target.value }))} />
            <Grid container spacing={2}>
              <Grid item xs={12} md={6}>
                <TextField label="Total" fullWidth value={invoiceDraft.total} onChange={(e) => setInvoiceDraft((prev) => ({ ...prev, total: e.target.value }))} />
              </Grid>
              <Grid item xs={12} md={6}>
                <TextField label="Moneda" fullWidth value={invoiceDraft.currency} onChange={(e) => setInvoiceDraft((prev) => ({ ...prev, currency: e.target.value }))} />
              </Grid>
            </Grid>
            <TextField select label="Estado" value={invoiceDraft.status} onChange={(e) => setInvoiceDraft((prev) => ({ ...prev, status: e.target.value }))}>
              <MenuItem value="issued">issued</MenuItem>
              <MenuItem value="paid">paid</MenuItem>
              <MenuItem value="void">void</MenuItem>
            </TextField>
            <TextField type="datetime-local" label="Emitida" value={invoiceDraft.issuedAt} onChange={(e) => setInvoiceDraft((prev) => ({ ...prev, issuedAt: e.target.value }))} InputLabelProps={{ shrink: true }} />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setInvoiceDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveInvoice}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={invoicePreviewOpen} onClose={() => setInvoicePreviewOpen(false)} maxWidth="lg" fullWidth>
        <DialogTitle>Vista previa PDF</DialogTitle>
        <DialogContent>
          {invoicePreviewUrl ? (
            <Box component="iframe" src={invoicePreviewUrl} sx={{ width: '100%', height: '75vh', border: 0 }} />
          ) : (
            <Typography color="text.secondary">Generando PDF...</Typography>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setInvoicePreviewOpen(false)}>Cerrar</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

import { Helmet } from 'react-helmet-async';
import { useMemo, useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Switch from '@mui/material/Switch';
import Dialog from '@mui/material/Dialog';
import { DataGrid } from '@mui/x-data-grid';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ButtonBase from '@mui/material/ButtonBase';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';

import { paths } from 'src/routes/paths';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

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
  description?: string;
  itemType: string;
  unitOfMeasure: string;
  tracksInventory: boolean;
  unitPrice: number;
  onHand: number;
  active: boolean;
  categoryIds: string[];
  branchIds: string[];
  imageUrls: string[];
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

type CategoryRow = {
  id: string;
  name: string;
  description?: string;
  parentCategoryId?: string;
  sortOrder: number;
  active: boolean;
  createdAt: string;
  updatedAt: string;
};

type BranchRow = {
  id: string;
  code: string;
  name: string;
  address?: string;
  phone?: string;
  active: boolean;
  properties?: Record<string, string>;
  createdAt: string;
  updatedAt: string;
};

type OrderRow = {
  id: string;
  partyId: string;
  currency: string;
  total: number;
  status: string;
  notes?: string;
  itemsCount: number;
  createdAt: string;
};

type OrderDetail = OrderRow & {
  sessionId?: string;
  threadId?: string;
  items: Array<{
    sku: string;
    name: string;
    unitPrice: number;
    quantity: number;
  }>;
};

type StoreSettings = {
  storeName: string;
  storeId: string;
  apiToken: string;
  currency: string;
  language: string;
  taxRate: number;
  usePerProductTax: boolean;
  hideOutOfStockProducts: boolean;
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
  description: string;
  itemType: string;
  unitOfMeasure: string;
  tracksInventory: boolean;
  unitPrice: string;
  onHand: string;
  active: boolean;
  categoryIds: string[];
  branchIds: string[];
  imageUrls: string[];
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

type CategoryDraft = {
  name: string;
  description: string;
  parentCategoryId: string;
  sortOrder: string;
  active: boolean;
};

type BranchDraft = {
  code: string;
  name: string;
  address: string;
  phone: string;
  active: boolean;
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
  description: '',
  itemType: 'physical',
  unitOfMeasure: 'unit',
  tracksInventory: true,
  unitPrice: '',
  onHand: '',
  active: true,
  categoryIds: [],
  branchIds: [],
  imageUrls: [],
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

const EMPTY_CATEGORY: CategoryDraft = {
  name: '',
  description: '',
  parentCategoryId: '',
  sortOrder: '0',
  active: true,
};

const EMPTY_BRANCH: BranchDraft = {
  code: '',
  name: '',
  address: '',
  phone: '',
  active: true,
};

const DEFAULT_STORE_SETTINGS: StoreSettings = {
  storeName: 'Ventas y cobros',
  storeId: '',
  apiToken: '',
  currency: 'USD',
  language: 'es',
  taxRate: 0,
  usePerProductTax: false,
  hideOutOfStockProducts: false,
};

const MODULE_IDS = {
  communicationInbox: 'communication-inbox',
  inventory: 'inventory',
  salesPos: 'sales-pos',
  billing: 'billing',
} as const;

const tabMeta = {
  customers: {
    label: 'Clientes',
    description: 'Clientes, leads y datos base para venta asistida.',
    icon: 'mdi:account-group-outline',
  },
  inventory: {
    label: 'Catalogo',
    description: 'Productos y servicios disponibles para vender.',
    icon: 'mdi:package-variant-closed',
  },
  categories: {
    label: 'Categorias',
    description: 'Organiza el catalogo y agrupa productos para venta.',
    icon: 'mdi:shape-outline',
  },
  branches: {
    label: 'Sucursales',
    description: 'Ubicaciones operativas donde se venden productos o servicios.',
    icon: 'mdi:store-marker-outline',
  },
  movements: {
    label: 'Movimientos',
    description: 'Entradas, salidas y ajustes de inventario.',
    icon: 'mdi:swap-horizontal-bold',
  },
  orders: {
    label: 'Pedidos',
    description: 'Carritos, pedidos y seguimiento previo a la venta final.',
    icon: 'mdi:cart-outline',
  },
  sales: {
    label: 'Ventas',
    description: 'Operaciones comerciales creadas por el equipo o por automatizacion.',
    icon: 'mdi:cash-register',
  },
  invoices: {
    label: 'Facturacion',
    description: 'Facturas, estado de cobro y vista previa PDF.',
    icon: 'mdi:receipt-text-outline',
  },
  settings: {
    label: 'Configuracion',
    description: 'Preferencias de tienda, impuestos base y token operativo.',
    icon: 'mdi:cog-outline',
  },
} as const;

export default function CommerceAdminPage() {
  const tenantId = useTenantId();
  type CommerceTab = keyof typeof tabMeta;

  const [tab, setTab] = useState<CommerceTab>('customers');
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
  const [inventoryEditingSku, setInventoryEditingSku] = useState<string | null>(null);
  const [inventoryImageUrlInput, setInventoryImageUrlInput] = useState('');
  const [inventoryPreviewImageIndex, setInventoryPreviewImageIndex] = useState(0);
  const [inventoryAdjustmentDraft, setInventoryAdjustmentDraft] = useState<InventoryAdjustmentDraft>(EMPTY_ADJUSTMENT);
  const [inventoryAdjustmentOpen, setInventoryAdjustmentOpen] = useState(false);

  const [categories, setCategories] = useState<CategoryRow[]>([]);
  const [categoriesTotal, setCategoriesTotal] = useState(0);
  const [categoryQuery, setCategoryQuery] = useState('');
  const [categoryPage, setCategoryPage] = useState(0);
  const [categoryPageSize, setCategoryPageSize] = useState(25);
  const [selectedCategory, setSelectedCategory] = useState<CategoryRow | null>(null);
  const [categoryDraft, setCategoryDraft] = useState<CategoryDraft>(EMPTY_CATEGORY);
  const [categoryDialogOpen, setCategoryDialogOpen] = useState(false);

  const [branches, setBranches] = useState<BranchRow[]>([]);
  const [branchesTotal, setBranchesTotal] = useState(0);
  const [branchQuery, setBranchQuery] = useState('');
  const [branchPage, setBranchPage] = useState(0);
  const [branchPageSize, setBranchPageSize] = useState(25);
  const [selectedBranch, setSelectedBranch] = useState<BranchRow | null>(null);
  const [branchDraft, setBranchDraft] = useState<BranchDraft>(EMPTY_BRANCH);
  const [branchDialogOpen, setBranchDialogOpen] = useState(false);

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

  const [orders, setOrders] = useState<OrderRow[]>([]);
  const [ordersTotal, setOrdersTotal] = useState(0);
  const [ordersPage, setOrdersPage] = useState(0);
  const [ordersPageSize, setOrdersPageSize] = useState(25);
  const [orderStatusFilter, setOrderStatusFilter] = useState('');
  const [selectedOrder, setSelectedOrder] = useState<OrderDetail | null>(null);
  const [orderDrawerOpen, setOrderDrawerOpen] = useState(false);
  const [orderDraftStatus, setOrderDraftStatus] = useState('draft');
  const [orderDraftNotes, setOrderDraftNotes] = useState('');

  const [storeSettings, setStoreSettings] = useState<StoreSettings>(DEFAULT_STORE_SETTINGS);

  const customersEnabled = Boolean(enabledModules[MODULE_IDS.communicationInbox]);
  const inventoryEnabled = Boolean(enabledModules[MODULE_IDS.inventory]);
  const salesEnabled = Boolean(enabledModules[MODULE_IDS.salesPos]);
  const billingEnabled = Boolean(enabledModules[MODULE_IDS.billing]);

  const availableTabs = useMemo(() => {
    const tabs: Array<{ value: CommerceTab; label: string }> = [];
    if (customersEnabled) tabs.push({ value: 'customers', label: 'Clientes' });
    if (inventoryEnabled) tabs.push({ value: 'inventory', label: 'Catalogo' });
    if (inventoryEnabled) tabs.push({ value: 'categories', label: 'Categorias' });
    if (inventoryEnabled) tabs.push({ value: 'branches', label: 'Sucursales' });
    if (inventoryEnabled) tabs.push({ value: 'movements', label: 'Movimientos' });
    if (salesEnabled) tabs.push({ value: 'orders', label: 'Pedidos' });
    if (salesEnabled) tabs.push({ value: 'sales', label: 'Ventas' });
    if (billingEnabled) tabs.push({ value: 'invoices', label: 'Facturacion' });
    if (inventoryEnabled) tabs.push({ value: 'settings', label: 'Configuracion' });
    return tabs;
  }, [billingEnabled, customersEnabled, inventoryEnabled, salesEnabled]);

  const commerceStats = useMemo(
    () => [
      {
        label: 'Clientes',
        value: customersTotal,
        helper: customers.filter((row) => row.kind === 'lead').length > 0
          ? `${customers.filter((row) => row.kind === 'lead').length} leads en la vista actual`
          : 'Base comercial activa',
        icon: 'mdi:account-group-outline',
      },
      {
        label: 'Catalogo',
        value: inventory.filter((row) => row.active).length,
        helper: inventory.filter((row) => row.tracksInventory && row.onHand <= 5).length > 0
          ? `${inventory.filter((row) => row.tracksInventory && row.onHand <= 5).length} con stock bajo`
          : 'Productos activos',
        icon: 'mdi:package-variant-closed',
      },
      {
        label: 'Ventas',
        value: salesTotal,
        helper: sales.filter((row) => row.state === 'paid').length > 0
          ? `${sales.filter((row) => row.state === 'paid').length} cobradas en la vista actual`
          : 'Seguimiento comercial',
        icon: 'mdi:cash-register',
      },
      {
        label: 'Facturas',
        value: invoicesTotal,
        helper: invoices.filter((row) => row.status !== 'paid').length > 0
          ? `${invoices.filter((row) => row.status !== 'paid').length} pendientes en la vista actual`
          : 'Cartera al dia',
        icon: 'mdi:receipt-text-outline',
      },
    ],
    [customers, customersTotal, inventory, invoices, invoicesTotal, sales, salesTotal]
  );

  const activeTabMeta = tabMeta[tab];

  const refreshAll = async () => {
    const tasks: Array<Promise<void>> = [];
    if (customersEnabled) tasks.push(loadCustomers());
    if (inventoryEnabled) tasks.push(loadInventory(), loadCategories(), loadBranches(), loadMovements(), loadStoreSettings());
    if (salesEnabled) tasks.push(loadOrders(), loadSales());
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

  const loadCategories = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(categoryPage),
        pageSize: String(categoryPageSize),
      });
      if (categoryQuery.trim()) qs.set('query', categoryQuery.trim());
      const res = await axios.get(`${endpoints.agentflow.commerce.categories(tenantId)}?${qs.toString()}`);
      setCategories(res.data?.items ?? []);
      setCategoriesTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar las categorias.');
    }
  };

  const loadBranches = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(branchPage),
        pageSize: String(branchPageSize),
      });
      if (branchQuery.trim()) qs.set('query', branchQuery.trim());
      const res = await axios.get(`${endpoints.agentflow.commerce.branches(tenantId)}?${qs.toString()}`);
      setBranches(res.data?.items ?? []);
      setBranchesTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar las sucursales.');
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

  const loadOrders = async () => {
    try {
      const qs = new URLSearchParams({
        page: String(ordersPage),
        pageSize: String(ordersPageSize),
      });
      if (orderStatusFilter) qs.set('status', orderStatusFilter);
      const res = await axios.get(`${endpoints.agentflow.commerce.ordersSearch(tenantId)}?${qs.toString()}`);
      setOrders(res.data?.items ?? []);
      setOrdersTotal(res.data?.total ?? 0);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudieron cargar los pedidos.');
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

  const loadStoreSettings = async () => {
    try {
      const res = await axios.get(endpoints.agentflow.commerce.storeSettings(tenantId));
      setStoreSettings({
        storeName: res.data?.storeName ?? DEFAULT_STORE_SETTINGS.storeName,
        storeId: res.data?.storeId ?? '',
        apiToken: res.data?.apiToken ?? '',
        currency: res.data?.currency ?? 'USD',
        language: res.data?.language ?? 'es',
        taxRate: Number(res.data?.taxRate ?? 0),
        usePerProductTax: Boolean(res.data?.usePerProductTax),
        hideOutOfStockProducts: Boolean(res.data?.hideOutOfStockProducts),
      });
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar la configuracion de tienda.');
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
    if (!inventoryEnabled) return undefined;
    const timeout = setTimeout(() => { loadCategories(); }, 250);
    return () => clearTimeout(timeout);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, categoryQuery, categoryPage, categoryPageSize, inventoryEnabled]);

  useEffect(() => {
    if (!inventoryEnabled) return undefined;
    const timeout = setTimeout(() => { loadBranches(); }, 250);
    return () => clearTimeout(timeout);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, branchQuery, branchPage, branchPageSize, inventoryEnabled]);

  useEffect(() => {
    if (!inventoryEnabled) return;
    loadMovements();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, movementSku, movementPage, movementPageSize, inventoryEnabled]);

  useEffect(() => {
    if (!salesEnabled) return;
    loadOrders();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, ordersPage, ordersPageSize, orderStatusFilter, salesEnabled]);

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

  useEffect(() => {
    if (!inventoryEnabled) return;
    loadStoreSettings();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, inventoryEnabled]);

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
      setInventoryEditingSku(row.sku);
      setInventoryDraft({
        sku: row.sku,
        name: row.name,
        description: row.description ?? '',
        itemType: row.itemType ?? 'physical',
        unitOfMeasure: row.unitOfMeasure ?? 'unit',
        tracksInventory: row.tracksInventory ?? true,
        unitPrice: String(row.unitPrice),
        onHand: String(row.onHand),
        active: row.active,
        categoryIds: row.categoryIds ?? [],
        branchIds: row.branchIds ?? [],
        imageUrls: row.imageUrls ?? [],
      });
      setInventoryImageUrlInput('');
      setInventoryPreviewImageIndex(0);
    } else {
      setInventoryEditingSku(null);
      setInventoryDraft(EMPTY_INVENTORY);
      setInventoryImageUrlInput('');
      setInventoryPreviewImageIndex(0);
    }
    setInventoryDialogOpen(true);
  };

  const saveInventory = async () => {
    try {
      await axios.put(endpoints.agentflow.commerce.inventoryItemBySku(tenantId, inventoryDraft.sku), {
        name: inventoryDraft.name,
        description: inventoryDraft.description || undefined,
        itemType: inventoryDraft.itemType,
        unitOfMeasure: inventoryDraft.unitOfMeasure,
        tracksInventory: inventoryDraft.tracksInventory,
        unitPrice: Number(inventoryDraft.unitPrice || 0),
        onHand: inventoryDraft.tracksInventory ? Number(inventoryDraft.onHand || 0) : 0,
        active: inventoryDraft.active,
        categoryIds: inventoryDraft.categoryIds,
        branchIds: inventoryDraft.branchIds,
        imageUrls: inventoryDraft.imageUrls,
      });
      setActionOk('Producto guardado.');
      setInventoryDialogOpen(false);
      setInventoryEditingSku(null);
      await loadInventory();
      await Promise.all([loadCategories(), loadBranches()]);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar el producto.');
    }
  };

  const openCategoryDialog = (row?: CategoryRow) => {
    setSelectedCategory(row ?? null);
    setCategoryDraft(row ? {
      name: row.name,
      description: row.description ?? '',
      parentCategoryId: row.parentCategoryId ?? '',
      sortOrder: String(row.sortOrder ?? 0),
      active: row.active,
    } : EMPTY_CATEGORY);
    setCategoryDialogOpen(true);
  };

  const saveCategory = async () => {
    try {
      const payload = {
        name: categoryDraft.name,
        description: categoryDraft.description || undefined,
        parentCategoryId: categoryDraft.parentCategoryId || undefined,
        sortOrder: Number(categoryDraft.sortOrder || 0),
        active: categoryDraft.active,
      };
      if (selectedCategory) {
        await axios.put(endpoints.agentflow.commerce.categoryById(tenantId, selectedCategory.id), payload);
      } else {
        await axios.post(endpoints.agentflow.commerce.categories(tenantId), payload);
      }
      setActionOk(selectedCategory ? 'Categoria actualizada.' : 'Categoria creada.');
      setCategoryDialogOpen(false);
      setSelectedCategory(null);
      await Promise.all([loadCategories(), loadInventory()]);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar la categoria.');
    }
  };

  const deleteCategory = async (categoryId: string) => {
    try {
      await axios.delete(endpoints.agentflow.commerce.categoryById(tenantId, categoryId));
      setActionOk('Categoria eliminada.');
      await Promise.all([loadCategories(), loadInventory()]);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo eliminar la categoria.');
    }
  };

  const openBranchDialog = (row?: BranchRow) => {
    setSelectedBranch(row ?? null);
    setBranchDraft(row ? {
      code: row.code,
      name: row.name,
      address: row.address ?? '',
      phone: row.phone ?? '',
      active: row.active,
    } : EMPTY_BRANCH);
    setBranchDialogOpen(true);
  };

  const saveBranch = async () => {
    try {
      const payload = {
        code: branchDraft.code,
        name: branchDraft.name,
        address: branchDraft.address || undefined,
        phone: branchDraft.phone || undefined,
        active: branchDraft.active,
        properties: {},
      };
      if (selectedBranch) {
        await axios.put(endpoints.agentflow.commerce.branchById(tenantId, selectedBranch.id), payload);
      } else {
        await axios.post(endpoints.agentflow.commerce.branches(tenantId), payload);
      }
      setActionOk(selectedBranch ? 'Sucursal actualizada.' : 'Sucursal creada.');
      setBranchDialogOpen(false);
      setSelectedBranch(null);
      await Promise.all([loadBranches(), loadInventory()]);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar la sucursal.');
    }
  };

  const deleteBranch = async (branchId: string) => {
    try {
      await axios.delete(endpoints.agentflow.commerce.branchById(tenantId, branchId));
      setActionOk('Sucursal eliminada.');
      await Promise.all([loadBranches(), loadInventory()]);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo eliminar la sucursal.');
    }
  };

  const saveStoreSettings = async () => {
    try {
      await axios.put(endpoints.agentflow.commerce.storeSettings(tenantId), {
        storeName: storeSettings.storeName,
        currency: storeSettings.currency,
        language: storeSettings.language,
        taxRate: Number(storeSettings.taxRate || 0),
        usePerProductTax: storeSettings.usePerProductTax,
        hideOutOfStockProducts: storeSettings.hideOutOfStockProducts,
      });
      setActionOk('Configuracion de tienda actualizada.');
      await loadStoreSettings();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo guardar la configuracion de tienda.');
    }
  };

  const openOrderDrawer = async (orderId: string) => {
    try {
      const res = await axios.get(endpoints.agentflow.commerce.orderById(tenantId, orderId));
      const order = res.data as OrderDetail;
      setSelectedOrder({
        ...order,
        itemsCount: order.items?.length ?? order.itemsCount ?? 0,
      });
      setOrderDraftStatus(order.status ?? 'draft');
      setOrderDraftNotes(order.notes ?? '');
      setOrderDrawerOpen(true);
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo cargar el pedido.');
    }
  };

  const saveOrder = async () => {
    if (!selectedOrder) return;
    try {
      await axios.put(endpoints.agentflow.commerce.orderById(tenantId, selectedOrder.id), {
        status: orderDraftStatus,
        notes: orderDraftNotes,
      });
      setActionOk('Pedido actualizado.');
      setOrderDrawerOpen(false);
      await loadOrders();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo actualizar el pedido.');
    }
  };

  const regenerateStoreToken = async () => {
    try {
      await axios.put(endpoints.agentflow.commerce.storeSettings(tenantId), { regenerateApiToken: true });
      setActionOk('Token regenerado.');
      await loadStoreSettings();
    } catch (e: any) {
      setActionError(e?.message ?? 'No se pudo regenerar el token.');
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

  const categoryLookup = useMemo(
    () => Object.fromEntries(categories.map((category) => [category.id, category])),
    [categories]
  );

  const categoryDepth = useMemo(() => {
    const cache: Record<string, number> = {};

    const resolveDepth = (categoryId: string, stack = new Set<string>()): number => {
      if (cache[categoryId] !== undefined) return cache[categoryId];
      if (stack.has(categoryId)) return 0;
      const category = categoryLookup[categoryId];
      if (!category?.parentCategoryId || !categoryLookup[category.parentCategoryId]) {
        cache[categoryId] = 0;
        return 0;
      }
      stack.add(categoryId);
      const depth = resolveDepth(category.parentCategoryId, stack) + 1;
      stack.delete(categoryId);
      cache[categoryId] = depth;
      return depth;
    };

    categories.forEach((category) => resolveDepth(category.id));
    return cache;
  }, [categories, categoryLookup]);

  const inventoryPreviewImage = inventoryDraft.imageUrls[inventoryPreviewImageIndex] ?? inventoryDraft.imageUrls[0] ?? '';

  const inventoryPreviewPrice = Number(inventoryDraft.unitPrice || 0).toFixed(2);
  const inventorySupportsStock = inventoryDraft.tracksInventory;

  const gridCardSx = {
    height: 560,
    '& .MuiDataGrid-columnHeaders': { bgcolor: 'background.neutral' },
    '& .MuiDataGrid-cell': { alignItems: 'center' },
  };

  return (
    <>
      <Helmet>
        <title>Ventas y cobros | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <BrandPageHeader
          eyebrow="Operacion comercial"
          title="Ventas y cobros"
          description="Centraliza clientes, catalogo, movimientos, ventas y facturacion en una sola superficie de trabajo conectada con conversaciones y automatizaciones."
          icon="mdi:store-cog-outline"
          meta={
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              <Chip size="small" color={customersEnabled ? 'success' : 'default'} label={customersEnabled ? 'CRM activo' : 'CRM pendiente'} />
              <Chip size="small" color={inventoryEnabled ? 'success' : 'default'} label={inventoryEnabled ? 'Inventario activo' : 'Inventario pendiente'} />
              <Chip size="small" color={salesEnabled ? 'success' : 'default'} label={salesEnabled ? 'Ventas activas' : 'Ventas pendientes'} />
              <Chip size="small" color={billingEnabled ? 'success' : 'default'} label={billingEnabled ? 'Facturacion activa' : 'Facturacion pendiente'} />
            </Stack>
          }
          actions={
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button variant="outlined" href={paths.dashboard.threads} startIcon={<Iconify icon="mdi:forum-outline" />}>
                Abrir bandeja
              </Button>
              <Button variant="contained" onClick={refreshAll} startIcon={<Iconify icon="solar:refresh-line-duotone" />}>
                Actualizar
              </Button>
            </Stack>
          }
        />

        {actionError && <Alert severity="error" sx={{ mb: 2 }}>{actionError}</Alert>}
        {actionOk && <Alert severity="success" sx={{ mb: 2 }}>{actionOk}</Alert>}

        <Grid container spacing={2.5} sx={{ mb: 2.5 }}>
          {commerceStats.map((stat) => (
            <Grid key={stat.label} item xs={12} sm={6} lg={3}>
              <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3, height: '100%' }}>
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Box
                    sx={{
                      width: 42,
                      height: 42,
                      borderRadius: 1.5,
                      display: 'grid',
                      placeItems: 'center',
                      bgcolor: 'primary.lighter',
                      color: 'primary.main',
                    }}
                  >
                    <Iconify icon={stat.icon} width={22} />
                  </Box>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography variant="h5">{stat.value}</Typography>
                    <Typography variant="subtitle2">{stat.label}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {stat.helper}
                    </Typography>
                  </Box>
                </Stack>
              </Card>
            </Grid>
          ))}
        </Grid>

        <Stack direction={{ xs: 'column', lg: 'row' }} spacing={2.5} alignItems="stretch">
          <Card
            variant="outlined"
            sx={{
              width: { xs: '100%', lg: 260 },
              minWidth: { lg: 260 },
              p: 1.5,
              borderRadius: 3,
              alignSelf: { lg: 'flex-start' },
            }}
          >
            <Stack spacing={0.75}>
              {availableTabs.length > 0 ? (
                availableTabs.map((entry) => {
                  const active = entry.value === tab;
                  const meta = tabMeta[entry.value];
                  return (
                    <ButtonBase
                      key={entry.value}
                      onClick={() => setTab(entry.value)}
                      sx={{
                        width: '100%',
                        px: 1.25,
                        py: 1.1,
                        borderRadius: 2,
                        justifyContent: 'flex-start',
                        textAlign: 'left',
                        bgcolor: active ? 'action.selected' : 'transparent',
                        borderLeft: '3px solid',
                        borderColor: active ? 'primary.main' : 'transparent',
                      }}
                    >
                      <Stack direction="row" spacing={1.1} alignItems="center">
                        <Iconify icon={meta.icon} width={18} />
                        <Typography variant="body2" fontWeight={active ? 700 : 600}>
                          {entry.label}
                        </Typography>
                      </Stack>
                    </ButtonBase>
                  );
                })
              ) : (
                <Alert severity="info">No hay modulos de comercio habilitados.</Alert>
              )}
            </Stack>
          </Card>

          <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, flex: 1, minWidth: 0 }}>
          {availableTabs.length > 0 ? (
            <Stack spacing={1.5} sx={{ mb: 2.5 }}>
                <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5, bgcolor: 'background.neutral' }}>
                  <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} justifyContent="space-between" alignItems={{ md: 'center' }}>
                    <Box>
                      <Typography variant="subtitle1">{activeTabMeta.label}</Typography>
                      <Typography variant="body2" color="text.secondary">
                        {activeTabMeta.description}
                      </Typography>
                    </Box>
                    <Chip size="small" icon={<Iconify icon={activeTabMeta.icon} width={14} />} label={availableTabs.findIndex((entry) => entry.value === tab) + 1 + ' de ' + availableTabs.length} />
                  </Stack>
                </Card>
              </Stack>
          ) : (
            <Alert severity="info" sx={{ mb: 2 }}>
              No hay modulos de comercio habilitados para este tenant.
            </Alert>
          )}

          {tab === 'customers' && (
            <Stack spacing={2}>
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Base de clientes</Typography>
                    <Typography variant="h6">{customersTotal}</Typography>
                    <Typography variant="body2" color="text.secondary">Contactos disponibles para venta, cobro y seguimiento.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Leads en pantalla</Typography>
                    <Typography variant="h6">{customers.filter((row) => row.kind === 'lead').length}</Typography>
                    <Typography variant="body2" color="text.secondary">Ayuda a priorizar conversion y seguimiento.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Origen conversacional</Typography>
                    <Typography variant="h6">{new Set(customers.map((row) => row.channel).filter(Boolean)).size}</Typography>
                    <Typography variant="body2" color="text.secondary">Canales base detectados en la vista actual.</Typography>
                  </Card>
                </Grid>
              </Grid>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar por nombre, telefono o email" value={customerQuery} onChange={(e) => { setCustomerQuery(e.target.value); setCustomerPage(0); }} />
                <Button variant="outlined" href={paths.dashboard.threads}>Abrir inbox</Button>
              </Stack>
              <Box sx={gridCardSx}>
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
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Items activos</Typography>
                    <Typography variant="h6">{inventory.filter((row) => row.active).length}</Typography>
                    <Typography variant="body2" color="text.secondary">Catalogo listo para vender.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Con control de stock</Typography>
                    <Typography variant="h6">{inventory.filter((row) => row.tracksInventory).length}</Typography>
                    <Typography variant="body2" color="text.secondary">Productos que requieren entradas y salidas.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Stock bajo</Typography>
                    <Typography variant="h6">{inventory.filter((row) => row.tracksInventory && row.onHand <= 5).length}</Typography>
                    <Typography variant="body2" color="text.secondary">Detectado sobre la lista cargada.</Typography>
                  </Card>
                </Grid>
              </Grid>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar SKU o nombre" value={inventoryQuery} onChange={(e) => setInventoryQuery(e.target.value)} />
                <Button variant="contained" onClick={() => openInventoryDialog()}>Nuevo producto</Button>
              </Stack>
              <Box sx={gridCardSx}>
                <DataGrid
                  rows={inventory}
                  columns={[
                    { field: 'sku', headerName: 'SKU', width: 150 },
                    { field: 'name', headerName: 'Producto', flex: 1, minWidth: 240 },
                    { field: 'itemType', headerName: 'Tipo', width: 120, renderCell: (params) => <Chip size="small" label={params.value} variant="outlined" /> },
                    { field: 'unitOfMeasure', headerName: 'Unidad', width: 110 },
                    { field: 'unitPrice', headerName: 'Precio', width: 120 },
                    { field: 'categoryIds', headerName: 'Categorias', width: 120, valueGetter: (_, row) => row.categoryIds?.length ?? 0 },
                    { field: 'branchIds', headerName: 'Sucursales', width: 120, valueGetter: (_, row) => row.branchIds?.length ?? 0 },
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

          {tab === 'categories' && (
            <Stack spacing={2}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar categoria" value={categoryQuery} onChange={(e) => { setCategoryQuery(e.target.value); setCategoryPage(0); }} />
                <Button variant="contained" onClick={() => openCategoryDialog()}>Nueva categoria</Button>
              </Stack>
              <Box sx={gridCardSx}>
                <DataGrid
                  rows={categories}
                  rowCount={categoriesTotal}
                  paginationMode="server"
                  paginationModel={{ page: categoryPage, pageSize: categoryPageSize }}
                  onPaginationModelChange={(next) => {
                    setCategoryPage(next.page);
                    setCategoryPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    {
                      field: 'name',
                      headerName: 'Categoria',
                      flex: 1,
                      minWidth: 260,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1} alignItems="center" sx={{ pl: `${(categoryDepth[params.row.id] ?? 0) * 2}px` }}>
                          <Box sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: params.row.active ? 'primary.main' : 'text.disabled' }} />
                          <Typography variant="body2" fontWeight={600}>
                            {params.row.name}
                          </Typography>
                        </Stack>
                      ),
                    },
                    { field: 'description', headerName: 'Descripcion', flex: 1, minWidth: 240, valueGetter: (_, row) => row.description || '-' },
                    { field: 'parentCategoryId', headerName: 'Categoria padre', width: 180, valueGetter: (_, row) => (row.parentCategoryId ? categoryLookup[row.parentCategoryId]?.name : '-') || '-' },
                    { field: 'sortOrder', headerName: 'Orden', width: 100 },
                    { field: 'products', headerName: 'Productos', width: 100, valueGetter: (_, row) => inventory.filter((item) => item.categoryIds?.includes(row.id)).length },
                    { field: 'active', headerName: 'Activa', width: 110, renderCell: (params) => <Chip size="small" label={params.value ? 'Si' : 'No'} color={params.value ? 'success' : 'default'} /> },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 200,
                      sortable: false,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1}>
                          <Button size="small" onClick={() => openCategoryDialog(params.row)}>Editar</Button>
                          <Button size="small" color="error" onClick={() => deleteCategory(params.row.id)}>Eliminar</Button>
                        </Stack>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'branches' && (
            <Stack spacing={2}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" fullWidth placeholder="Buscar sucursal por codigo o nombre" value={branchQuery} onChange={(e) => { setBranchQuery(e.target.value); setBranchPage(0); }} />
                <Button variant="contained" onClick={() => openBranchDialog()}>Nueva sucursal</Button>
              </Stack>
              <Box sx={gridCardSx}>
                <DataGrid
                  rows={branches}
                  rowCount={branchesTotal}
                  paginationMode="server"
                  paginationModel={{ page: branchPage, pageSize: branchPageSize }}
                  onPaginationModelChange={(next) => {
                    setBranchPage(next.page);
                    setBranchPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'code', headerName: 'Codigo', width: 150 },
                    { field: 'name', headerName: 'Sucursal', flex: 1, minWidth: 220 },
                    { field: 'address', headerName: 'Direccion', flex: 1, minWidth: 240, valueGetter: (_, row) => row.address || '-' },
                    { field: 'phone', headerName: 'Telefono', width: 150, valueGetter: (_, row) => row.phone || '-' },
                    { field: 'active', headerName: 'Activa', width: 110, renderCell: (params) => <Chip size="small" label={params.value ? 'Si' : 'No'} color={params.value ? 'success' : 'default'} /> },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 200,
                      sortable: false,
                      renderCell: (params) => (
                        <Stack direction="row" spacing={1}>
                          <Button size="small" onClick={() => openBranchDialog(params.row)}>Editar</Button>
                          <Button size="small" color="error" onClick={() => deleteBranch(params.row.id)}>Eliminar</Button>
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
                  <Card variant="outlined" sx={{ p: 1.75, height: '100%', borderRadius: 2.5 }}>
                    <Typography variant="body2" color="text.secondary">Delta acumulado de la pagina actual</Typography>
                    <Typography variant="h6">{movementSummary.totalDelta >= 0 ? '+' : ''}{movementSummary.totalDelta}</Typography>
                  </Card>
                </Grid>
              </Grid>
              <Box sx={gridCardSx}>
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

          {tab === 'orders' && (
            <Stack spacing={2}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField
                  select
                  size="small"
                  label="Estado"
                  value={orderStatusFilter}
                  onChange={(e) => {
                    setOrderStatusFilter(e.target.value);
                    setOrdersPage(0);
                  }}
                  sx={{ width: { xs: '100%', md: 220 } }}
                >
                  <MenuItem value="">Todos</MenuItem>
                  <MenuItem value="draft">Borrador</MenuItem>
                  <MenuItem value="submitted">Enviado</MenuItem>
                  <MenuItem value="confirmed">Confirmado</MenuItem>
                  <MenuItem value="cancelled">Cancelado</MenuItem>
                </TextField>
              </Stack>
              <Box sx={gridCardSx}>
                <DataGrid
                  rows={orders}
                  rowCount={ordersTotal}
                  paginationMode="server"
                  paginationModel={{ page: ordersPage, pageSize: ordersPageSize }}
                  onPaginationModelChange={(next) => {
                    setOrdersPage(next.page);
                    setOrdersPageSize(next.pageSize);
                  }}
                  pageSizeOptions={[10, 25, 50]}
                  columns={[
                    { field: 'id', headerName: 'Pedido', width: 220 },
                    { field: 'partyId', headerName: 'Cliente', width: 220 },
                    { field: 'status', headerName: 'Estado', width: 140, renderCell: (params) => <Chip size="small" label={params.value} color={params.value === 'confirmed' ? 'success' : 'default'} /> },
                    { field: 'itemsCount', headerName: 'Items', width: 90 },
                    { field: 'total', headerName: 'Total', width: 120 },
                    { field: 'currency', headerName: 'Moneda', width: 100 },
                    { field: 'createdAt', headerName: 'Creado', width: 180, valueFormatter: (value) => new Date(value).toLocaleString() },
                    {
                      field: 'actions',
                      headerName: '',
                      width: 140,
                      sortable: false,
                      renderCell: (params) => (
                        <Button size="small" onClick={() => openOrderDrawer(params.row.id)}>
                          Ver detalle
                        </Button>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'sales' && (
            <Stack spacing={2}>
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Ventas registradas</Typography>
                    <Typography variant="h6">{salesTotal}</Typography>
                    <Typography variant="body2" color="text.secondary">Operaciones comerciales encontradas con el filtro actual.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Cobradas en la vista</Typography>
                    <Typography variant="h6">{sales.filter((row) => row.state === 'paid').length}</Typography>
                    <Typography variant="body2" color="text.secondary">Sirve para revisar cierre comercial.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Monto visible</Typography>
                    <Typography variant="h6">
                      {sales.reduce((acc, row) => acc + Number(row.total || 0), 0).toFixed(2)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">Suma de la pagina actual.</Typography>
                  </Card>
                </Grid>
              </Grid>
              <Grid container spacing={1}>
                <Grid item xs={12} md={4}>
                  <TextField select size="small" fullWidth label="Estado" value={salesState} onChange={(e) => { setSalesState(e.target.value); setSalesPage(0); }}>
                    <MenuItem value="">Todos</MenuItem>
                    <MenuItem value="sale_created">Creada</MenuItem>
                    <MenuItem value="invoiced">Facturada</MenuItem>
                    <MenuItem value="paid">Pagada</MenuItem>
                  </TextField>
                </Grid>
              </Grid>
              <Box sx={gridCardSx}>
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
                    { field: 'state', headerName: 'Estado', width: 130, renderCell: (params) => <Chip size="small" label={params.value === 'sale_created' ? 'Creada' : params.value === 'invoiced' ? 'Facturada' : params.value === 'paid' ? 'Pagada' : params.value} color={params.value === 'paid' ? 'success' : 'warning'} /> },
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
              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Facturas registradas</Typography>
                    <Typography variant="h6">{invoicesTotal}</Typography>
                    <Typography variant="body2" color="text.secondary">Documentos disponibles para seguimiento y cobro.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Pendientes en la vista</Typography>
                    <Typography variant="h6">{invoices.filter((row) => row.status !== 'paid').length}</Typography>
                    <Typography variant="body2" color="text.secondary">Ayuda a priorizar cobros y recordatorios.</Typography>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card variant="outlined" sx={{ p: 1.75, borderRadius: 2.5 }}>
                    <Typography variant="caption" color="text.secondary">Monto visible</Typography>
                    <Typography variant="h6">
                      {invoices.reduce((acc, row) => acc + Number(row.total || 0), 0).toFixed(2)}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">Total acumulado de la pagina actual.</Typography>
                  </Card>
                </Grid>
              </Grid>
              <Grid container spacing={1}>
                <Grid item xs={12} md={4}>
                  <TextField select size="small" fullWidth label="Estado" value={invoiceStatusFilter} onChange={(e) => { setInvoiceStatusFilter(e.target.value); setInvoicePage(0); }}>
                    <MenuItem value="">Todos</MenuItem>
                    <MenuItem value="issued">Emitida</MenuItem>
                    <MenuItem value="paid">Pagada</MenuItem>
                    <MenuItem value="void">Anulada</MenuItem>
                  </TextField>
                </Grid>
              </Grid>
              <Box sx={gridCardSx}>
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
                    { field: 'status', headerName: 'Estado', width: 120, renderCell: (params) => <Chip size="small" label={params.value === 'issued' ? 'Emitida' : params.value === 'paid' ? 'Pagada' : params.value === 'void' ? 'Anulada' : params.value} color={params.value === 'paid' ? 'success' : 'secondary'} /> },
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
                          <Button size="small" color="secondary" onClick={() => openInvoicePreview(params.row.id)}>Ver PDF</Button>
                        </Stack>
                      ),
                    },
                  ]}
                  disableRowSelectionOnClick
                />
              </Box>
            </Stack>
          )}

          {tab === 'settings' && (
            <Stack spacing={2}>
              <Grid container spacing={2}>
                <Grid item xs={12} lg={7}>
                  <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                    <Stack spacing={2}>
                      <Typography variant="h6">Informacion de tienda</Typography>
                      <TextField label="Nombre comercial" value={storeSettings.storeName} onChange={(e) => setStoreSettings((prev) => ({ ...prev, storeName: e.target.value }))} />
                      <Grid container spacing={2}>
                        <Grid item xs={12} md={6}>
                          <TextField label="Moneda" value={storeSettings.currency} onChange={(e) => setStoreSettings((prev) => ({ ...prev, currency: e.target.value.toUpperCase() }))} />
                        </Grid>
                        <Grid item xs={12} md={6}>
                          <TextField select label="Idioma" value={storeSettings.language} onChange={(e) => setStoreSettings((prev) => ({ ...prev, language: e.target.value }))}>
                            <MenuItem value="es">Español</MenuItem>
                            <MenuItem value="en">English</MenuItem>
                          </TextField>
                        </Grid>
                      </Grid>
                    </Stack>
                  </Card>
                </Grid>
                <Grid item xs={12} lg={5}>
                  <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                    <Stack spacing={2}>
                      <Typography variant="h6">Identificadores y token</Typography>
                      <TextField label="Store ID" value={storeSettings.storeId} disabled />
                      <TextField label="Token API" value={storeSettings.apiToken} disabled multiline minRows={2} />
                      <Button variant="outlined" onClick={regenerateStoreToken}>Regenerar token</Button>
                    </Stack>
                  </Card>
                </Grid>
              </Grid>
              <Grid container spacing={2}>
                <Grid item xs={12} md={6}>
                  <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                    <Stack spacing={2}>
                      <Typography variant="h6">Impuestos</Typography>
                      <TextField
                        label="Tasa global de impuesto"
                        value={storeSettings.taxRate}
                        onChange={(e) => setStoreSettings((prev) => ({ ...prev, taxRate: Number(e.target.value || 0) }))}
                      />
                      <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                          <Box>
                            <Typography variant="subtitle2">Impuesto por producto</Typography>
                            <Typography variant="caption" color="text.secondary">Permite que cada producto maneje una tasa propia.</Typography>
                          </Box>
                          <Switch checked={storeSettings.usePerProductTax} onChange={() => setStoreSettings((prev) => ({ ...prev, usePerProductTax: !prev.usePerProductTax }))} />
                        </Stack>
                      </Card>
                    </Stack>
                  </Card>
                </Grid>
                <Grid item xs={12} md={6}>
                  <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                    <Stack spacing={2}>
                      <Typography variant="h6">Preferencias</Typography>
                      <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                          <Box>
                            <Typography variant="subtitle2">Ocultar productos sin stock</Typography>
                            <Typography variant="caption" color="text.secondary">Afecta experiencias futuras de catalogo y carrito.</Typography>
                          </Box>
                          <Switch checked={storeSettings.hideOutOfStockProducts} onChange={() => setStoreSettings((prev) => ({ ...prev, hideOutOfStockProducts: !prev.hideOutOfStockProducts }))} />
                        </Stack>
                      </Card>
                      <Button variant="contained" onClick={saveStoreSettings}>Guardar configuracion</Button>
                    </Stack>
                  </Card>
                </Grid>
              </Grid>
            </Stack>
          )}
          </Card>
        </Stack>
      </DashboardContent>

      <Dialog open={customerDialogOpen} onClose={() => setCustomerDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>
          <Typography variant="h6">Editar cliente</Typography>
          <Typography variant="body2" color="text.secondary">
            Actualiza datos de contacto y tipo comercial.
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Alias" value={customerDraft.displayName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, displayName: e.target.value }))} />
            <TextField label="Nombre completo" value={customerDraft.fullName} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, fullName: e.target.value }))} />
            <TextField label="Telefono" value={customerDraft.phone} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, phone: e.target.value }))} />
            <TextField label="Email" value={customerDraft.email} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, email: e.target.value }))} />
            <TextField select label="Tipo" value={customerDraft.kind} onChange={(e) => setCustomerDraft((prev) => ({ ...prev, kind: e.target.value }))}>
              <MenuItem value="lead">Lead</MenuItem>
              <MenuItem value="customer">Cliente</MenuItem>
            </TextField>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCustomerDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveCustomer}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Drawer
        anchor="right"
        open={inventoryDialogOpen}
        onClose={() => {
          setInventoryDialogOpen(false);
          setInventoryEditingSku(null);
        }}
        PaperProps={{ sx: { width: { xs: '100%', md: 1120 }, maxWidth: '100%' } }}
      >
        <Stack sx={{ height: '100%' }}>
          <Box sx={{ px: 3, py: 2.5, borderBottom: '1px solid', borderColor: 'divider' }}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} justifyContent="space-between" alignItems={{ md: 'center' }}>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Productos
                </Typography>
                <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.25 }}>
                  <Typography variant="h4">
                    {inventoryEditingSku ? 'Editar producto' : 'Nuevo producto'}
                  </Typography>
                  <Chip size="small" color={inventoryDraft.active ? 'success' : 'default'} label={inventoryDraft.active ? 'Activo' : 'Inactivo'} />
                </Stack>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>
                  Configura lo que hoy soporta el catalogo comercial: nombre, SKU, tipo, unidad, precio y control de inventario.
                </Typography>
              </Box>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                <Button
                  variant="text"
                  onClick={() => {
                    setInventoryDraft(EMPTY_INVENTORY);
                    setInventoryEditingSku(null);
                  }}
                >
                  Limpiar cambios
                </Button>
                <Button variant="contained" onClick={saveInventory}>
                  Guardar producto
                </Button>
              </Stack>
            </Stack>
          </Box>

          <Stack direction={{ xs: 'column', lg: 'row' }} spacing={0} sx={{ flex: 1, minHeight: 0 }}>
            <Box sx={{ flex: 1, minWidth: 0, overflow: 'auto', p: 3 }}>
              <Stack spacing={2}>
                <Card variant="outlined" sx={{ borderRadius: 3 }}>
                  <Box sx={{ p: 2.25, borderBottom: '1px solid', borderColor: 'divider' }}>
                    <Typography variant="h6">Detalles del producto</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Lo minimo necesario para que el producto exista en tu catalogo.
                    </Typography>
                  </Box>
                  <Stack spacing={2} sx={{ p: 2.25 }}>
                    <TextField
                      label="Nombre del producto"
                      value={inventoryDraft.name}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, name: e.target.value }))}
                    />
                    <TextField
                      label="SKU"
                      value={inventoryDraft.sku}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, sku: e.target.value }))}
                      helperText="Identificador unico del producto."
                    />
                    <TextField
                      label="Descripcion operativa"
                      value={inventoryDraft.description}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, description: e.target.value }))}
                      multiline
                      minRows={4}
                      placeholder="Resumen comercial del producto."
                    />
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
                    <TextField
                      label="Precio"
                      value={inventoryDraft.unitPrice}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, unitPrice: e.target.value }))}
                      InputProps={{ startAdornment: <Box sx={{ mr: 1, color: 'text.secondary' }}>$</Box> }}
                    />
                    <TextField
                      select
                      label="Categorias"
                      SelectProps={{ multiple: true }}
                      value={inventoryDraft.categoryIds}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, categoryIds: e.target.value as unknown as string[] }))}
                      helperText="Agrupa el producto para catalogo y filtros."
                    >
                      {categories.map((category) => (
                        <MenuItem key={category.id} value={category.id}>
                          {category.name}
                        </MenuItem>
                      ))}
                    </TextField>
                    <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center">
                        <Box>
                          <Typography variant="subtitle2">Activo</Typography>
                          <Typography variant="caption" color="text.secondary">
                            Define si el producto aparece disponible para usar en ventas.
                          </Typography>
                        </Box>
                        <Switch
                          checked={inventoryDraft.active}
                          onChange={() => setInventoryDraft((prev) => ({ ...prev, active: !prev.active }))}
                        />
                      </Stack>
                    </Card>
                  </Stack>
                </Card>

                <Card variant="outlined" sx={{ borderRadius: 3 }}>
                  <Box sx={{ p: 2.25, borderBottom: '1px solid', borderColor: 'divider' }}>
                    <Typography variant="h6">Inventario y venta</Typography>
                    <Typography variant="body2" color="text.secondary">
                      Stock y control operativo disponible hoy en el sistema.
                    </Typography>
                  </Box>
                  <Stack spacing={2} sx={{ p: 2.25 }}>
                    <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
                      <Stack direction="row" justifyContent="space-between" alignItems="center">
                        <Box>
                          <Typography variant="subtitle2">Controlar inventario</Typography>
                          <Typography variant="caption" color="text.secondary">
                            Desactivalo para servicios o productos intangibles.
                          </Typography>
                        </Box>
                        <Switch
                          checked={inventoryDraft.tracksInventory}
                          onChange={() =>
                            setInventoryDraft((prev) => ({
                              ...prev,
                              tracksInventory: !prev.tracksInventory,
                              onHand: !prev.tracksInventory ? prev.onHand : '0',
                            }))
                          }
                        />
                      </Stack>
                    </Card>
                    <TextField
                      label="Stock disponible"
                      value={inventoryDraft.onHand}
                      disabled={!inventoryDraft.tracksInventory}
                      helperText={inventoryDraft.tracksInventory ? 'Disponible para venta.' : 'No aplica para servicios o intangibles.'}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, onHand: e.target.value }))}
                    />
                    <TextField
                      select
                      label="Sucursales"
                      SelectProps={{ multiple: true }}
                      value={inventoryDraft.branchIds}
                      onChange={(e) => setInventoryDraft((prev) => ({ ...prev, branchIds: e.target.value as unknown as string[] }))}
                      helperText="Define donde se ofrece el producto."
                    >
                      {branches.map((branch) => (
                        <MenuItem key={branch.id} value={branch.id}>
                          {branch.name}
                        </MenuItem>
                      ))}
                    </TextField>
                    <Stack spacing={1}>
                      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                        <TextField
                          fullWidth
                          label="URL de imagen"
                          value={inventoryImageUrlInput}
                          onChange={(e) => setInventoryImageUrlInput(e.target.value)}
                        />
                        <Button
                          variant="outlined"
                          onClick={() => {
                            if (!inventoryImageUrlInput.trim()) return;
                            setInventoryDraft((prev) => ({
                              ...prev,
                              imageUrls: [...prev.imageUrls, inventoryImageUrlInput.trim()],
                            }));
                            setInventoryImageUrlInput('');
                          }}
                        >
                          Agregar imagen
                        </Button>
                      </Stack>
                      {inventoryDraft.imageUrls.length > 0 && (
                        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                          {inventoryDraft.imageUrls.map((url, index) => (
                            <Chip
                              key={url}
                              label={`Imagen ${index + 1}`}
                              color={index === inventoryPreviewImageIndex ? 'primary' : 'default'}
                              variant={index === inventoryPreviewImageIndex ? 'filled' : 'outlined'}
                              onClick={() => setInventoryPreviewImageIndex(index)}
                              onDelete={() => {
                                setInventoryDraft((prev) => ({
                                  ...prev,
                                  imageUrls: prev.imageUrls.filter((item) => item !== url),
                                }));
                                setInventoryPreviewImageIndex((prev) => Math.max(0, prev - (index <= prev ? 1 : 0)));
                              }}
                            />
                          ))}
                        </Stack>
                      )}
                    </Stack>
                    <Alert severity="info">
                      Variaciones avanzadas, descuentos por producto y galeria enriquecida todavia no estan modeladas. Categorias, sucursales e imagenes base ya quedan persistidas.
                    </Alert>
                  </Stack>
                </Card>
              </Stack>
            </Box>

            <Box
              sx={{
                width: { xs: '100%', lg: 340 },
                borderLeft: { lg: '1px solid' },
                borderTop: { xs: '1px solid', lg: 0 },
                borderColor: 'divider',
                bgcolor: 'background.neutral',
                p: 2.5,
              }}
            >
              <Card variant="outlined" sx={{ borderRadius: 3, position: { lg: 'sticky' }, top: 0 }}>
                <Box sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider' }}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center">
                    <Typography variant="subtitle1">Vista previa</Typography>
                    <Chip size="small" color="success" label="En vivo" />
                  </Stack>
                </Box>
                <Stack spacing={2} sx={{ p: 2 }}>
                  <Box
                    sx={{
                      borderRadius: 3,
                      border: '10px solid',
                      borderColor: 'common.black',
                      bgcolor: 'background.paper',
                      minHeight: 520,
                      p: 2,
                    }}
                  >
                    <Box
                      sx={{
                        width: '100%',
                        height: 210,
                        borderRadius: 2,
                        bgcolor: 'background.neutral',
                        display: 'grid',
                        placeItems: 'center',
                        color: 'text.secondary',
                        mb: 2,
                        overflow: 'hidden',
                      }}
                    >
                      {inventoryPreviewImage ? (
                        <Box component="img" src={inventoryPreviewImage} alt={inventoryDraft.name || 'Producto'} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                      ) : (
                        <Stack alignItems="center" spacing={1}>
                          <Iconify icon="mdi:image-outline" width={28} />
                          <Typography variant="caption">Imagen no disponible</Typography>
                        </Stack>
                      )}
                    </Box>
                    {inventoryDraft.imageUrls.length > 1 && (
                      <Stack direction="row" spacing={1} sx={{ overflowX: 'auto', pb: 0.5 }}>
                        {inventoryDraft.imageUrls.map((url, index) => (
                          <Box
                            key={url}
                            onClick={() => setInventoryPreviewImageIndex(index)}
                            sx={{
                              width: 56,
                              height: 56,
                              borderRadius: 1.5,
                              overflow: 'hidden',
                              cursor: 'pointer',
                              border: '2px solid',
                              borderColor: index === inventoryPreviewImageIndex ? 'primary.main' : 'divider',
                              flex: '0 0 auto',
                            }}
                          >
                            <Box component="img" src={url} alt={`Miniatura ${index + 1}`} sx={{ width: '100%', height: '100%', objectFit: 'cover' }} />
                          </Box>
                        ))}
                      </Stack>
                    )}
                    <Stack spacing={0.75}>
                      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                        {inventoryDraft.categoryIds.map((categoryId) => (
                          <Chip key={categoryId} size="small" label={categoryLookup[categoryId]?.name || 'Categoria'} variant="outlined" />
                        ))}
                      </Stack>
                      <Stack direction="row" justifyContent="space-between" spacing={1}>
                        <Typography variant="subtitle1" sx={{ minWidth: 0 }}>
                          {inventoryDraft.name || 'Nombre del producto'}
                        </Typography>
                        <Typography variant="subtitle1">${inventoryPreviewPrice}</Typography>
                      </Stack>
                      <Typography variant="body2" color="text.secondary">
                        {inventoryDraft.description || (inventorySupportsStock
                          ? `Stock disponible: ${inventoryDraft.onHand || '0'} ${inventoryDraft.unitOfMeasure}`
                          : 'Producto sin control de inventario')}
                      </Typography>
                      <Divider sx={{ my: 1 }} />
                      <Typography variant="caption" color="text.secondary">
                        SKU: {inventoryDraft.sku || '-'}
                      </Typography>
                      {inventoryDraft.branchIds.length > 0 && (
                        <Typography variant="caption" color="text.secondary">
                          Disponible en: {inventoryDraft.branchIds.map((branchId) => branches.find((branch) => branch.id === branchId)?.name).filter(Boolean).join(', ')}
                        </Typography>
                      )}
                      <Button variant="contained" disabled sx={{ mt: 2 }}>
                        Agregar al carrito - ${inventoryPreviewPrice}
                      </Button>
                    </Stack>
                  </Box>
                </Stack>
              </Card>
            </Box>
          </Stack>

          <Box sx={{ px: 3, py: 2, borderTop: '1px solid', borderColor: 'divider' }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} justifyContent="space-between" alignItems={{ sm: 'center' }}>
              <Typography variant="body2" color="text.secondary">
                {inventoryEditingSku ? 'Editando producto existente.' : 'Nuevo producto pendiente de guardar.'}
              </Typography>
              <Stack direction="row" spacing={1}>
                <Button
                  onClick={() => {
                    setInventoryDialogOpen(false);
                    setInventoryEditingSku(null);
                  }}
                >
                  Cancelar
                </Button>
                <Button variant="contained" onClick={saveInventory}>
                  Guardar producto
                </Button>
              </Stack>
            </Stack>
          </Box>
        </Stack>
      </Drawer>

      <Dialog open={inventoryAdjustmentOpen} onClose={() => setInventoryAdjustmentOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>
          <Typography variant="h6">Ajustar inventario</Typography>
          <Typography variant="body2" color="text.secondary">
            Registra una entrada o salida manual con su referencia.
          </Typography>
        </DialogTitle>
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

      <Dialog open={categoryDialogOpen} onClose={() => setCategoryDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>
          <Typography variant="h6">{selectedCategory ? 'Editar categoria' : 'Nueva categoria'}</Typography>
          <Typography variant="body2" color="text.secondary">
            Organiza el catalogo con una estructura reutilizable.
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Nombre" value={categoryDraft.name} onChange={(e) => setCategoryDraft((prev) => ({ ...prev, name: e.target.value }))} />
            <TextField label="Descripcion" multiline minRows={3} value={categoryDraft.description} onChange={(e) => setCategoryDraft((prev) => ({ ...prev, description: e.target.value }))} />
            <TextField
              select
              label="Categoria padre"
              value={categoryDraft.parentCategoryId}
              onChange={(e) => setCategoryDraft((prev) => ({ ...prev, parentCategoryId: e.target.value }))}
            >
              <MenuItem value="">Sin padre</MenuItem>
              {categories
                .filter((row) => row.id !== selectedCategory?.id)
                .map((row) => (
                  <MenuItem key={row.id} value={row.id}>
                    {row.name}
                  </MenuItem>
                ))}
            </TextField>
            <TextField label="Orden" value={categoryDraft.sortOrder} onChange={(e) => setCategoryDraft((prev) => ({ ...prev, sortOrder: e.target.value }))} />
            <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="subtitle2">Activa</Typography>
                <Switch checked={categoryDraft.active} onChange={() => setCategoryDraft((prev) => ({ ...prev, active: !prev.active }))} />
              </Stack>
            </Card>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCategoryDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveCategory}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={branchDialogOpen} onClose={() => setBranchDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>
          <Typography variant="h6">{selectedBranch ? 'Editar sucursal' : 'Nueva sucursal'}</Typography>
          <Typography variant="body2" color="text.secondary">
            Registra un punto de operacion para el catalogo y la venta.
          </Typography>
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Codigo" value={branchDraft.code} onChange={(e) => setBranchDraft((prev) => ({ ...prev, code: e.target.value }))} />
            <TextField label="Nombre" value={branchDraft.name} onChange={(e) => setBranchDraft((prev) => ({ ...prev, name: e.target.value }))} />
            <TextField label="Direccion" value={branchDraft.address} onChange={(e) => setBranchDraft((prev) => ({ ...prev, address: e.target.value }))} />
            <TextField label="Telefono" value={branchDraft.phone} onChange={(e) => setBranchDraft((prev) => ({ ...prev, phone: e.target.value }))} />
            <Card variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="subtitle2">Activa</Typography>
                <Switch checked={branchDraft.active} onChange={() => setBranchDraft((prev) => ({ ...prev, active: !prev.active }))} />
              </Stack>
            </Card>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setBranchDialogOpen(false)}>Cancelar</Button>
          <Button variant="contained" onClick={saveBranch}>Guardar</Button>
        </DialogActions>
      </Dialog>

      <Drawer
        anchor="right"
        open={orderDrawerOpen}
        onClose={() => setOrderDrawerOpen(false)}
        PaperProps={{ sx: { width: { xs: '100%', md: 640 }, maxWidth: '100%' } }}
      >
        <Stack sx={{ height: '100%' }}>
          <Box sx={{ px: 3, py: 2.5, borderBottom: '1px solid', borderColor: 'divider' }}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} justifyContent="space-between" alignItems={{ md: 'center' }}>
              <Box>
                <Typography variant="overline" color="text.secondary">
                  Pedidos
                </Typography>
                <Typography variant="h4">
                  {selectedOrder ? `Pedido ${selectedOrder.id.slice(0, 8)}` : 'Detalle de pedido'}
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75 }}>
                  Revisa el contenido del pedido y actualiza su estado operativo.
                </Typography>
              </Box>
              <Chip size="small" color={orderDraftStatus === 'confirmed' ? 'success' : 'default'} label={orderDraftStatus} />
            </Stack>
          </Box>

          <Box sx={{ flex: 1, overflow: 'auto', p: 3 }}>
            {selectedOrder ? (
              <Stack spacing={2}>
                <Grid container spacing={2}>
                  <Grid item xs={12} sm={6}>
                    <Card variant="outlined" sx={{ p: 2, borderRadius: 2.5 }}>
                      <Typography variant="caption" color="text.secondary">Cliente</Typography>
                      <Typography variant="subtitle1">{selectedOrder.partyId}</Typography>
                    </Card>
                  </Grid>
                  <Grid item xs={12} sm={6}>
                    <Card variant="outlined" sx={{ p: 2, borderRadius: 2.5 }}>
                      <Typography variant="caption" color="text.secondary">Total</Typography>
                      <Typography variant="subtitle1">
                        {selectedOrder.currency} {Number(selectedOrder.total || 0).toFixed(2)}
                      </Typography>
                    </Card>
                  </Grid>
                </Grid>

                <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                  <Stack spacing={2}>
                    <TextField
                      select
                      label="Estado"
                      value={orderDraftStatus}
                      onChange={(e) => setOrderDraftStatus(e.target.value)}
                    >
                      <MenuItem value="draft">Borrador</MenuItem>
                      <MenuItem value="submitted">Enviado</MenuItem>
                      <MenuItem value="confirmed">Confirmado</MenuItem>
                      <MenuItem value="cancelled">Cancelado</MenuItem>
                    </TextField>
                    <TextField
                      label="Notas"
                      multiline
                      minRows={4}
                      placeholder="Observaciones operativas del pedido."
                      value={orderDraftNotes}
                      onChange={(e) => setOrderDraftNotes(e.target.value)}
                    />
                  </Stack>
                </Card>

                <Card variant="outlined" sx={{ p: 2.25, borderRadius: 3 }}>
                  <Typography variant="h6" sx={{ mb: 1.5 }}>Items del pedido</Typography>
                  <Stack spacing={1.25}>
                    {selectedOrder.items?.length ? selectedOrder.items.map((item, index) => (
                      <Card key={`${item.sku}-${index}`} variant="outlined" sx={{ p: 1.5, borderRadius: 2.5 }}>
                        <Stack direction="row" justifyContent="space-between" spacing={2}>
                          <Box sx={{ minWidth: 0 }}>
                            <Typography variant="subtitle2">{item.name}</Typography>
                            <Typography variant="caption" color="text.secondary">
                              SKU: {item.sku} | Cantidad: {item.quantity}
                            </Typography>
                          </Box>
                          <Typography variant="subtitle2">
                            {selectedOrder.currency} {(item.unitPrice * item.quantity).toFixed(2)}
                          </Typography>
                        </Stack>
                      </Card>
                    )) : (
                      <Alert severity="info">Este pedido no tiene items cargados.</Alert>
                    )}
                  </Stack>
                </Card>
              </Stack>
            ) : (
              <Typography color="text.secondary">Cargando pedido...</Typography>
            )}
          </Box>

          <Box sx={{ px: 3, py: 2, borderTop: '1px solid', borderColor: 'divider' }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} justifyContent="space-between" alignItems={{ sm: 'center' }}>
              <Typography variant="body2" color="text.secondary">
                Los cambios se aplican sobre estado y notas del pedido.
              </Typography>
              <Stack direction="row" spacing={1}>
                <Button onClick={() => setOrderDrawerOpen(false)}>Cerrar</Button>
                <Button variant="contained" onClick={saveOrder}>Guardar pedido</Button>
              </Stack>
            </Stack>
          </Box>
        </Stack>
      </Drawer>

      <Dialog open={invoiceDialogOpen} onClose={() => setInvoiceDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>
          <Typography variant="h6">Editar factura</Typography>
          <Typography variant="body2" color="text.secondary">
            Ajusta numero, monto, estado y fecha de emision.
          </Typography>
        </DialogTitle>
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
              <MenuItem value="issued">Emitida</MenuItem>
              <MenuItem value="paid">Pagada</MenuItem>
              <MenuItem value="void">Anulada</MenuItem>
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
        <DialogTitle>
          <Typography variant="h6">Vista previa PDF</Typography>
          <Typography variant="body2" color="text.secondary">
            Revisa el documento antes de compartirlo o descargarlo.
          </Typography>
        </DialogTitle>
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

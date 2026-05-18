import type { NavSectionProps } from 'src/components/nav-section';

import { paths } from 'src/routes/paths';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

const icon = (name: string) => <Iconify icon={name} width={24} />;

const ICONS = {
  dashboard: icon('mdi:view-dashboard-outline'),
  workflow: icon('mdi:source-branch'),
  agent: icon('mdi:robot-happy-outline'),
  intent: icon('mdi:target-variant'),
  inbox: icon('mdi:inbox-outline'),
  commerce: icon('mdi:store-cog-outline'),
  executions: icon('mdi:chart-timeline-variant'),
  humanReview: icon('mdi:account-supervisor-outline'),
  kycPayments: icon('mdi:shield-account-outline'),
  channels: icon('mdi:access-point'),
  marketplace: icon('mdi:storefront-outline'),
  mcp: icon('mdi:connection'),
  settings: icon('mdi:cog-outline'),
};

// ----------------------------------------------------------------------

export const navData: NavSectionProps['data'] = [
  {
    subheader: 'Inicio',
    items: [
      {
        title: 'Inicio',
        path: paths.dashboard.overview,
        icon: ICONS.dashboard,
      },
    ],
  },
  {
    subheader: 'Construccion',
    items: [
      {
        title: 'Flujos automatizados',
        path: paths.dashboard.workflows,
        icon: ICONS.workflow,
        caption: 'Antes se llamaba workflow. Aqui defines que debe pasar desde que entra un mensaje hasta que se resuelve el caso.',
      },
      {
        title: 'Asistentes IA',
        path: paths.dashboard.agents,
        icon: ICONS.agent,
        caption: 'Configuracion de los asistentes que responden, consultan datos o ejecutan tareas.',
      },
      {
        title: 'Motivos del cliente',
        path: paths.dashboard.intentMap,
        icon: ICONS.intent,
        caption: 'Intencion es el motivo por el que escribe el cliente. Aqui se define como lo reconoce el sistema.',
      },
    ],
  },
  {
    subheader: 'Operacion',
    items: [
      {
        title: 'Bandeja de entrada',
        path: paths.dashboard.threads,
        icon: ICONS.inbox,
        caption: 'Vista unificada de conversaciones y seguimiento comercial.',
      },
      {
        title: 'Ventas y cobros',
        path: paths.dashboard.commerce,
        icon: ICONS.commerce,
        caption: 'Pedidos, ventas, facturas e inventario ligados a la conversacion.',
      },
      {
        title: 'Actividad del sistema',
        path: paths.dashboard.executions,
        icon: ICONS.executions,
        caption: 'Registro tecnico de lo que el sistema hizo en cada caso.',
      },
      {
        title: 'Casos para revisar',
        path: paths.dashboard.checkpoints,
        icon: ICONS.humanReview,
        caption: 'Solicitudes que necesitan aprobacion o validacion humana antes de continuar.',
      },
      {
        title: 'KYC y pagos',
        path: paths.dashboard.kycPayments,
        icon: ICONS.kycPayments,
      },
    ],
  },
  {
    subheader: 'Integraciones',
    items: [
      {
        title: 'Canales de atencion',
        path: paths.dashboard.system.channels,
        icon: ICONS.channels,
        caption: 'WhatsApp, web, voz, email u otras entradas por donde llegan clientes.',
      },
      {
        title: 'Integraciones',
        path: paths.dashboard.marketplace,
        icon: ICONS.marketplace,
        caption: 'Conexiones a proveedores, sistemas externos y modulos disponibles.',
      },
      {
        title: 'Herramientas externas',
        path: paths.dashboard.system.mcp,
        icon: ICONS.mcp,
        caption: 'MCP es el protocolo tecnico para exponer herramientas externas al sistema.',
      },
    ],
  },
  {
    subheader: 'Administracion',
    items: [
      {
        title: 'Configuracion',
        path: paths.dashboard.system.settings,
        icon: ICONS.settings,
        caption: 'Reglas globales, seguridad, modelos y auditoria.',
        children: [
          { title: 'Modelos IA', path: paths.dashboard.system.models, caption: 'Motores de IA disponibles y su prioridad de uso.' },
          { title: 'Credenciales', path: paths.dashboard.system.authProfiles, caption: 'Perfiles de acceso para proveedores y servicios.' },
          { title: 'Segmentos', path: paths.dashboard.system.segmentRouting, caption: 'Reglas para adaptar la experiencia segun tipo de cliente o caso.' },
          { title: 'Funciones beta', path: paths.dashboard.system.featureFlags, caption: 'Activa o pausa capacidades en prueba.' },
          { title: 'Politicas', path: paths.dashboard.governance.policies, caption: 'Reglas de seguridad, aprobacion y limites operativos.' },
          { title: 'Auditoria', path: paths.dashboard.governance.audit, caption: 'Historia completa y trazable de cada caso.' },
        ],
      },
    ],
  },
];

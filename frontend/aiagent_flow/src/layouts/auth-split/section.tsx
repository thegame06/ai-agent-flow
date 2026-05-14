import type { BoxProps } from '@mui/material/Box';
import type { Breakpoint } from '@mui/material/styles';

import { varAlpha } from 'minimal-shared/utils';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Avatar from '@mui/material/Avatar';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

// ----------------------------------------------------------------------

export type AuthSplitSectionProps = BoxProps & {
  title?: string;
  subtitle?: string;
  layoutQuery?: Breakpoint;
  // legacy props kept for interface compat
  method?: string;
  imgUrl?: string;
  methods?: { path: string; icon: string; label: string }[];
};

const STATS = [
  { value: '10M+', label: 'Mensajes procesados' },
  { value: '99.9%', label: 'Uptime garantizado' },
  { value: '< 300ms', label: 'Latencia promedio' },
  { value: '50+', label: 'Integraciones nativas' },
];

const USE_CASES = [
  {
    icon: 'ðŸ¦',
    title: 'Banca & Fintech',
    desc: 'Onboarding, originaciÃ³n de crÃ©dito y soporte 24/7 en WhatsApp.',
  },
  {
    icon: 'ðŸ›’',
    title: 'Retail & eCommerce',
    desc: 'Cotizaciones, seguimiento de pedidos y recuperaciÃ³n de carrito abandonado.',
  },
  {
    icon: 'ðŸ¥',
    title: 'Salud',
    desc: 'Agendamiento, recordatorios y triaje inteligente de pacientes.',
  },
  {
    icon: 'ðŸ¢',
    title: 'Empresas B2B',
    desc: 'CalificaciÃ³n de leads y automatizaciÃ³n del ciclo de ventas.',
  },
];

const TESTIMONIALS = [
  {
    name: 'MarÃ­a GonzÃ¡lez',
    role: 'CTO Â· Banco Regional',
    text: 'AgentFlow nos permitiÃ³ lanzar nuestro asistente de crÃ©ditos en 3 semanas. El ROI fue inmediato.',
    avatar: 'MG',
    color: '#6366f1',
  },
  {
    name: 'Carlos Ruiz',
    role: 'VP TecnologÃ­a Â· RetailCo',
    text: 'Pasamos de 2h de espera promedio a respuesta instantÃ¡nea. 40% menos de carga al call center.',
    avatar: 'CR',
    color: '#0ea5e9',
  },
];

export function AuthSplitSection({
  sx,
  layoutQuery = 'md',
  ...other
}: AuthSplitSectionProps) {
  return (
    <Box
      sx={[
        (theme) => ({
          ...theme.mixins.bgGradient({
            images: [
              `linear-gradient(135deg, #0f0c29 0%, #302b63 50%, #24243e 100%)`,
            ],
          }),
          px: 4,
          pb: 4,
          width: 1,
          maxWidth: 540,
          display: 'none',
          position: 'relative',
          pt: 'calc(var(--layout-header-desktop-height) + 24px)',
          overflowY: 'auto',
          [theme.breakpoints.up(layoutQuery)]: {
            gap: 0,
            display: 'flex',
            alignItems: 'flex-start',
            flexDirection: 'column',
            justifyContent: 'flex-start',
          },
        }),
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      {...other}
    >
      {/* â”€â”€ Hero â”€â”€ */}
      <Box sx={{ mb: 4 }}>
        <Chip
          label="Plataforma Unicorn-Grade Â· Enterprise AI"
          size="small"
          sx={{
            mb: 2,
            bgcolor: alpha('#6366f1', 0.2),
            color: '#a5b4fc',
            border: '1px solid',
            borderColor: alpha('#6366f1', 0.4),
            fontWeight: 600,
            letterSpacing: 0.5,
          }}
        />
        <Typography
          variant="h3"
          sx={{
            color: 'white',
            fontWeight: 800,
            lineHeight: 1.2,
            mb: 1.5,
          }}
        >
          OrquestaciÃ³n de
          <Box component="span" sx={{ color: '#818cf8', display: 'block' }}>
            Agentes de IA
          </Box>
          para empresas reales
        </Typography>
        <Typography
          variant="body1"
          sx={{ color: alpha('#fff', 0.6), lineHeight: 1.7, maxWidth: 420 }}
        >
          DiseÃ±a, despliega y audita workflows conversacionales omnicanal â€”
          sin cÃ³digo, con control determinÃ­stico y trazabilidad WORM.
        </Typography>
      </Box>

      {/* â”€â”€ Stats â”€â”€ */}
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: '1fr 1fr',
          gap: 1.5,
          mb: 4,
          width: '100%',
        }}
      >
        {STATS.map(({ value, label }) => (
          <Box
            key={label}
            sx={{
              p: 2,
              borderRadius: 2,
              bgcolor: alpha('#fff', 0.05),
              border: '1px solid',
              borderColor: alpha('#fff', 0.08),
              textAlign: 'center',
            }}
          >
            <Typography variant="h5" sx={{ color: '#818cf8', fontWeight: 800 }}>
              {value}
            </Typography>
            <Typography variant="caption" sx={{ color: alpha('#fff', 0.5) }}>
              {label}
            </Typography>
          </Box>
        ))}
      </Box>

      {/* â”€â”€ Use cases â”€â”€ */}
      <Typography
        variant="overline"
        sx={{ color: alpha('#fff', 0.4), letterSpacing: 1.5, mb: 1.5, display: 'block' }}
      >
        Casos de uso
      </Typography>
      <Stack spacing={1.5} sx={{ mb: 4, width: '100%' }}>
        {USE_CASES.map(({ icon, title, desc }) => (
          <Box
            key={title}
            sx={{
              p: 2,
              borderRadius: 2,
              bgcolor: alpha('#fff', 0.04),
              border: '1px solid',
              borderColor: alpha('#fff', 0.07),
              display: 'flex',
              gap: 2,
              alignItems: 'flex-start',
              transition: 'background 0.2s',
              '&:hover': { bgcolor: alpha('#6366f1', 0.1) },
            }}
          >
            <Typography sx={{ fontSize: 22, lineHeight: 1.3 }}>{icon}</Typography>
            <Box>
              <Typography variant="subtitle2" sx={{ color: 'white', fontWeight: 700 }}>
                {title}
              </Typography>
              <Typography variant="caption" sx={{ color: alpha('#fff', 0.5) }}>
                {desc}
              </Typography>
            </Box>
          </Box>
        ))}
      </Stack>

      {/* â”€â”€ Testimonials â”€â”€ */}
      <Typography
        variant="overline"
        sx={{ color: alpha('#fff', 0.4), letterSpacing: 1.5, mb: 1.5, display: 'block' }}
      >
        Lo que dicen nuestros clientes
      </Typography>
      <Stack spacing={1.5} sx={{ width: '100%', pb: 2 }}>
        {TESTIMONIALS.map(({ name, role, text, avatar, color }) => (
          <Box
            key={name}
            sx={{
              p: 2.5,
              borderRadius: 2,
              bgcolor: alpha('#fff', 0.04),
              border: '1px solid',
              borderColor: alpha('#fff', 0.07),
            }}
          >
            <Typography
              variant="body2"
              sx={{ color: alpha('#fff', 0.75), fontStyle: 'italic', mb: 2, lineHeight: 1.6 }}
            >
              &ldquo;{text}&rdquo;
            </Typography>
            <Stack direction="row" spacing={1.5} alignItems="center">
              <Avatar sx={{ width: 32, height: 32, bgcolor: color, fontSize: 12, fontWeight: 700 }}>
                {avatar}
              </Avatar>
              <Box>
                <Typography variant="subtitle2" sx={{ color: 'white', fontWeight: 700, lineHeight: 1 }}>
                  {name}
                </Typography>
                <Typography variant="caption" sx={{ color: alpha('#fff', 0.45) }}>
                  {role}
                </Typography>
              </Box>
            </Stack>
          </Box>
        ))}
      </Stack>
    </Box>
  );
}

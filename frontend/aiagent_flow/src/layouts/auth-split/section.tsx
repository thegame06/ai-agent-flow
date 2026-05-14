import type { BoxProps } from '@mui/material/Box';
import type { Breakpoint } from '@mui/material/styles';

import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

import { Iconify } from 'src/components/iconify';

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
  { value: '24/7', label: 'Atención automatizada' },
  { value: '50+', label: 'Integraciones nativas' },
  { value: '< 300ms', label: 'Latencia promedio' },
  { value: '99.9%', label: 'Disponibilidad objetivo' },
];

const USE_CASES = [
  {
    icon: 'solar:banknote-2-bold-duotone',
    title: 'Banca y fintech',
    desc: 'Onboarding, originación de crédito y soporte seguro en WhatsApp, web y voz.',
  },
  {
    icon: 'solar:cart-large-2-bold-duotone',
    title: 'Retail y eCommerce',
    desc: 'Cotizaciones, seguimiento de pedidos y recuperación de carritos abandonados.',
  },
  {
    icon: 'solar:health-bold-duotone',
    title: 'Salud',
    desc: 'Agendamiento, recordatorios, triaje inicial y seguimiento de pacientes.',
  },
  {
    icon: 'solar:case-round-bold-duotone',
    title: 'Empresas B2B',
    desc: 'Calificación de leads, automatización comercial y trazabilidad de cada interacción.',
  },
];

const BENEFITS = [
  'Diseño visual de flujos conversacionales sin fricción técnica.',
  'Auditoría WORM para conversaciones, decisiones y cambios operativos.',
  'Observabilidad en tiempo real para medir ventas, costos y calidad.',
];

// ----------------------------------------------------------------------

export function AuthSplitSection({ sx, layoutQuery = 'md', ...other }: AuthSplitSectionProps) {
  return (
    <Box
      sx={[
        (theme) => ({
          px: { md: 5, lg: 7 },
          pb: 5,
          width: 1,
          maxWidth: 760,
          display: 'none',
          position: 'relative',
          overflowY: 'auto',
          color: '#10231D',
          bgcolor: '#F6FAF7',
          pt: 'calc(var(--layout-header-desktop-height) + 32px)',
          borderRight: '1px solid',
          borderColor: alpha('#10231D', 0.08),
          [theme.breakpoints.up(layoutQuery)]: {
            gap: 4,
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
      <Stack spacing={3.5} sx={{ width: 1 }}>
        <Box>
          <Box
            sx={{
              gap: 1,
              mb: 2.5,
              px: 1.5,
              py: 0.75,
              width: 'fit-content',
              display: 'flex',
              borderRadius: 1,
              alignItems: 'center',
              color: '#0E7E57',
              typography: 'caption',
              fontWeight: 700,
              bgcolor: alpha('#0E7E57', 0.1),
              border: '1px solid',
              borderColor: alpha('#0E7E57', 0.18),
            }}
          >
            <Iconify icon="solar:stars-bold-duotone" width={16} />
            IA conversacional para vender, atender y operar
          </Box>

          <Typography
            variant="h2"
            sx={{
              mb: 2,
              maxWidth: 640,
              lineHeight: 1.04,
              fontWeight: 850,
              color: '#10231D',
              letterSpacing: 0,
            }}
          >
            Convierte cada conversación en una oportunidad de negocio
          </Typography>

          <Typography variant="h6" sx={{ maxWidth: 600, color: alpha('#10231D', 0.72), lineHeight: 1.6 }}>
            Annonai orquesta agentes de IA para capturar leads, responder clientes y automatizar
            procesos críticos con control empresarial.
          </Typography>
        </Box>

        <Stack direction="row" spacing={1.5} useFlexGap flexWrap="wrap">
          <Button
            size="large"
            variant="contained"
            href="mailto:sales@annonai.com"
            startIcon={<Iconify icon="solar:letter-bold-duotone" />}
            sx={{ bgcolor: '#10231D', '&:hover': { bgcolor: '#0E7E57' } }}
          >
            Contactar ventas
          </Button>
          <Button
            size="large"
            variant="outlined"
            href="mailto:sales@annonai.com?subject=Demo%20Annonai"
            startIcon={<Iconify icon="solar:calendar-mark-bold-duotone" />}
            sx={{ borderColor: alpha('#10231D', 0.2), color: '#10231D' }}
          >
            Solicitar demo
          </Button>
        </Stack>

        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { md: 'repeat(4, minmax(0, 1fr))' },
            gap: 1.25,
            width: 1,
          }}
        >
          {STATS.map(({ value, label }) => (
            <Box
              key={label}
              sx={{
                p: 2,
                minHeight: 112,
                borderRadius: 1,
                bgcolor: '#FFFFFF',
                border: '1px solid',
                borderColor: alpha('#10231D', 0.08),
              }}
            >
              <Typography variant="h4" sx={{ color: '#0E7E57', fontWeight: 850, mb: 0.5 }}>
                {value}
              </Typography>
              <Typography variant="caption" sx={{ color: alpha('#10231D', 0.62), lineHeight: 1.4 }}>
                {label}
              </Typography>
            </Box>
          ))}
        </Box>

        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { md: '1fr 1fr' },
            gap: 1.5,
            width: 1,
          }}
        >
          {USE_CASES.map(({ icon, title, desc }) => (
            <Box
              key={title}
              sx={{
                p: 2.25,
                gap: 1.5,
                display: 'flex',
                borderRadius: 1,
                bgcolor: '#FFFFFF',
                border: '1px solid',
                borderColor: alpha('#10231D', 0.08),
              }}
            >
              <Box
                sx={{
                  width: 42,
                  height: 42,
                  flexShrink: 0,
                  borderRadius: 1,
                  display: 'grid',
                  color: '#0E7E57',
                  placeItems: 'center',
                  bgcolor: alpha('#0E7E57', 0.1),
                }}
              >
                <Iconify icon={icon} width={24} />
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 800, mb: 0.5 }}>
                  {title}
                </Typography>
                <Typography variant="body2" sx={{ color: alpha('#10231D', 0.64), lineHeight: 1.55 }}>
                  {desc}
                </Typography>
              </Box>
            </Box>
          ))}
        </Box>

        <Box
          sx={{
            p: 2.5,
            borderRadius: 1,
            bgcolor: alpha('#10231D', 0.04),
            border: '1px solid',
            borderColor: alpha('#10231D', 0.08),
          }}
        >
          <Typography variant="overline" sx={{ color: '#0E7E57', fontWeight: 800 }}>
            Por qué Annonai
          </Typography>
          <Stack spacing={1.25} sx={{ mt: 1.5 }}>
            {BENEFITS.map((benefit) => (
              <Stack key={benefit} direction="row" spacing={1.25} alignItems="flex-start">
                <Iconify icon="solar:check-circle-bold-duotone" width={20} sx={{ color: '#0E7E57', mt: 0.25 }} />
                <Typography variant="body2" sx={{ color: alpha('#10231D', 0.72), lineHeight: 1.55 }}>
                  {benefit}
                </Typography>
              </Stack>
            ))}
          </Stack>
        </Box>

        <Typography variant="body2" sx={{ color: alpha('#10231D', 0.66) }}>
          Ventas y alianzas:{' '}
          <Link href="mailto:sales@annonai.com" color="inherit" underline="always" sx={{ fontWeight: 800 }}>
            sales@annonai.com
          </Link>
        </Typography>
      </Stack>
    </Box>
  );
}

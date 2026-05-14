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
    desc: 'Onboarding, crédito y soporte seguro en WhatsApp, web y voz.',
  },
  {
    icon: 'solar:cart-large-2-bold-duotone',
    title: 'Retail y eCommerce',
    desc: 'Cotizaciones, pedidos y recuperación de carritos abandonados.',
  },
  {
    icon: 'solar:health-bold-duotone',
    title: 'Salud',
    desc: 'Agendamiento, recordatorios, triaje inicial y seguimiento.',
  },
  {
    icon: 'solar:case-round-bold-duotone',
    title: 'Empresas B2B',
    desc: 'Calificación de leads y automatización comercial trazable.',
  },
];

const BENEFITS = [
  'Diseño visual de flujos conversacionales sin fricción técnica.',
  'Auditoría WORM para conversaciones, decisiones y cambios operativos.',
  'Observabilidad en tiempo real para medir ventas, costos y calidad.',
];

const LIVE_EVENTS = [
  { label: 'Lead calificado', value: '92%', icon: 'solar:user-check-rounded-bold-duotone' },
  { label: 'Venta asistida', value: '$18.4k', icon: 'solar:wallet-money-bold-duotone' },
  { label: 'Tickets resueltos', value: '1,284', icon: 'solar:chat-round-check-bold-duotone' },
];

// ----------------------------------------------------------------------

export function AuthSplitSection({ sx, layoutQuery = 'md', ...other }: AuthSplitSectionProps) {
  return (
    <Box
      sx={[
        (theme) => ({
          width: 1,
          minHeight: '100vh',
          display: 'flex',
          position: 'relative',
          overflow: 'hidden',
          color: '#10231D',
          bgcolor: '#F6FAF7',
          px: { xs: 2.5, sm: 4, md: 7, lg: 9 },
          pb: { xs: 5, md: 7 },
          pt: {
            xs: 'calc(var(--layout-header-mobile-height) + 88px)',
            md: 'calc(var(--layout-header-desktop-height) + 70px)',
          },
          '&::before': {
            top: 120,
            right: -180,
            width: 560,
            height: 560,
            content: '""',
            opacity: 0.35,
            borderRadius: '50%',
            position: 'absolute',
            background: 'radial-gradient(circle, rgba(14,126,87,0.28), rgba(14,126,87,0))',
          },
          [theme.breakpoints.up(layoutQuery)]: {
            alignItems: 'flex-start',
            justifyContent: 'center',
          },
        }),
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      {...other}
    >
      <Box sx={{ width: 1, maxWidth: 1440, position: 'relative', zIndex: 1 }}>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 0.95fr) minmax(420px, 0.65fr)' },
            gap: { xs: 4, lg: 7 },
            alignItems: 'center',
          }}
        >
          <Stack spacing={3.5} sx={{ maxWidth: 760 }}>
            <Box
              sx={{
                gap: 1,
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

            <Box>
              <Typography
                variant="h1"
                sx={{
                  mb: 2,
                  maxWidth: 720,
                  lineHeight: 1.02,
                  fontWeight: 850,
                  color: '#10231D',
                  letterSpacing: 0,
                  fontSize: { xs: 44, sm: 58, md: 72 },
                }}
              >
                Convierte cada conversación en una oportunidad de negocio
              </Typography>

              <Typography
                variant="h5"
                sx={{ maxWidth: 660, color: alpha('#10231D', 0.72), lineHeight: 1.55 }}
              >
                Annonai orquesta agentes de IA para capturar leads, responder clientes y
                automatizar procesos críticos con control empresarial.
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
                gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, 1fr)' },
                gap: 1.25,
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
                  <Typography
                    variant="caption"
                    sx={{ color: alpha('#10231D', 0.62), lineHeight: 1.4 }}
                  >
                    {label}
                  </Typography>
                </Box>
              ))}
            </Box>
          </Stack>

          <Box
            sx={{
              display: { xs: 'none', lg: 'block' },
              position: 'relative',
              minHeight: 560,
            }}
          >
            <Box
              sx={{
                p: 2.5,
                right: 0,
                top: 20,
                width: 500,
                borderRadius: 2,
                position: 'absolute',
                bgcolor: 'rgba(255,255,255,0.82)',
                border: '1px solid',
                borderColor: alpha('#10231D', 0.1),
                boxShadow: '0 30px 90px rgba(16,35,29,0.14)',
                backdropFilter: 'blur(18px)',
              }}
            >
              <Stack spacing={2}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Box>
                    <Typography variant="overline" sx={{ color: '#0E7E57', fontWeight: 900 }}>
                      Revenue cockpit
                    </Typography>
                    <Typography variant="h5" sx={{ fontWeight: 850 }}>
                      Conversaciones en vivo
                    </Typography>
                  </Box>
                  <Box
                    sx={{
                      px: 1.25,
                      py: 0.75,
                      borderRadius: 1,
                      color: '#0E7E57',
                      typography: 'caption',
                      fontWeight: 800,
                      bgcolor: alpha('#0E7E57', 0.1),
                    }}
                  >
                    Online
                  </Box>
                </Stack>

                {LIVE_EVENTS.map(({ icon, label, value }) => (
                  <Stack
                    key={label}
                    direction="row"
                    spacing={1.5}
                    alignItems="center"
                    sx={{
                      p: 1.5,
                      borderRadius: 1,
                      bgcolor: '#FFFFFF',
                      border: '1px solid',
                      borderColor: alpha('#10231D', 0.08),
                    }}
                  >
                    <Box
                      sx={{
                        width: 40,
                        height: 40,
                        borderRadius: 1,
                        display: 'grid',
                        color: '#0E7E57',
                        flexShrink: 0,
                        placeItems: 'center',
                        bgcolor: alpha('#0E7E57', 0.1),
                      }}
                    >
                      <Iconify icon={icon} width={23} />
                    </Box>
                    <Box sx={{ flex: '1 1 auto' }}>
                      <Typography variant="subtitle2" sx={{ fontWeight: 800 }}>
                        {label}
                      </Typography>
                      <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                        Automatizado por agente IA
                      </Typography>
                    </Box>
                    <Typography variant="h5" sx={{ color: '#10231D', fontWeight: 850 }}>
                      {value}
                    </Typography>
                  </Stack>
                ))}
              </Stack>
            </Box>

            <Box
              sx={{
                p: 2,
                left: 10,
                bottom: 28,
                width: 340,
                borderRadius: 2,
                position: 'absolute',
                bgcolor: '#10231D',
                color: '#FFFFFF',
                boxShadow: '0 24px 70px rgba(16,35,29,0.24)',
              }}
            >
              <Typography variant="subtitle1" sx={{ fontWeight: 850, mb: 1 }}>
                Flujo recomendado
              </Typography>
              <Stack spacing={1}>
                {['Capturar intención', 'Calificar oportunidad', 'Enviar al CRM'].map((step) => (
                  <Stack key={step} direction="row" spacing={1} alignItems="center">
                    <Iconify icon="solar:check-circle-bold-duotone" width={18} sx={{ color: '#74E4C4' }} />
                    <Typography variant="body2" sx={{ color: alpha('#FFFFFF', 0.82) }}>
                      {step}
                    </Typography>
                  </Stack>
                ))}
              </Stack>
            </Box>
          </Box>
        </Box>

        <Box
          sx={{
            mt: { xs: 4, md: 6 },
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: 'repeat(4, minmax(0, 1fr))' },
            gap: 1.5,
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
            mt: 1.5,
            p: 2.5,
            borderRadius: 1,
            bgcolor: alpha('#10231D', 0.04),
            border: '1px solid',
            borderColor: alpha('#10231D', 0.08),
          }}
        >
          <Stack
            direction={{ xs: 'column', md: 'row' }}
            spacing={2}
            alignItems={{ md: 'center' }}
            justifyContent="space-between"
          >
            <Stack spacing={1}>
              {BENEFITS.map((benefit) => (
                <Stack key={benefit} direction="row" spacing={1.25} alignItems="flex-start">
                  <Iconify
                    icon="solar:check-circle-bold-duotone"
                    width={20}
                    sx={{ color: '#0E7E57', mt: 0.25 }}
                  />
                  <Typography
                    variant="body2"
                    sx={{ color: alpha('#10231D', 0.72), lineHeight: 1.55 }}
                  >
                    {benefit}
                  </Typography>
                </Stack>
              ))}
            </Stack>

            <Typography variant="body2" sx={{ color: alpha('#10231D', 0.66), minWidth: 240 }}>
              Ventas y alianzas:{' '}
              <Link href="mailto:sales@annonai.com" color="inherit" underline="always" sx={{ fontWeight: 800 }}>
                sales@annonai.com
              </Link>
            </Typography>
          </Stack>
        </Box>
      </Box>
    </Box>
  );
}

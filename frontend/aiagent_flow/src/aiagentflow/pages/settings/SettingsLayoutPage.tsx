import { Outlet, useLocation } from 'react-router';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ButtonBase from '@mui/material/ButtonBase';
import InputAdornment from '@mui/material/InputAdornment';

import { paths } from 'src/routes/paths';
import { RouterLink } from 'src/routes/components';

import { DashboardContent } from 'src/layouts/dashboard';
import { BrandPageHeader } from 'src/aiagentflow/components/BrandPageHeader';

import { Iconify } from 'src/components/iconify';

import { SettingsWorkspaceContext } from './SettingsWorkspaceContext';

const sections = [
  {
    label: 'Configuracion general',
    description: 'Tenant, seguridad y limites operativos.',
    path: paths.dashboard.settings.general,
    icon: 'mdi:cog-outline',
  },
  {
    label: 'Modelos IA',
    description: 'Catalogo, proveedor y costo.',
    path: paths.dashboard.settings.models,
    icon: 'mdi:atom-variant',
  },
  {
    label: 'Credenciales',
    description: 'Perfiles de autenticacion por proveedor.',
    path: paths.dashboard.settings.authProfiles,
    icon: 'mdi:key-outline',
  },
  {
    label: 'Funciones beta',
    description: 'Flags y capacidades en evaluacion.',
    path: paths.dashboard.settings.featureFlags,
    icon: 'mdi:flask-outline',
  },
  {
    label: 'Equipos y atencion',
    description: 'Personas, colas y asignacion.',
    path: paths.dashboard.settings.workforce,
    icon: 'mdi:account-group-outline',
  },
  {
    label: 'Politicas',
    description: 'Reglas y controles de gobernanza.',
    path: paths.dashboard.settings.policies,
    icon: 'mdi:shield-check-outline',
  },
  {
    label: 'Auditoria',
    description: 'Eventos y trazabilidad operativa.',
    path: paths.dashboard.settings.audit,
    icon: 'mdi:clipboard-text-search-outline',
  },
  {
    label: 'Operaciones IA',
    description: 'Senales, alertas y dead letters.',
    path: paths.dashboard.settings.operations,
    icon: 'mdi:chart-timeline-variant',
  },
];

export default function SettingsLayoutPage() {
  const location = useLocation();

  return (
    <SettingsWorkspaceContext.Provider value={{ embedded: true }}>
      <DashboardContent maxWidth={false}>
        <BrandPageHeader
          eyebrow="Administracion"
          title="Configuracion"
          description="Centraliza modelos, credenciales, politicas y operaciones internas en una sola vista de trabajo."
          icon="mdi:cog-outline"
        />

        <Stack direction={{ xs: 'column', lg: 'row' }} spacing={2.5} alignItems="stretch">
          <Card
            variant="outlined"
            sx={{
              width: { xs: '100%', lg: 320 },
              minWidth: { lg: 320 },
              p: 2,
              borderRadius: 3,
              alignSelf: { lg: 'flex-start' },
              position: { lg: 'sticky' },
              top: { lg: 24 },
            }}
          >
            <Stack spacing={2}>
              <Box>
                <Typography variant="subtitle1">Secciones</Typography>
                <Typography variant="body2" color="text.secondary">
                  Accede a cada bloque sin volver al menu principal.
                </Typography>
              </Box>

              <TextField
                size="small"
                placeholder="Buscar configuracion..."
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <Iconify icon="mdi:magnify" width={18} />
                    </InputAdornment>
                  ),
                  readOnly: true,
                }}
              />

              <Stack spacing={0.75}>
                {sections.map((section) => {
                  const active = location.pathname === section.path;
                  return (
                    <ButtonBase
                      key={section.path}
                      component={RouterLink}
                      href={section.path}
                      sx={{
                        width: '100%',
                        textAlign: 'left',
                        borderRadius: 2,
                        px: 1.25,
                        py: 1.1,
                        justifyContent: 'flex-start',
                        border: '1px solid',
                        borderColor: active ? 'primary.main' : 'transparent',
                        bgcolor: active ? 'action.selected' : 'transparent',
                      }}
                    >
                      <Stack direction="row" spacing={1.2} alignItems="flex-start">
                        <Box sx={{ color: active ? 'primary.main' : 'text.secondary', mt: 0.25 }}>
                          <Iconify icon={section.icon} width={18} />
                        </Box>
                        <Box>
                          <Typography variant="body2" fontWeight={active ? 700 : 600}>
                            {section.label}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {section.description}
                          </Typography>
                        </Box>
                      </Stack>
                    </ButtonBase>
                  );
                })}
              </Stack>
            </Stack>
          </Card>

          <Box sx={{ minWidth: 0, flex: 1 }}>
            <Outlet />
          </Box>
        </Stack>
      </DashboardContent>
    </SettingsWorkspaceContext.Provider>
  );
}

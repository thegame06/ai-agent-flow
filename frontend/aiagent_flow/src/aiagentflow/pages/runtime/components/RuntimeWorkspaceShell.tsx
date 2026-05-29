import type { ReactNode } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';

import { DashboardContent } from 'src/layouts/dashboard';

import { Iconify } from 'src/components/iconify';

type Props = {
  title: string;
  description: string;
  runtimeKind: 'Text' | 'Voice' | 'MultimodalRealtime';
  actions?: Array<{ label: string; href: string; icon: string; variant?: 'contained' | 'outlined' }>;
  children: ReactNode;
};

export function RuntimeWorkspaceShell({ title, description, runtimeKind, actions = [], children }: Props) {
  const runtimeLabel =
    runtimeKind === 'Text' ? 'Texto' : runtimeKind === 'Voice' ? 'Voz' : 'Multimodal';

  return (
    <DashboardContent maxWidth="xl">
      <Card variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, mb: 3, borderRadius: 3 }}>
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          justifyContent="space-between"
          alignItems={{ md: 'center' }}
          spacing={2}
        >
          <Box>
            <Typography variant="overline" color="text.secondary">
              Espacio por modalidad
            </Typography>
            <Typography variant="h4" sx={{ mt: 0.25 }}>
              {title}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75, maxWidth: 760 }}>
              {description}
            </Typography>
            <Chip size="small" color="info" sx={{ mt: 1.5 }} label={`Modalidad ${runtimeLabel}`} />
          </Box>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {actions.map((action) => (
              <Button
                key={action.label}
                href={action.href}
                variant={action.variant ?? 'outlined'}
                startIcon={<Iconify icon={action.icon} />}
              >
                {action.label}
              </Button>
            ))}
          </Stack>
        </Stack>
      </Card>
      {children}
    </DashboardContent>
  );
}

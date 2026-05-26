import type { ReactNode } from 'react';

import Box from '@mui/material/Box';
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
  return (
    <DashboardContent maxWidth="xl">
      <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" alignItems={{ md: 'center' }} spacing={1.5} sx={{ mb: 2 }}>
        <Box>
          <Typography variant="h4">{title}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            {description}
          </Typography>
          <Chip size="small" color="info" sx={{ mt: 1 }} label={`Runtime ${runtimeKind}`} />
        </Box>
        <Stack direction="row" spacing={1} flexWrap="wrap">
          {actions.map((action) => (
            <Button key={action.label} href={action.href} variant={action.variant ?? 'outlined'} startIcon={<Iconify icon={action.icon} />}>
              {action.label}
            </Button>
          ))}
        </Stack>
      </Stack>
      {children}
    </DashboardContent>
  );
}

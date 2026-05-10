import type { Theme, SxProps } from '@mui/material/styles';

import { Fragment } from 'react';

import Box from '@mui/material/Box';
import Portal from '@mui/material/Portal';
import { styled } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

export type LoadingScreenProps = React.ComponentProps<'div'> & {
  portal?: boolean;
  sx?: SxProps<Theme>;
};

export function LoadingScreen({ portal, sx, ...other }: LoadingScreenProps) {
  const PortalWrapper = portal ? Portal : Fragment;

  return (
    <PortalWrapper>
      <LoadingContent sx={sx} {...other}>
        <Box component="img" src="/logo/logo-loading.svg" alt="Cargando Annonai" sx={{ width: 84, height: 84 }} />
        <Typography variant="caption" sx={{ mt: 1, color: 'text.secondary', fontWeight: 700 }}>
          Cargando
        </Typography>
      </LoadingContent>
    </PortalWrapper>
  );
}

const LoadingContent = styled('div')(({ theme }) => ({
  flexGrow: 1,
  width: '100%',
  display: 'flex',
  minHeight: '100%',
  alignItems: 'center',
  flexDirection: 'column',
  justifyContent: 'center',
  paddingLeft: theme.spacing(5),
  paddingRight: theme.spacing(5),
}));

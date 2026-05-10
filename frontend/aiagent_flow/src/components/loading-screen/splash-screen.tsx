import type { Theme, SxProps } from '@mui/material/styles';

import { Fragment } from 'react';

import Box from '@mui/material/Box';
import Portal from '@mui/material/Portal';
import { styled } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

export type SplashScreenProps = React.ComponentProps<'div'> & {
  portal?: boolean;
  sx?: SxProps<Theme>;
  slotProps?: {
    wrapper?: React.ComponentProps<typeof LoadingWrapper>;
  };
};

export function SplashScreen({ portal = true, slotProps, sx, ...other }: SplashScreenProps) {
  const PortalWrapper = portal ? Portal : Fragment;

  return (
    <PortalWrapper>
      <LoadingWrapper {...slotProps?.wrapper}>
        <LoadingContent sx={sx} {...other}>
          <Box component="img" src="/logo/logo-loading.svg" alt="Cargando Annonai" sx={{ width: 132, height: 132 }} />
          <Typography variant="caption" sx={{ mt: 1.5, color: 'text.secondary', fontWeight: 700 }}>
            Preparando Annonai
          </Typography>
        </LoadingContent>
      </LoadingWrapper>
    </PortalWrapper>
  );
}

const LoadingWrapper = styled('div')({
  flexGrow: 1,
  display: 'flex',
  flexDirection: 'column',
});

const LoadingContent = styled('div')(({ theme }) => ({
  right: 0,
  bottom: 0,
  zIndex: 9998,
  flexGrow: 1,
  width: '100%',
  height: '100%',
  display: 'flex',
  position: 'fixed',
  alignItems: 'center',
  flexDirection: 'column',
  justifyContent: 'center',
  background: `radial-gradient(circle at 50% 38%, ${theme.vars.palette.primary.lighter}, transparent 32%), ${theme.vars.palette.background.default}`,
}));

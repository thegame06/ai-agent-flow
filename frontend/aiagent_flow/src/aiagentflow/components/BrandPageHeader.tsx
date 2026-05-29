import type { ReactNode } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Avatar from '@mui/material/Avatar';
import Typography from '@mui/material/Typography';
import { alpha, useTheme } from '@mui/material/styles';

import { Iconify } from 'src/components/iconify';

type Props = {
  title: string;
  description: string;
  icon: string;
  eyebrow?: string;
  actions?: ReactNode;
  meta?: ReactNode;
  help?: ReactNode;
};

export function BrandPageHeader({
  title,
  description,
  icon,
  eyebrow,
  actions,
  meta,
  help,
}: Props) {
  const theme = useTheme();

  return (
    <Card
      variant="outlined"
      sx={{
        mb: 3,
        p: { xs: 2, md: 2.5 },
        borderRadius: 3,
        position: 'relative',
        overflow: 'hidden',
        borderColor:
          theme.palette.mode === 'dark'
            ? alpha(theme.palette.primary.light, 0.18)
            : alpha(theme.palette.primary.main, 0.12),
        bgcolor: 'background.paper',
        '&::before': {
          content: '""',
          position: 'absolute',
          left: 0,
          top: 0,
          width: 88,
          height: 3,
          bgcolor: 'primary.main',
        },
      }}
    >
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        spacing={2}
        justifyContent="space-between"
        alignItems={{ md: 'center' }}
      >
        <Stack direction="row" spacing={1.5} alignItems="flex-start">
          <Avatar
            sx={{
              width: 44,
              height: 44,
              bgcolor: alpha(theme.palette.primary.main, 0.08),
              color: 'primary.main',
              border: `1px solid ${alpha(theme.palette.primary.main, 0.14)}`,
            }}
          >
            <Iconify icon={icon} width={22} />
          </Avatar>
          <Box>
            {eyebrow && (
              <Typography
                variant="caption"
                sx={{ color: 'text.secondary', textTransform: 'uppercase', letterSpacing: 0.8 }}
              >
                {eyebrow}
              </Typography>
            )}
            <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: eyebrow ? 0.35 : 0 }}>
              <Typography variant="h4" sx={{ lineHeight: 1.1 }}>
                {title}
              </Typography>
              {help}
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ mt: 0.75, maxWidth: 760 }}>
              {description}
            </Typography>
            {meta && (
              <Box sx={{ mt: 1.25 }}>
                {meta}
              </Box>
            )}
          </Box>
        </Stack>
        {actions && (
          <Box sx={{ width: { xs: '100%', md: 'auto' } }}>
            {actions}
          </Box>
        )}
      </Stack>
    </Card>
  );
}

// Types for Inbox are already defined in types.ts
// This file contains the stats cards component

import type { InboxStats } from './types';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface InboxStatsCardsProps {
  stats: InboxStats | null;
  loading?: boolean;
}

export function InboxStatsCards({ stats, loading }: InboxStatsCardsProps) {
  if (loading || !stats) {
    return (
      <Grid container spacing={3}>
        {[1, 2, 3, 4].map((i) => (
          <Grid item xs={12} sm={6} md={3} key={i}>
            <Card sx={{ p: 3, height: 120 }} />
          </Grid>
        ))}
      </Grid>
    );
  }

  const cards = [
    {
      title: 'Total Conversations',
      value: stats.total,
      icon: 'eva:message-circle-outline',
      color: 'primary.main',
    },
    {
      title: 'Awaiting Classification',
      value: stats.awaiting_classification,
      icon: 'eva:clock-outline',
      color: 'warning.main',
    },
    {
      title: 'Requires Review',
      value: stats.requires_review,
      icon: 'eva:alert-triangle-outline',
      color: 'error.main',
    },
    {
      title: 'Resolved Today',
      value: stats.resolved_today,
      icon: 'eva:checkmark-circle-2-outline',
      color: 'success.main',
    },
  ];

  return (
    <Grid container spacing={3}>
      {cards.map((card, index) => (
        <Grid item xs={12} sm={6} md={3} key={index}>
          <Card sx={{ p: 3 }}>
            <Stack spacing={2}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Box
                  sx={{
                    width: 48,
                    height: 48,
                    borderRadius: 1.5,
                    bgcolor: card.color,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <Iconify icon={card.icon} width={24} color="white" />
                </Box>
              </Stack>

              <Stack spacing={0.5}>
                <Typography variant="h3">{card.value}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {card.title}
                </Typography>
              </Stack>
            </Stack>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
}

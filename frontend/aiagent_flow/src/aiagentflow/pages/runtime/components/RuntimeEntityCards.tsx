import Grid from '@mui/material/Grid';
import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';

import { Iconify } from 'src/components/iconify';

type EntityCard = {
  title: string;
  description: string;
  href: string;
  icon: string;
};

type Props = {
  items: EntityCard[];
};

export function RuntimeEntityCards({ items }: Props) {
  return (
    <Grid container spacing={2}>
      {items.map((item) => (
        <Grid key={item.title} item xs={12} md={4}>
          <Card variant="outlined" sx={{ p: 2, height: '100%' }}>
            <Stack spacing={1.25}>
              <Stack direction="row" spacing={1} alignItems="center">
                <Iconify icon={item.icon} width={22} />
                <Typography variant="subtitle1">{item.title}</Typography>
              </Stack>
              <Typography variant="body2" color="text.secondary">
                {item.description}
              </Typography>
              <Button size="small" variant="outlined" href={item.href} sx={{ alignSelf: 'flex-start' }}>
                Abrir
              </Button>
            </Stack>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
}


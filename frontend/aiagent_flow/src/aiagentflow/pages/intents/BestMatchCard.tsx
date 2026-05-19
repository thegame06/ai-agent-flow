import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import LinearProgress from '@mui/material/LinearProgress';
import Box from '@mui/material/Box';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface BestMatchCardProps {
  match: {
    intent_key: string;
    intent_name: string;
    description: string;
  };
  confidence: number;
  confidenceLevel: 'High' | 'Medium' | 'Low';
}

const confidenceColor = (level: string) => {
  switch (level) {
    case 'High': return 'success';
    case 'Medium': return 'warning';
    case 'Low': return 'error';
    default: return 'default';
  }
};

export function BestMatchCard({ match, confidence, confidenceLevel }: BestMatchCardProps) {
  return (
    <Card>
      <CardContent>
        <Stack spacing={2}>
          <Stack direction="row" alignItems="center" justifyContent="space-between">
            <Stack direction="row" alignItems="center" spacing={1}>
              <Iconify icon="eva:checkmark-circle-2-fill" width={24} color="success.main" />
              <Typography variant="h6">Best Match</Typography>
            </Stack>
            <Chip 
              label={confidenceLevel} 
              color={confidenceColor(confidenceLevel) as any}
              size="small"
            />
          </Stack>

          <Box>
            <Typography variant="subtitle1" sx={{ mb: 0.5 }}>
              {match.intent_name}
            </Typography>
            <Typography variant="caption" color="text.disabled">
              {match.intent_key}
            </Typography>
          </Box>

          <Typography variant="body2" color="text.secondary">
            {match.description}
          </Typography>

          <Box>
            <Stack direction="row" justifyContent="space-between" sx={{ mb: 1 }}>
              <Typography variant="body2" color="text.secondary">
                Confidence Score
              </Typography>
              <Typography variant="body2" fontWeight="bold">
                {(confidence * 100).toFixed(1)}%
              </Typography>
            </Stack>
            <LinearProgress 
              variant="determinate" 
              value={confidence * 100}
              color={confidenceColor(confidenceLevel) as any}
              sx={{ height: 8, borderRadius: 1 }}
            />
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
}

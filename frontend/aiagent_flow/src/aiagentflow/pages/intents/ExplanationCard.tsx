import type { ExplanationData } from './types';

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CardHeader from '@mui/material/CardHeader';
import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

// ----------------------------------------------------------------------

interface ExplanationCardProps {
  explanation: ExplanationData;
}

export function ExplanationCard({ explanation }: ExplanationCardProps) {
  return (
    <Card>
      <CardHeader 
        title="Explicación de la decisión" 
        subheader="Por qué se seleccionó esta intención"
      />
      <CardContent>
        <Stack spacing={3}>
          <Alert severity="info" icon={false}>
            <Typography variant="body2">
              {explanation.decision}
            </Typography>
          </Alert>

          <Box>
            <Typography variant="subtitle2" sx={{ mb: 2 }}>
              Factores contribuyentes
            </Typography>
            <Stack spacing={2}>
              {explanation.factors.map((factor, index) => (
                <Box key={index}>
                  <Stack direction="row" justifyContent="space-between" sx={{ mb: 0.5 }}>
                    <Typography variant="body2" fontWeight="medium">
                      {factor.name}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {(factor.contribution * 100).toFixed(0)}%
                    </Typography>
                  </Stack>
                  <LinearProgress
                    variant="determinate"
                    value={factor.contribution * 100}
                    sx={{ height: 6, borderRadius: 1, mb: 0.5 }}
                  />
                  <Typography variant="caption" color="text.secondary">
                    {factor.details}
                  </Typography>
                </Box>
              ))}
            </Stack>
          </Box>

          <Typography variant="caption" color="text.disabled">
            {explanation.alternatives_considered} intenciones alternativas fueron consideradas
          </Typography>
        </Stack>
      </CardContent>
    </Card>
  );
}

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardHeader from '@mui/material/CardHeader';
import Chip from '@mui/material/Chip';
import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import type { IntentCandidate } from './types';

// ----------------------------------------------------------------------

interface CandidatesListCardProps {
  candidates: IntentCandidate[];
}

export function CandidatesListCard({ candidates }: CandidatesListCardProps) {
  return (
    <Card>
      <CardHeader title="Todos los candidatos" subheader={`${candidates.length} intenciones evaluadas`} />
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Intención</TableCell>
              <TableCell>Puntuación</TableCell>
              <TableCell>Características coincidentes</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {candidates.map((candidate, index) => (
              <TableRow key={index}>
                <TableCell>
                  <Stack spacing={0.5}>
                    <Typography variant="body2" fontWeight="medium">
                      {candidate.intent_name}
                    </Typography>
                    <Typography variant="caption" color="text.disabled">
                      {candidate.intent_key}
                    </Typography>
                  </Stack>
                </TableCell>
                <TableCell>
                  <Box sx={{ minWidth: 200 }}>
                    <Stack direction="row" justifyContent="space-between" sx={{ mb: 0.5 }}>
                      <Typography variant="caption" color="text.secondary">
                        {(candidate.score * 100).toFixed(1)}%
                      </Typography>
                    </Stack>
                    <LinearProgress
                      variant="determinate"
                      value={candidate.score * 100}
                      sx={{ height: 6, borderRadius: 1 }}
                    />
                  </Box>
                </TableCell>
                <TableCell>
                  <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                    {candidate.matched_features.map((feature, i) => (
                      <Chip key={i} label={feature} size="small" variant="outlined" />
                    ))}
                  </Stack>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Card>
  );
}

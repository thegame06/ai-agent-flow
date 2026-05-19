import type { IntentCandidate } from './types';

import Card from '@mui/material/Card';
import Stack from '@mui/material/Stack';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import CardHeader from '@mui/material/CardHeader';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import LinearProgress from '@mui/material/LinearProgress';
import Box from '@mui/material/Box';

// ----------------------------------------------------------------------

interface CandidatesListCardProps {
  candidates: IntentCandidate[];
}

export function CandidatesListCard({ candidates }: CandidatesListCardProps) {
  return (
    <Card>
      <CardHeader title="All Candidates" subheader={`${candidates.length} intents evaluated`} />
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Intent</TableCell>
              <TableCell>Score</TableCell>
              <TableCell>Matched Features</TableCell>
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

import Card from '@mui/material/Card';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Stack from '@mui/material/Stack';

import type { InboxFilter } from './types';

// ----------------------------------------------------------------------

interface InboxFiltersProps {
  filter: InboxFilter;
  onChange: (filter: InboxFilter) => void;
}

export function InboxFilters({ filter, onChange }: InboxFiltersProps) {
  const stateOptions = [
    { value: 'all', label: 'All States' },
    { value: 'AwaitingClassification', label: 'Awaiting Classification' },
    { value: 'Classified', label: 'Classified' },
    { value: 'InProgress', label: 'In Progress' },
    { value: 'Resolved', label: 'Resolved' },
    { value: 'Abandoned', label: 'Abandoned' },
  ];

  const confidenceOptions = [
    { value: 'all', label: 'All Confidence Levels' },
    { value: 'High', label: 'High Confidence' },
    { value: 'Medium', label: 'Medium Confidence' },
    { value: 'Low', label: 'Low Confidence' },
  ];

  return (
    <Card sx={{ p: 2 }}>
      <Stack direction="row" spacing={2}>
        <FormControl size="small" sx={{ minWidth: 220 }}>
          <InputLabel>State</InputLabel>
          <Select
            value={filter.state}
            label="State"
            onChange={(e) => onChange({ ...filter, state: e.target.value })}
          >
            {stateOptions.map((opt) => (
              <MenuItem key={opt.value} value={opt.value}>
                {opt.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        <FormControl size="small" sx={{ minWidth: 220 }}>
          <InputLabel>Confidence</InputLabel>
          <Select
            value={filter.confidence}
            label="Confidence"
            onChange={(e) => onChange({ ...filter, confidence: e.target.value })}
          >
            {confidenceOptions.map((opt) => (
              <MenuItem key={opt.value} value={opt.value}>
                {opt.label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>
    </Card>
  );
}

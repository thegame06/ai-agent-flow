import type { IntentFilter } from './types';

import Card from '@mui/material/Card';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Stack from '@mui/material/Stack';
import InputLabel from '@mui/material/InputLabel';
import FormControl from '@mui/material/FormControl';

// ----------------------------------------------------------------------

interface IntentFiltersProps {
  filter: IntentFilter;
  onChange: (filter: IntentFilter) => void;
}

export function IntentFilters({ filter, onChange }: IntentFiltersProps) {
  const categories = [
    'all',
    'Customer Service',
    'Sales',
    'Support',
    'Information',
    'Transaction',
    'Other',
  ];

  const enabledOptions = [
    { value: 'all', label: 'All' },
    { value: 'enabled', label: 'Enabled Only' },
    { value: 'disabled', label: 'Disabled Only' },
  ];

  return (
    <Card sx={{ p: 2 }}>
      <Stack direction="row" spacing={2}>
        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel>Category</InputLabel>
          <Select
            value={filter.category}
            label="Category"
            onChange={(e) => onChange({ ...filter, category: e.target.value })}
          >
            {categories.map((cat) => (
              <MenuItem key={cat} value={cat}>
                {cat === 'all' ? 'All Categories' : cat}
              </MenuItem>
            ))}
          </Select>
        </FormControl>

        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel>Status</InputLabel>
          <Select
            value={filter.enabled}
            label="Status"
            onChange={(e) => onChange({ ...filter, enabled: e.target.value })}
          >
            {enabledOptions.map((opt) => (
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

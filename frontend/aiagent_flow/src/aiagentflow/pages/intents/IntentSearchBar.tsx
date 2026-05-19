import Card from '@mui/material/Card';
import InputBase from '@mui/material/InputBase';
import InputAdornment from '@mui/material/InputAdornment';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

interface IntentSearchBarProps {
  value: string;
  onChange: (value: string) => void;
}

export function IntentSearchBar({ value, onChange }: IntentSearchBarProps) {
  return (
    <Card sx={{ p: 2 }}>
      <InputBase
        fullWidth
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="Search intents by name, key, or description..."
        startAdornment={
          <InputAdornment position="start">
            <Iconify icon="eva:search-outline" sx={{ color: 'text.disabled', mr: 1 }} />
          </InputAdornment>
        }
      />
    </Card>
  );
}

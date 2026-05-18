import Tooltip from '@mui/material/Tooltip';
import IconButton from '@mui/material/IconButton';

import { Iconify } from 'src/components/iconify';

type Props = {
  title: string;
};

export function TermHelp({ title }: Props) {
  return (
    <Tooltip title={title} arrow placement="top">
      <IconButton size="small" sx={{ p: 0.25, color: 'text.secondary' }}>
        <Iconify icon="eva:info-outline" width={16} />
      </IconButton>
    </Tooltip>
  );
}

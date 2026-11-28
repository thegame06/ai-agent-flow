import { useState, useEffect } from 'react';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Tooltip from '@mui/material/Tooltip';
import Skeleton from '@mui/material/Skeleton';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

// ----------------------------------------------------------------------

type Props = {
  tenantId: string;
  mediaId: string;
  /** image, document, audio, video… (from the attachment) */
  kind?: string | null;
  fileName?: string | null;
  size?: number;
};

/**
 * A file a customer sent (a receipt), loaded with the user's session: images show as a thumbnail,
 * anything else as a chip; both open the full file in a new tab.
 */
export function MediaPreview({ tenantId, mediaId, kind, fileName, size = 140 }: Props) {
  const [url, setUrl] = useState<string | null>(null);
  const [type, setType] = useState<string>('');
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    axios
      .get(`/api/v1/tenants/${tenantId}/media/${mediaId}`, { responseType: 'blob' })
      .then((res) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(res.data);
        setType(res.data?.type ?? '');
        setUrl(objectUrl);
      })
      .catch(() => !cancelled && setFailed(true));
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [tenantId, mediaId]);

  const open = () => url && window.open(url, '_blank', 'noopener');

  if (failed)
    return <Chip size="small" color="default" icon={<Iconify icon="mdi:file-alert-outline" width={14} />} label="Archivo no disponible" />;
  if (!url) return <Skeleton variant="rounded" width={size} height={kind === 'image' ? size * 0.75 : 24} />;

  if (type.startsWith('image/'))
    return (
      <Tooltip title="Ver comprobante">
        <Box
          component="img"
          src={url}
          alt={fileName ?? 'comprobante'}
          onClick={open}
          sx={{ width: size, maxHeight: size * 1.4, objectFit: 'cover', borderRadius: 1, cursor: 'zoom-in', display: 'block', border: 1, borderColor: 'divider' }}
        />
      </Tooltip>
    );

  return (
    <Chip
      size="small"
      clickable
      onClick={open}
      icon={<Iconify icon={type === 'application/pdf' ? 'mdi:file-pdf-box' : 'mdi:paperclip'} width={16} />}
      label={fileName || (type === 'application/pdf' ? 'Comprobante PDF' : 'Ver archivo')}
    />
  );
}

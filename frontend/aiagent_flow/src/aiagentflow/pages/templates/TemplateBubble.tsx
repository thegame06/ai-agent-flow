import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { renderTemplate } from './templateModel';

import type { WaTemplate } from './templateModel';

type Props = {
  template: Pick<WaTemplate, 'components' | 'parameters'>;
  variables?: Record<string, string> | null;
};

/** The template as a WhatsApp message bubble (header bold, body, footer in grey, buttons below). */
export function TemplateBubble({ template, variables }: Props) {
  const r = renderTemplate(template, variables);
  return (
    <Box sx={{ bgcolor: 'background.neutral', p: 2, borderRadius: 1.5 }}>
      <Box
        sx={{
          maxWidth: 340,
          bgcolor: 'background.paper',
          borderRadius: 1.5,
          px: 1.5,
          py: 1,
          boxShadow: (theme) => theme.customShadows?.z1 ?? 1,
        }}
      >
        {r.header && (
          <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
            {r.header}
          </Typography>
        )}
        <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
          {r.body || '—'}
        </Typography>
        {r.footer && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
            {r.footer}
          </Typography>
        )}
      </Box>
      {r.buttons.length > 0 && (
        <Stack spacing={0.5} sx={{ maxWidth: 340, mt: 0.5 }}>
          {r.buttons.map((b, i) => (
            <Box
              key={`${b}-${i}`}
              sx={{ bgcolor: 'background.paper', borderRadius: 1.5, py: 0.75, textAlign: 'center', color: 'info.main', typography: 'body2' }}
            >
              {b}
            </Box>
          ))}
        </Stack>
      )}
    </Box>
  );
}

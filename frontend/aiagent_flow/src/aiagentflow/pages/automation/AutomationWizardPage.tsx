import { useState } from 'react';
import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';

import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';
import { type WizardId, WizardLauncher } from 'src/aiagentflow/components/chat-wizard/WizardRegistry';

export default function AutomationWizardPage() {
  const [searchParams] = useSearchParams();
  const channelId = searchParams.get('channelId') ?? undefined;
  const wizardParam = searchParams.get('wizard');
  const [wizardId, setWizardId] = useState<WizardId>(
    wizardParam === 'agentSubflow' ? 'agentSubflow' : 'automation'
  );

  return (
    <>
      <Helmet>
        <title>Crear automatizacion | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="lg">
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            Wizard conversacional reusable. Tambien puede incrustarse en Inicio y otros modulos.
          </Typography>
          <WizardLauncher
            value={wizardId}
            onChange={setWizardId}
            initialChannelId={channelId}
          />
        </Stack>
      </DashboardContent>
    </>
  );
}

import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';

import { AutomationChatWizard } from './AutomationChatWizard';
import { AgentSubflowChatWizard } from './AgentSubflowChatWizard';

export type WizardId = 'automation' | 'agentSubflow';

type WizardEntry = {
  id: WizardId;
  label: string;
  render: (props?: { initialChannelId?: string }) => React.ReactNode;
};

export const wizardRegistry: WizardEntry[] = [
  {
    id: 'automation',
    label: 'Wizard de automatizacion',
    render: (props) => <AutomationChatWizard initialChannelId={props?.initialChannelId} />,
  },
  {
    id: 'agentSubflow',
    label: 'Wizard de subflujo de agente',
    render: () => <AgentSubflowChatWizard />,
  },
];

type WizardLauncherProps = {
  value: WizardId;
  onChange: (wizardId: WizardId) => void;
  initialChannelId?: string;
};

export function WizardLauncher({ value, onChange, initialChannelId }: WizardLauncherProps) {
  const selected = wizardRegistry.find((entry) => entry.id === value) ?? wizardRegistry[0];

  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1}>
        {wizardRegistry.map((entry) => (
          <Chip
            key={entry.id}
            label={entry.label}
            color={value === entry.id ? 'primary' : 'default'}
            onClick={() => onChange(entry.id)}
          />
        ))}
      </Stack>
      {selected.render({ initialChannelId })}
    </Stack>
  );
}


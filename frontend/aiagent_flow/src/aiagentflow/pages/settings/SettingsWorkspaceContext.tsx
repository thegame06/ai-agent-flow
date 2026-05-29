import { createContext, useContext } from 'react';

type SettingsWorkspaceContextValue = {
  embedded: boolean;
};

export const SettingsWorkspaceContext = createContext<SettingsWorkspaceContextValue>({
  embedded: false,
});

export function useSettingsWorkspace() {
  return useContext(SettingsWorkspaceContext);
}

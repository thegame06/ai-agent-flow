import { useState, useEffect } from 'react';

import axios, { endpoints } from 'src/lib/axios';

const TYPE_LABEL: Record<string, string> = { WhatsApp: 'WhatsApp', Voice: 'Voz', Api: 'API', WebChat: 'Webchat' };

/** The tenant's lines by channel id ("Voz · Cobranza MX"), so campaigns show a name instead of an id. */
export function useLineNames(tenantId: string): Record<string, string> {
  const [names, setNames] = useState<Record<string, string>>({});
  useEffect(() => {
    axios
      .get(endpoints.agentflow.channels.list(tenantId))
      .then((res) =>
        setNames(
          Object.fromEntries(
            ((res.data ?? []) as any[])
              .filter((c) => c?.id)
              .map((c) => [c.id, `${TYPE_LABEL[c.type] ?? c.type} · ${c.name}`])
          )
        )
      )
      .catch(() => setNames({}));
  }, [tenantId]);
  return names;
}

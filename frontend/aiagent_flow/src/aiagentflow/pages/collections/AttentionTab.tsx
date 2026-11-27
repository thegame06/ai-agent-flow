import { useState, useEffect, useCallback } from 'react';

import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { waitingFor, HANDOFF_STATE } from './shared';

import type { AttentionItem } from './shared';

// ----------------------------------------------------------------------

type Props = {
  conversationsBase: string;
  reloadKey: number;
  onOpen: (item: AttentionItem) => void;
  onCount?: (count: number) => void;
};

/** Customers waiting for a person: the assistant handed them off or a campaign flagged them. */
export function AttentionTab({ conversationsBase, reloadKey, onOpen, onCount }: Props) {
  const [items, setItems] = useState<AttentionItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [, setTick] = useState(0);

  const load = useCallback(
    (quiet = false) => {
      if (!quiet) setLoading(true);
      axios
        .get<AttentionItem[]>(`${conversationsBase}/attention`)
        .then((res) => {
          setItems(res.data ?? []);
          onCount?.(res.data?.length ?? 0);
        })
        .finally(() => setLoading(false));
    },
    [conversationsBase, onCount]
  );

  useEffect(() => {
    load();
  }, [load, reloadKey]);

  // New customer messages arrive on their own: refresh quietly and keep the waiting times current.
  useEffect(() => {
    const id = window.setInterval(() => {
      load(true);
      setTick((t) => t + 1);
    }, 15000);
    return () => window.clearInterval(id);
  }, [load]);

  return (
    <>
      <Stack direction="row" spacing={2} sx={{ p: 2 }} alignItems="center" justifyContent="space-between">
        <Typography variant="body2" color="text.secondary">
          Mientras una conversación espera o está con un asesor, el asistente no contesta. Al terminar, devuélvela al asistente.
        </Typography>
        <Button size="small" color="inherit" startIcon={<Iconify icon="mdi:refresh" />} onClick={() => load()}>
          Actualizar
        </Button>
      </Stack>
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Cliente</TableCell>
              <TableCell>Motivo</TableCell>
              <TableCell>Último mensaje</TableCell>
              <TableCell>Esperando</TableCell>
              <TableCell>Estado</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={5} align="center" sx={{ py: 4 }}>
                  <CircularProgress size={24} />
                </TableCell>
              </TableRow>
            )}
            {!loading && items.length === 0 && (
              <TableRow>
                <TableCell colSpan={5} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                  Nadie espera a un asesor. Aquí aparecen los clientes que el asistente transfiere y los que una campaña marca para
                  atender.
                </TableCell>
              </TableRow>
            )}
            {!loading &&
              items.map((item) => {
                const st = HANDOFF_STATE[item.state] ?? HANDOFF_STATE.flagged;
                return (
                  <TableRow
                    key={item.sessionId ?? item.contactId}
                    hover
                    sx={{ cursor: 'pointer' }}
                    onClick={() => onOpen(item)}
                  >
                    <TableCell>
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Typography variant="subtitle2">{item.customer || item.recipient}</Typography>
                        {item.unread > 0 && <Chip size="small" color="error" label={item.unread} />}
                      </Stack>
                      <Typography variant="caption" color="text.secondary">
                        {item.campaignName ?? 'Sin campaña'}
                      </Typography>
                    </TableCell>
                    <TableCell sx={{ maxWidth: 240 }}>{item.reason || '—'}</TableCell>
                    <TableCell sx={{ maxWidth: 280 }}>
                      <Typography variant="body2" noWrap title={item.lastCustomerMessage ?? ''}>
                        {item.lastCustomerMessage || '—'}
                      </Typography>
                    </TableCell>
                    <TableCell>{waitingFor(item.since)}</TableCell>
                    <TableCell>
                      <Chip size="small" label={st.label} color={st.color} variant="soft" />
                    </TableCell>
                  </TableRow>
                );
              })}
          </TableBody>
        </Table>
      </TableContainer>
    </>
  );
}

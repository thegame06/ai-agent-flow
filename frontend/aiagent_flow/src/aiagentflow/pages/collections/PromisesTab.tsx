import { useState, useEffect, useCallback } from 'react';

import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import MenuItem from '@mui/material/MenuItem';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';

import { day, money, dateTime, promiseStatus, PROMISE_STATUS, OUTCOME_SOURCE_LABEL } from './shared';

import type { CollectionPromise } from './shared';

// ----------------------------------------------------------------------

type Props = { base: string; onOpenCustomer: (partyId: string) => void };

export function PromisesTab({ base, onOpenCustomer }: Props) {
  const [status, setStatus] = useState('Pending');
  const [items, setItems] = useState<CollectionPromise[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    axios
      .get(`${base}/promises`, { params: { status: status || undefined } })
      .then((res) => setItems(res.data.items ?? []))
      .finally(() => setLoading(false));
  }, [base, status]);

  useEffect(() => {
    load();
  }, [load]);

  const cancel = async (id: string) => {
    await axios.post(`${base}/promises/${id}/cancel`);
    load();
  };

  return (
    <>
      <Stack direction="row" spacing={2} sx={{ p: 2 }} alignItems="center">
        <TextField select size="small" label="Estado" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 180 }}>
          <MenuItem value="">Todas</MenuItem>
          {PROMISE_STATUS.map((p) => (
            <MenuItem key={p.key} value={p.key}>
              {p.label}
            </MenuItem>
          ))}
        </TextField>
        <Typography variant="body2" color="text.secondary">
          Se marcan como cumplidas cuando el siguiente corte muestra el pago, e incumplidas si pasa la fecha sin pago.
        </Typography>
      </Stack>
      <TableContainer>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Cliente</TableCell>
              <TableCell align="right">Monto</TableCell>
              <TableCell>Fecha prometida</TableCell>
              <TableCell>Origen</TableCell>
              <TableCell align="right">Vencido al prometer</TableCell>
              <TableCell>Estado</TableCell>
              <TableCell />
            </TableRow>
          </TableHead>
          <TableBody>
            {loading && (
              <TableRow>
                <TableCell colSpan={7} align="center" sx={{ py: 4 }}>
                  <CircularProgress size={24} />
                </TableCell>
              </TableRow>
            )}
            {!loading && items.length === 0 && (
              <TableRow>
                <TableCell colSpan={7} align="center" sx={{ py: 4, color: 'text.secondary' }}>
                  No hay promesas en este estado. Se crean desde las respuestas de las campañas o desde la ficha del cliente.
                </TableCell>
              </TableRow>
            )}
            {!loading &&
              items.map((p) => {
                const st = promiseStatus(p.status);
                return (
                  <TableRow key={p.id} hover sx={{ cursor: 'pointer' }} onClick={() => onOpenCustomer(p.partyId)}>
                    <TableCell>
                      <Typography variant="subtitle2">{p.customerName || 'Cliente'}</Typography>
                      <Typography variant="caption" color="text.secondary">
                        registrada {dateTime(p.createdAt)}
                      </Typography>
                    </TableCell>
                    <TableCell align="right">{p.amount ? money(p.amount) : 'No indicado'}</TableCell>
                    <TableCell>{day(p.promisedDate)}</TableCell>
                    <TableCell>{OUTCOME_SOURCE_LABEL[p.source] ?? p.source}</TableCell>
                    <TableCell align="right">{money(p.overdueAtCreation)}</TableCell>
                    <TableCell>
                      <Chip size="small" label={st.label} color={st.color} variant="soft" />
                    </TableCell>
                    <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                      {st.key === 'Pending' && (
                        <Button size="small" color="inherit" onClick={() => cancel(p.id)}>
                          Cancelar
                        </Button>
                      )}
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

import { useState, useEffect } from 'react';

import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import TableRow from '@mui/material/TableRow';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DialogTitle from '@mui/material/DialogTitle';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import TableContainer from '@mui/material/TableContainer';
import CircularProgress from '@mui/material/CircularProgress';

import axios from 'src/lib/axios';

import { Iconify } from 'src/components/iconify';

import { todayIso } from './shared';

// ----------------------------------------------------------------------

type ImportResult = {
  dryRun: boolean;
  batchId?: string;
  snapshotDate: string;
  totalRows: number;
  validRows: number;
  customers: number;
  loansCreated: number;
  loansUpdated: number;
  failed: number;
  errors: { row: number; field: string; message: string }[];
};

export function ImportDialog({
  base,
  open,
  onClose,
  onImported,
}: {
  base: string;
  open: boolean;
  onClose: () => void;
  onImported: () => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [snapshotDate, setSnapshotDate] = useState(todayIso());
  const [preview, setPreview] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) {
      setFile(null);
      setPreview(null);
      setError(null);
      setSnapshotDate(todayIso());
    }
  }, [open]);

  const submit = async (dryRun: boolean) => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      form.append('snapshotDate', snapshotDate);
      form.append('dryRun', String(dryRun));
      const res = await axios.post<ImportResult>(`${base}/imports/csv`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      if (dryRun) setPreview(res.data);
      else onImported();
    } catch (err: any) {
      setError(err?.response?.data?.message ?? 'No se pudo procesar el archivo.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>Importar cartera</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Un renglón por préstamo. Un cliente puede aparecer en varios renglones (varios préstamos). Volver a cargar un
          préstamo con el mismo id lo actualiza.
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mb: 2 }}>
          <Button variant="outlined" component="label" startIcon={<Iconify icon="mdi:paperclip" />}>
            {file ? file.name : 'Elegir archivo CSV'}
            <input
              hidden
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                setPreview(null);
              }}
            />
          </Button>
          <TextField
            size="small"
            type="date"
            label="Fecha de corte"
            value={snapshotDate}
            onChange={(e) => {
              setSnapshotDate(e.target.value);
              setPreview(null);
            }}
            InputLabelProps={{ shrink: true }}
          />
        </Stack>

        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        {preview && (
          <>
            <Alert severity={preview.failed > 0 ? 'warning' : 'success'} sx={{ mb: 2 }}>
              {preview.totalRows} renglones · {preview.validRows} válidos · {preview.customers} clientes ·{' '}
              {preview.loansCreated} préstamos nuevos · {preview.loansUpdated} actualizados
              {preview.failed > 0 ? ` · ${preview.failed} con errores (se omitirán)` : ''}
            </Alert>
            {preview.errors.length > 0 && (
              <TableContainer sx={{ maxHeight: 260 }}>
                <Table size="small" stickyHeader>
                  <TableHead>
                    <TableRow>
                      <TableCell>Renglón</TableCell>
                      <TableCell>Columna</TableCell>
                      <TableCell>Error</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {preview.errors.slice(0, 100).map((e, i) => (
                      <TableRow key={`${e.row}-${e.field}-${i}`}>
                        <TableCell>{e.row}</TableCell>
                        <TableCell>{e.field}</TableCell>
                        <TableCell>{e.message}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancelar</Button>
        <Button variant="outlined" disabled={!file || busy} onClick={() => submit(true)}>
          Validar
        </Button>
        <Button
          variant="contained"
          disabled={!file || busy || !preview || preview.validRows === 0}
          onClick={() => submit(false)}
        >
          {busy ? <CircularProgress size={18} /> : `Importar ${preview?.validRows ?? ''}`}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

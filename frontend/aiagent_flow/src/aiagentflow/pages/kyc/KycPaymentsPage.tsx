import { Helmet } from 'react-helmet-async';
import { useState, useEffect, useCallback } from 'react';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Alert from '@mui/material/Alert';
import Table from '@mui/material/Table';
import Button from '@mui/material/Button';
import TableRow from '@mui/material/TableRow';
import TableCell from '@mui/material/TableCell';
import TableBody from '@mui/material/TableBody';
import TableHead from '@mui/material/TableHead';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CardContent from '@mui/material/CardContent';

import { CONFIG } from 'src/global-config';
import axios, { endpoints } from 'src/lib/axios';
import { DashboardContent } from 'src/layouts/dashboard';
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

type KycCase = {
  caseId: string;
  decisionStatus: string;
  riskScore: number;
  reviewRequired: boolean;
  reviewNotes?: string;
};

type PaymentIntent = {
  paymentId: string;
  status: string;
  amount: number;
  currency: string;
  reference?: string;
};

export default function KycPaymentsPage() {
  const tenantId = useTenantId();
  const [error, setError] = useState<string | null>(null);
  const [kycCase, setKycCase] = useState<KycCase | null>(null);
  const [payment, setPayment] = useState<PaymentIntent | null>(null);
  const [cases, setCases] = useState<KycCase[]>([]);
  const [payments, setPayments] = useState<PaymentIntent[]>([]);
  const [caseStatusFilter, setCaseStatusFilter] = useState('');
  const [paymentStatusFilter, setPaymentStatusFilter] = useState('');
  const [casePage, setCasePage] = useState(1);
  const [paymentPage, setPaymentPage] = useState(1);

  const [kycForm, setKycForm] = useState({
    customerId: '',
    fullName: '',
    documentType: 'national_id',
    documentNumber: '',
    evidenceUrls: '',
  });

  const [reviewForm, setReviewForm] = useState({
    caseId: '',
    approved: true,
    notes: '',
    reviewerId: 'supervisor-1',
  });

  const [paymentForm, setPaymentForm] = useState({
    customerId: '',
    amount: 0,
    currency: 'USD',
    reference: '',
  });

  const loadHistory = useCallback(async () => {
    try {
      const [casesRes, paymentsRes] = await Promise.all([
        axios.get(`${endpoints.agentflow.kyc.listCases(tenantId)}?page=${casePage}&pageSize=10${caseStatusFilter ? `&status=${encodeURIComponent(caseStatusFilter)}` : ''}`),
        axios.get(`${endpoints.agentflow.transactions.listPayments(tenantId)}?page=${paymentPage}&pageSize=10${paymentStatusFilter ? `&status=${encodeURIComponent(paymentStatusFilter)}` : ''}`),
      ]);
      setCases(casesRes.data?.items ?? []);
      setPayments(paymentsRes.data?.items ?? []);
    } catch {
      // keep current state
    }
  }, [casePage, caseStatusFilter, paymentPage, paymentStatusFilter, tenantId]);

  useEffect(() => {
    loadHistory();
  }, [loadHistory]);

  const runDocumentCheck = async () => {
    try {
      setError(null);
      const res = await axios.post(endpoints.agentflow.kyc.documentCheck(tenantId), {
        customerId: kycForm.customerId || undefined,
        fullName: kycForm.fullName || undefined,
        documentType: kycForm.documentType || undefined,
        documentNumber: kycForm.documentNumber || undefined,
        evidenceUrls: kycForm.evidenceUrls
          .split(',')
          .map((x) => x.trim())
          .filter(Boolean),
      });
      setKycCase(res.data);
      setReviewForm((prev) => ({ ...prev, caseId: res.data.caseId }));
      await loadHistory();
    } catch (err: any) {
      setError(err?.message || 'Failed to run document check');
    }
  };

  const submitReview = async () => {
    try {
      setError(null);
      const res = await axios.post(endpoints.agentflow.kyc.review(tenantId, reviewForm.caseId), {
        approved: reviewForm.approved,
        notes: reviewForm.notes,
        reviewerId: reviewForm.reviewerId,
      });
      setKycCase(res.data);
      await loadHistory();
    } catch (err: any) {
      setError(err?.message || 'Failed to submit review');
    }
  };

  const createPayment = async () => {
    try {
      setError(null);
      const res = await axios.post(endpoints.agentflow.transactions.createPayment(tenantId), {
        customerId: paymentForm.customerId || undefined,
        amount: Number(paymentForm.amount),
        currency: paymentForm.currency,
        reference: paymentForm.reference || undefined,
      });
      setPayment(res.data);
      await loadHistory();
    } catch (err: any) {
      setError(err?.message || 'Failed to create payment');
    }
  };

  const confirmPayment = async () => {
    if (!payment?.paymentId) return;
    try {
      setError(null);
      const res = await axios.post(endpoints.agentflow.transactions.confirmPayment(tenantId, payment.paymentId));
      setPayment(res.data);
      await loadHistory();
    } catch (err: any) {
      setError(err?.message || 'Failed to confirm payment');
    }
  };

  return (
    <>
      <Helmet>
        <title>KYC & Payments | {CONFIG.appName}</title>
      </Helmet>
      <DashboardContent maxWidth="xl">
        <Box sx={{ mb: 3 }}>
          <Typography variant="h4">KYC & Payments</Typography>
          <Typography variant="body2" color="text.secondary">
            Base flow: document check, supervisor review, and payment intent lifecycle.
          </Typography>
        </Box>

        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

        <Grid container spacing={2}>
          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>KYC Document Check</Typography>
                <Stack spacing={2}>
                  <TextField label="Customer ID" value={kycForm.customerId} onChange={(e) => setKycForm((p) => ({ ...p, customerId: e.target.value }))} />
                  <TextField label="Full Name" value={kycForm.fullName} onChange={(e) => setKycForm((p) => ({ ...p, fullName: e.target.value }))} />
                  <TextField label="Document Type" value={kycForm.documentType} onChange={(e) => setKycForm((p) => ({ ...p, documentType: e.target.value }))} />
                  <TextField label="Document Number" value={kycForm.documentNumber} onChange={(e) => setKycForm((p) => ({ ...p, documentNumber: e.target.value }))} />
                  <TextField label="Evidence URLs (comma separated)" value={kycForm.evidenceUrls} onChange={(e) => setKycForm((p) => ({ ...p, evidenceUrls: e.target.value }))} />
                  <Button variant="contained" onClick={runDocumentCheck}>Run Document Check</Button>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>KYC Review</Typography>
                <Stack spacing={2}>
                  <TextField label="Case ID" value={reviewForm.caseId} onChange={(e) => setReviewForm((p) => ({ ...p, caseId: e.target.value }))} />
                  <TextField label="Reviewer ID" value={reviewForm.reviewerId} onChange={(e) => setReviewForm((p) => ({ ...p, reviewerId: e.target.value }))} />
                  <TextField label="Notes" value={reviewForm.notes} onChange={(e) => setReviewForm((p) => ({ ...p, notes: e.target.value }))} multiline minRows={3} />
                  <Stack direction="row" spacing={1}>
                    <Button variant={reviewForm.approved ? 'contained' : 'outlined'} onClick={() => setReviewForm((p) => ({ ...p, approved: true }))}>Approve</Button>
                    <Button variant={!reviewForm.approved ? 'contained' : 'outlined'} color="error" onClick={() => setReviewForm((p) => ({ ...p, approved: false }))}>Reject</Button>
                  </Stack>
                  <Button variant="contained" color="secondary" onClick={submitReview}>Submit Review</Button>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>Payment Intent</Typography>
                <Stack spacing={2}>
                  <TextField label="Customer ID" value={paymentForm.customerId} onChange={(e) => setPaymentForm((p) => ({ ...p, customerId: e.target.value }))} />
                  <TextField label="Amount" type="number" value={paymentForm.amount} onChange={(e) => setPaymentForm((p) => ({ ...p, amount: Number(e.target.value || 0) }))} />
                  <TextField label="Currency" value={paymentForm.currency} onChange={(e) => setPaymentForm((p) => ({ ...p, currency: e.target.value }))} />
                  <TextField label="Reference" value={paymentForm.reference} onChange={(e) => setPaymentForm((p) => ({ ...p, reference: e.target.value }))} />
                  <Button variant="contained" onClick={createPayment}>Create Payment</Button>
                  <Button variant="outlined" onClick={confirmPayment} disabled={!payment?.paymentId}>Confirm Payment</Button>
                </Stack>
              </CardContent>
            </Card>
          </Grid>

          <Grid item xs={12} md={6}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 2 }}>Latest Results</Typography>
                <Stack spacing={1}>
                  <Typography variant="subtitle2">KYC Case</Typography>
                  <pre style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{JSON.stringify(kycCase, null, 2)}</pre>
                  <Typography variant="subtitle2" sx={{ mt: 1 }}>Payment</Typography>
                  <pre style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{JSON.stringify(payment, null, 2)}</pre>
                </Stack>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>KYC Cases History</Typography>
                <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                  <TextField size="small" label="Status filter" value={caseStatusFilter} onChange={(e) => { setCaseStatusFilter(e.target.value); setCasePage(1); }} />
                  <Button size="small" variant="outlined" onClick={() => setCasePage((p) => Math.max(1, p - 1))}>Prev</Button>
                  <Button size="small" variant="outlined" onClick={() => setCasePage((p) => p + 1)}>Next</Button>
                </Stack>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Case ID</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Risk</TableCell>
                      <TableCell>Review Required</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {cases.map((item) => (
                      <TableRow key={item.caseId}>
                        <TableCell>{item.caseId}</TableCell>
                        <TableCell>{item.decisionStatus}</TableCell>
                        <TableCell>{item.riskScore}</TableCell>
                        <TableCell>{item.reviewRequired ? 'Yes' : 'No'}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </Grid>
          <Grid item xs={12}>
            <Card>
              <CardContent>
                <Typography variant="h6" sx={{ mb: 1 }}>Payments History</Typography>
                <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                  <TextField size="small" label="Status filter" value={paymentStatusFilter} onChange={(e) => { setPaymentStatusFilter(e.target.value); setPaymentPage(1); }} />
                  <Button size="small" variant="outlined" onClick={() => setPaymentPage((p) => Math.max(1, p - 1))}>Prev</Button>
                  <Button size="small" variant="outlined" onClick={() => setPaymentPage((p) => p + 1)}>Next</Button>
                </Stack>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Payment ID</TableCell>
                      <TableCell>Status</TableCell>
                      <TableCell>Amount</TableCell>
                      <TableCell>Currency</TableCell>
                      <TableCell>Reference</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {payments.map((item) => (
                      <TableRow key={item.paymentId}>
                        <TableCell>{item.paymentId}</TableCell>
                        <TableCell>{item.status}</TableCell>
                        <TableCell>{item.amount}</TableCell>
                        <TableCell>{item.currency}</TableCell>
                        <TableCell>{item.reference || '-'}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      </DashboardContent>
    </>
  );
}

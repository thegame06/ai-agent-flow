import { useParams , Link as RouterLink } from 'react-router';

import { Box, Link, Container, Typography, Breadcrumbs } from '@mui/material';

import { useTenantId } from '../hooks/useTenantId';
import { ChatInterface } from '../components/ChatInterface';

export default function ChatPage() {
  const { agentId } = useParams<{ agentId: string }>();
  const tenantId = useTenantId();

  if (!agentId) {
    return (
      <Container>
        <Typography color="error">Se necesita el identificador del asistente.</Typography>
      </Container>
    );
  }

  return (
    <Container maxWidth="lg" sx={{ py: 4 }}>
      <Breadcrumbs sx={{ mb: 3 }}>
        <Link component={RouterLink} to="/agents" underline="hover" color="inherit">
          Asistentes IA
        </Link>
        <Typography color="text.primary">Conversacion de prueba</Typography>
      </Breadcrumbs>

      <Box
        sx={{
          height: 'calc(100vh - 200px)',
          minHeight: 500,
        }}
      >
        <ChatInterface agentId={agentId} agentName="Asistente" tenantId={tenantId} />
      </Box>
    </Container>
  );
}

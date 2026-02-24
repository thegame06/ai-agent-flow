import { useParams , Link as RouterLink } from 'react-router';

import { Box, Link, Container, Typography, Breadcrumbs } from '@mui/material';

import { ChatInterface } from '../components/ChatInterface';

export default function ChatPage() {
  const { agentId } = useParams<{ agentId: string }>();

  if (!agentId) {
    return (
      <Container>
        <Typography color="error">Agent ID is required</Typography>
      </Container>
    );
  }

  return (
    <Container maxWidth="lg" sx={{ py: 4 }}>
      <Breadcrumbs sx={{ mb: 3 }}>
        <Link component={RouterLink} to="/agents" underline="hover" color="inherit">
          Agents
        </Link>
        <Typography color="text.primary">Chat</Typography>
      </Breadcrumbs>

      <Box
        sx={{
          height: 'calc(100vh - 200px)',
          minHeight: 500,
        }}
      >
        <ChatInterface agentId={agentId} agentName="Agent" tenantId="tenant-1" />
      </Box>
    </Container>
  );
}

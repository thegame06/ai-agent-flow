import { useState } from 'react';
import { Helmet } from 'react-helmet-async';
import { usePopover } from 'minimal-shared/hooks';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import CardContent from '@mui/material/CardContent';
import CardActions from '@mui/material/CardActions';
import { alpha, useTheme } from '@mui/material/styles';
import LinearProgress from '@mui/material/LinearProgress';

import { paths } from 'src/routes/paths';
import { useRouter } from 'src/routes/hooks';
import { RouterLink } from 'src/routes/components';

import { CONFIG } from 'src/global-config';
import { DashboardContent } from 'src/layouts/dashboard';

import { Label } from 'src/components/label';
import { Iconify } from 'src/components/iconify';
import { CustomPopover } from 'src/components/custom-popover';

import { useAgents } from './Hooks/useAgents';
import { CloneAgentDialog } from './components/CloneAgentDialog';
import { DeleteAgentDialog } from './components/DeleteAgentDialog';
import { ExecuteAgentDialog } from './components/ExecuteAgentDialog';

// ----------------------------------------------------------------------

const statusColor = (status: string) => {
  switch (status) {
    case 'Published':
      return 'success';
    case 'Draft':
      return 'warning';
    case 'Archived':
      return 'error';
    default:
      return 'default';
  }
};

// ----------------------------------------------------------------------

export default function AgentsPage() {
  const theme = useTheme();
  const router = useRouter();
  const { agents, loading, clone, remove } = useAgents('tenant-1');
  const [executeDialog, setExecuteDialog] = useState<{
    open: boolean;
    agent: { id: string; name: string; description?: string } | null;
  }>({
    open: false,
    agent: null,
  });
  const [cloneDialog, setCloneDialog] = useState<{
    open: boolean;
    agent: { id: string; name: string } | null;
  }>({
    open: false,
    agent: null,
  });
  const [deleteDialog, setDeleteDialog] = useState<{
    open: boolean;
    agent: { id: string; name: string } | null;
  }>({
    open: false,
    agent: null,
  });

  const handleEdit = (agentId: string) => {
    router.push(`${paths.dashboard.agentDesigner}?id=${agentId}`);
  };

  const handleChat = (agentId: string) => {
    router.push(`${paths.dashboard.agents}/${agentId}/chat`);
  };

  const handleViewDetail = (agentId: string) => {
    router.push(`${paths.dashboard.agents}/${agentId}`);
  };

  const handleExecute = (agentId: string) => {
    const agent = agents.find((a) => a.id === agentId);
    if (agent) {
      setExecuteDialog({
        open: true,
        agent: {
          id: agent.id,
          name: agent.name,
          description: agent.description,
        },
      });
    }
  };

  const handleCloseExecuteDialog = () => {
    setExecuteDialog({ open: false, agent: null });
  };

  const handleClone = (agentId: string) => {
    const agent = agents.find((a) => a.id === agentId);
    if (agent) {
      setCloneDialog({
        open: true,
        agent: { id: agent.id, name: agent.name },
      });
    }
  };

  const handleConfirmClone = async (newName: string, newDescription?: string) => {
    if (cloneDialog.agent) {
      await clone(cloneDialog.agent.id, newName, newDescription);
      setCloneDialog({ open: false, agent: null });
    }
  };

  const handleDelete = (agentId: string) => {
    const agent = agents.find((a) => a.id === agentId);
    if (agent) {
      setDeleteDialog({
        open: true,
        agent: { id: agent.id, name: agent.name },
      });
    }
  };

  const handleConfirmDelete = async () => {
    if (deleteDialog.agent) {
      await remove(deleteDialog.agent.id);
      setDeleteDialog({ open: false, agent: null });
    }
  };

  return (
    <>
      <Helmet>
        <title>Agents | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 5 }}>
          <Box>
            <Typography variant="h4">Agent Management</Typography>
            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 1 }}>
              Create, configure, and deploy AI agents with enterprise-grade controls
            </Typography>
          </Box>

          <Button
            component={RouterLink}
            href={paths.dashboard.agentDesigner}
            variant="contained"
            startIcon={<Iconify icon="mingcute:add-line" />}
          >
            Create New Agent
          </Button>
        </Box>

        {loading ? (
          <LinearProgress />
        ) : agents.length === 0 ? (
          <Card sx={{ p: 5, textAlign: 'center' }}>
            <Iconify icon="mdi:robot-outline" width={80} sx={{ color: 'text.disabled', mb: 2 }} />
            <Typography variant="h6" color="text.secondary">
              No agents found
            </Typography>
            <Typography variant="body2" color="text.disabled" sx={{ mb: 3 }}>
              Create your first agent to get started
            </Typography>
            <Button
              component={RouterLink}
              href={paths.dashboard.agentDesigner}
              variant="contained"
              startIcon={<Iconify icon="mingcute:add-line" />}
            >
              Create Agent
            </Button>
          </Card>
        ) : (
          <Grid container spacing={3}>
            {agents.map((agent) => (
              <Grid key={agent.id} item xs={12} sm={6} md={4}>
                <Card
                  sx={{
                    height: '100%',
                    display: 'flex',
                    flexDirection: 'column',
                    border: `1px solid ${alpha(theme.palette.grey[500], 0.12)}`,
                    transition: 'all 0.3s ease',
                    '&:hover': {
                      boxShadow: theme.shadows[12],
                      transform: 'translateY(-4px)',
                    },
                  }}
                >
                  <CardContent sx={{ flexGrow: 1 }}>
                    <Stack spacing={2}>
                      {/* Header */}
                      <Box sx={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between' }}>
                        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                          <Typography variant="h6" noWrap sx={{ mb: 0.5 }}>
                            {agent.name}
                          </Typography>
                          <Typography variant="caption" color="text.secondary" noWrap>
                            v{agent.version}
                          </Typography>
                        </Box>
                        <AgentMenu
                          agentId={agent.id}
                          onEdit={handleEdit}
                          onChat={handleChat}
                          onClone={handleClone}
                          onDelete={handleDelete}
                        />
                      </Box>

                      {/* Status & Tags */}
                      <Stack direction="row" spacing={1} flexWrap="wrap">
                        <Label color={statusColor(agent.status)} variant="soft">
                          {agent.status}
                        </Label>
                        {agent.tags?.slice(0, 2).map((tag: string) => (
                          <Chip key={tag} label={tag} size="small" variant="outlined" />
                        ))}
                      </Stack>

                      {/* Description */}
                      <Typography
                        variant="body2"
                        color="text.secondary"
                        sx={{
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          display: '-webkit-box',
                          WebkitLineClamp: 2,
                          WebkitBoxOrient: 'vertical',
                          minHeight: 40,
                        }}
                      >
                        {agent.description || 'No description'}
                      </Typography>

                      <Divider />

                      {/* Stats */}
                      <Stack spacing={1}>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify icon="mdi:calendar" width={14} sx={{ mr: 0.5, verticalAlign: 'text-bottom' }} />
                            Created
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            {new Date(agent.createdAt).toLocaleDateString()}
                          </Typography>
                        </Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify icon="mdi:update" width={14} sx={{ mr: 0.5, verticalAlign: 'text-bottom' }} />
                            Updated
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            {new Date(agent.updatedAt).toLocaleDateString()}
                          </Typography>
                        </Box>
                      </Stack>
                    </Stack>
                  </CardContent>

                  <CardActions sx={{ px: 2, pb: 2 }}>
                    <Button
                      fullWidth
                      variant="outlined"
                      startIcon={<Iconify icon="mdi:eye-outline" />}
                      onClick={() => handleViewDetail(agent.id)}
                    >
                      View Detail
                    </Button>
                    <Button
                      fullWidth
                      variant="contained"
                      startIcon={<Iconify icon="mdi:play" />}
                      onClick={() => handleExecute(agent.id)}
                    >
                      Execute
                    </Button>
                  </CardActions>
                </Card>
              </Grid>
            ))}
          </Grid>
        )}
      </DashboardContent>

      {/* Execute Agent Dialog */}
      {executeDialog.agent && (
        <ExecuteAgentDialog
          open={executeDialog.open}
          onClose={handleCloseExecuteDialog}
          agent={executeDialog.agent}
        />
      )}

      {/* Clone Agent Dialog */}
      {cloneDialog.agent && (
        <CloneAgentDialog
          open={cloneDialog.open}
          onClose={() => setCloneDialog({ open: false, agent: null })}
          agent={cloneDialog.agent}
          onConfirm={handleConfirmClone}
        />
      )}

      {/* Delete Agent Dialog */}
      {deleteDialog.agent && (
        <DeleteAgentDialog
          open={deleteDialog.open}
          onClose={() => setDeleteDialog({ open: false, agent: null })}
          agent={deleteDialog.agent}
          onConfirm={handleConfirmDelete}
        />
      )}
    </>
  );
}

// ----------------------------------------------------------------------

interface AgentMenuProps {
  agentId: string;
  onEdit: (id: string) => void;
  onChat: (id: string) => void;
  onClone: (id: string) => void;
  onDelete: (id: string) => void;
}

function AgentMenu({ agentId, onEdit, onChat, onClone, onDelete }: AgentMenuProps) {
  const { open, anchorEl, onClose, onOpen } = usePopover();

  return (
    <>
      <IconButton onClick={onOpen}>
        <Iconify icon="eva:more-vertical-fill" />
      </IconButton>

      <CustomPopover open={open} anchorEl={anchorEl} onClose={onClose}>
        <MenuItem
          onClick={() => {
            onClose();
            onEdit(agentId);
          }}
        >
          <Iconify icon="mdi:pencil-outline" />
          Edit
        </MenuItem>

        <MenuItem
          onClick={() => {
            onClose();
            onChat(agentId);
          }}
        >
          <Iconify icon="mdi:message-text-outline" />
          Chat
        </MenuItem>

        <MenuItem
          onClick={() => {
            onClose();
            onClone(agentId);
          }}
        >
          <Iconify icon="mdi:content-copy" />
          Clone
        </MenuItem>

        <Divider sx={{ borderStyle: 'dashed' }} />

        <MenuItem
          onClick={() => {
            onClose();
            onDelete(agentId);
          }}
          sx={{ color: 'error.main' }}
        >
          <Iconify icon="mdi:delete-outline" />
          Delete
        </MenuItem>
      </CustomPopover>
    </>
  );
}

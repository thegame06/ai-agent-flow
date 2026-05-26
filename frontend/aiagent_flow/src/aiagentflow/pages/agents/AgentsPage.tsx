import { useState, useEffect } from 'react';
import { Helmet } from 'react-helmet-async';
import { useSearchParams } from 'react-router';
import { usePopover } from 'minimal-shared/hooks';

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import Stack from '@mui/material/Stack';
import Paper from '@mui/material/Paper';
import Avatar from '@mui/material/Avatar';
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
import { useTenantId } from 'src/aiagentflow/hooks/useTenantId';

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

const systemRoleLabel = (role?: string) => {
  switch (role) {
    case 'Router': return 'ROUTER';
    case 'ConfigAssistant': return 'CONFIG';
    case 'WorkflowBrain': return 'BRAIN';
    default: return null;
  }
};

const systemRoleColor = (role?: string) => {
  switch (role) {
    case 'Router': return 'primary';
    case 'ConfigAssistant': return 'warning';
    case 'WorkflowBrain': return 'info';
    default: return 'default';
  }
};

// ----------------------------------------------------------------------

export default function AgentsPage() {
  const theme = useTheme();
  const router = useRouter();
  const tenantId = useTenantId();
  const [searchParams] = useSearchParams();
  const runtimeStorageKey = `af:agent:runtimeKind:${tenantId}`;
  const resolveRuntime = (value?: string | null): 'Text' | 'Voice' | 'MultimodalRealtime' | null => {
    if (!value) return null;
    const normalized = value.toLowerCase();
    if (normalized === 'text') return 'Text';
    if (normalized === 'voice') return 'Voice';
    if (normalized === 'multimodal' || normalized === 'multimodalrealtime') return 'MultimodalRealtime';
    return null;
  };
  const runtimeFromQuery = resolveRuntime(searchParams.get('runtimeKind'));
  const runtimeFromStorage =
    typeof window !== 'undefined' ? resolveRuntime(localStorage.getItem(runtimeStorageKey)) : null;
  const runtimeKind = (runtimeFromQuery ?? runtimeFromStorage ?? null) as string | null;
  const { agents, loading, clone, remove } = useAgents(tenantId, runtimeKind);
  useEffect(() => {
    if (typeof window === 'undefined') return;
    if (runtimeKind) localStorage.setItem(runtimeStorageKey, runtimeKind);
  }, [runtimeKind, runtimeStorageKey]);
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
    router.push(`${paths.dashboard.agentDesigner}/${agentId}`);
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

  const publishedAgents = agents.filter((agent) => agent.status === 'Published').length;
  const toolReadyAgents = agents.filter((agent) => (agent.availableTools?.length ?? agent.tools?.length ?? 0) > 0).length;
  const systemAgents = agents.filter((agent) => agent.isSystemAgent).length;

  return (
    <>
      <Helmet>
        <title>Asistentes IA | {CONFIG.appName}</title>
      </Helmet>

      <DashboardContent maxWidth="xl">
        <Paper
          variant="outlined"
          sx={{
            mb: 3,
            p: { xs: 2.5, md: 3 },
            borderRadius: 4,
            background:
              'radial-gradient(circle at 8% 18%, rgba(14,124,90,0.16), transparent 30%), linear-gradient(135deg, #FBFDF9 0%, #F3F9F5 100%)',
          }}
        >
          <Grid container spacing={3} alignItems="center">
            <Grid item xs={12} md={7}>
              <Stack direction="row" spacing={1.5} alignItems="center">
                <Avatar sx={{ width: 56, height: 56, bgcolor: 'primary.lighter', color: 'primary.main' }}>
                  <Iconify icon="mdi:robot-happy-outline" width={30} />
                </Avatar>
                <Box>
                  <Typography variant="overline" color="text.secondary">
                    Asistentes reutilizables
                  </Typography>
                  <Typography variant="h3">Asistentes IA</Typography>
                  {runtimeKind && <Chip size="small" color="info" label={`Runtime ${runtimeKind}`} sx={{ mt: 0.5 }} />}
                  <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                    Disena asistentes reutilizables para canales y flujos automatizados. Cada asistente puede tener modelo,
                    memoria, herramientas, integraciones externas y reglas de seguridad.
                  </Typography>
                </Box>
              </Stack>
            </Grid>
            <Grid item xs={12} md={5}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} justifyContent={{ md: 'flex-end' }}>
                <Button
                  component={RouterLink}
                  href={paths.dashboard.agentsListDetail}
                  variant="outlined"
                  startIcon={<Iconify icon="mdi:view-split-vertical" />}
                >
                  Vista lista-detalle
                </Button>
                <Button
                  component={RouterLink}
                  href={paths.dashboard.workflows}
                  variant="outlined"
                  startIcon={<Iconify icon="mdi:source-branch" />}
                >
                  Usar en flujo
                </Button>
                <Button
                  component={RouterLink}
                  href={paths.dashboard.agentDesigner}
                  variant="contained"
                  startIcon={<Iconify icon="mingcute:add-line" />}
                >
                  Nuevo asistente
                </Button>
              </Stack>
            </Grid>
          </Grid>
        </Paper>

        <Grid container spacing={2} sx={{ mb: 3 }}>
          {[
            ['Total', agents.length, 'mdi:robot-outline'],
            ['Publicados', publishedAgents, 'mdi:check-decagram-outline'],
            ['Sistema', systemAgents, 'mdi:shield-lock-outline'],
            ['Con herramientas', toolReadyAgents, 'mdi:tools'],
          ].map(([label, value, icon]) => (
            <Grid key={String(label)} item xs={12} sm={6} md={3}>
              <Card variant="outlined" sx={{ p: 2 }}>
                <Stack direction="row" spacing={1.5} alignItems="center">
                  <Avatar sx={{ width: 38, height: 38, bgcolor: 'background.neutral', color: 'primary.main' }}>
                    <Iconify icon={String(icon)} width={21} />
                  </Avatar>
                  <Box>
                    <Typography variant="h5">{String(value)}</Typography>
                    <Typography variant="caption" color="text.secondary">
                      {label}
                    </Typography>
                  </Box>
                </Stack>
              </Card>
            </Grid>
          ))}
        </Grid>

        {loading ? (
          <LinearProgress />
        ) : agents.length === 0 ? (
          <Card sx={{ p: 5, textAlign: 'center' }}>
            <Iconify icon="mdi:robot-outline" width={80} sx={{ color: 'text.disabled', mb: 2 }} />
            <Typography variant="h6" color="text.secondary">
              No hay asistentes creados
            </Typography>
            <Typography variant="body2" color="text.disabled" sx={{ mb: 3 }}>
              Crea tu primer asistente para usarlo en canales o como paso dentro de flujos automatizados.
            </Typography>
            <Button
              component={RouterLink}
              href={paths.dashboard.agentDesigner}
              variant="contained"
              startIcon={<Iconify icon="mingcute:add-line" />}
            >
              Crear asistente
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
                          isSystemAgent={agent.isSystemAgent}
                          systemRole={agent.systemRole}
                          onEdit={handleEdit}
                          onChat={handleChat}
                          onClone={handleClone}
                          onDelete={handleDelete}
                        />
                      </Box>

                      {/* Status & Tags */}
                      <Stack direction="row" spacing={1} flexWrap="wrap" alignItems="center">
                        <Label color={statusColor(agent.status)} variant="soft">
                          {agent.status}
                        </Label>
                        {agent.isSystemAgent && systemRoleLabel(agent.systemRole) && (
                          <Chip
                            size="small"
                            label={systemRoleLabel(agent.systemRole)}
                            color={systemRoleColor(agent.systemRole) as any}
                            icon={<Iconify icon="mdi:shield-lock-outline" width={14} />}
                          />
                        )}
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
                            Creado
                          </Typography>
                          <Typography variant="caption" fontWeight={600}>
                            {new Date(agent.createdAt).toLocaleDateString()}
                          </Typography>
                        </Box>
                        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
                          <Typography variant="caption" color="text.secondary">
                            <Iconify icon="mdi:update" width={14} sx={{ mr: 0.5, verticalAlign: 'text-bottom' }} />
                            Actualizado
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
                      Ver detalle
                    </Button>
                    <Button
                      fullWidth
                      variant="contained"
                      startIcon={<Iconify icon="mdi:play" />}
                      onClick={() => handleExecute(agent.id)}
                      disabled={agent.isSystemAgent === true}
                    >
                      Ejecutar
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
  isSystemAgent?: boolean;
  systemRole?: string;
  onEdit: (id: string) => void;
  onChat: (id: string) => void;
  onClone: (id: string) => void;
  onDelete: (id: string) => void;
}

function AgentMenu({ agentId, isSystemAgent, systemRole, onEdit, onChat, onClone, onDelete }: AgentMenuProps) {
  const isReadOnly = isSystemAgent === true && systemRole !== 'WorkflowBrain';
  const { open, anchorEl, onClose, onOpen } = usePopover();

  return (
    <>
      <IconButton onClick={onOpen}>
        <Iconify icon="eva:more-vertical-fill" />
      </IconButton>

      <CustomPopover open={open} anchorEl={anchorEl} onClose={onClose}>
        <MenuItem
          disabled={isReadOnly}
          onClick={() => {
            onClose();
            onEdit(agentId);
          }}
        >
          <Iconify icon="mdi:pencil-outline" />
          Editar
        </MenuItem>

        <MenuItem
          disabled={isSystemAgent === true}
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
          Clonar
        </MenuItem>

        <Divider sx={{ borderStyle: 'dashed' }} />

        <MenuItem
          disabled={isReadOnly}
          onClick={() => {
            onClose();
            onDelete(agentId);
          }}
          sx={{ color: isReadOnly ? 'text.disabled' : 'error.main' }}
        >
          <Iconify icon="mdi:delete-outline" />
          Eliminar
        </MenuItem>
      </CustomPopover>
    </>
  );
}

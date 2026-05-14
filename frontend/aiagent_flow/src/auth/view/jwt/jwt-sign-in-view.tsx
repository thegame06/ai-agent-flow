import { z as zod } from 'zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useBoolean } from 'minimal-shared/hooks';
import { zodResolver } from '@hookform/resolvers/zod';

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import LoadingButton from '@mui/lab/LoadingButton';

import { useRouter } from 'src/routes/hooks';

import { Iconify } from 'src/components/iconify';
import { Form, Field } from 'src/components/hook-form';

import { useAuthContext } from '../../hooks';
import { getErrorMessage } from '../../utils';
import { signInWithPassword } from '../../context/jwt';

// ----------------------------------------------------------------------

export type SignInSchemaType = zod.infer<typeof SignInSchema>;

export const SignInSchema = zod.object({
  email: zod
    .string()
    .min(1, { message: 'El email es requerido' })
    .email({ message: 'Ingresa un email vÃ¡lido' }),
  password: zod
    .string()
    .min(1, { message: 'La contraseÃ±a es requerida' })
    .min(6, { message: 'MÃ­nimo 6 caracteres' }),
});

const FEATURES = [
  { icon: 'mdi:shield-lock-outline', label: 'AuditorÃ­a WORM Â· ISO 27001' },
  { icon: 'mdi:robot-outline', label: 'Multi-agente Â· Omnicanal' },
  { icon: 'mdi:chart-timeline-variant', label: 'Observabilidad en tiempo real' },
];

// ----------------------------------------------------------------------

export function JwtSignInView() {
  const router = useRouter();
  const showPassword = useBoolean();
  const { checkUserSession } = useAuthContext();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const defaultValues: SignInSchemaType = {
    email: 'admin@agentflow.local',
    password: 'Admin123!',
  };

  const methods = useForm<SignInSchemaType>({
    resolver: zodResolver(SignInSchema),
    defaultValues,
  });

  const {
    handleSubmit,
    formState: { isSubmitting },
  } = methods;

  const onSubmit = handleSubmit(async (data) => {
    try {
      await signInWithPassword({ email: data.email, password: data.password });
      await checkUserSession?.();
      router.refresh();
    } catch (error) {
      console.error(error);
      setErrorMessage(getErrorMessage(error));
    }
  });

  return (
    <Box sx={{ width: '100%', maxWidth: 420 }}>
      {/* â”€â”€ Brand header â”€â”€ */}
      <Box sx={{ mb: 4, textAlign: 'center' }}>
        <Box
          sx={{
            width: 56,
            height: 56,
            borderRadius: 2,
            bgcolor: 'primary.main',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            mx: 'auto',
            mb: 2,
          }}
        >
          <Iconify icon="mdi:robot-outline" width={30} sx={{ color: 'white' }} />
        </Box>
        <Typography variant="h4" sx={{ fontWeight: 800, mb: 0.5 }}>
          Bienvenido a AgentFlow
        </Typography>
        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
          Plataforma de orquestaciÃ³n de agentes de IA para empresas
        </Typography>
      </Box>

      {/* â”€â”€ Feature chips â”€â”€ */}
      <Stack direction="row" spacing={1} justifyContent="center" flexWrap="wrap" sx={{ mb: 4, gap: 1 }}>
        {FEATURES.map(({ icon, label }) => (
          <Chip
            key={label}
            icon={<Iconify icon={icon} width={14} />}
            label={label}
            size="small"
            variant="outlined"
            sx={{ fontSize: 11, height: 26 }}
          />
        ))}
      </Stack>

      <Divider sx={{ mb: 3 }}>
        <Typography variant="caption" sx={{ color: 'text.disabled', px: 1 }}>
          Acceso a la plataforma
        </Typography>
      </Divider>

      {!!errorMessage && (
        <Alert severity="error" sx={{ mb: 3 }}>
          {errorMessage}
        </Alert>
      )}

      <Form methods={methods} onSubmit={onSubmit}>
        <Box sx={{ gap: 2.5, display: 'flex', flexDirection: 'column' }}>
          <Field.Text
            name="email"
            label="Email corporativo"
            slotProps={{ inputLabel: { shrink: true } }}
          />

          <Field.Text
            name="password"
            label="ContraseÃ±a"
            type={showPassword.value ? 'text' : 'password'}
            slotProps={{
              inputLabel: { shrink: true },
              input: {
                endAdornment: (
                  <InputAdornment position="end">
                    <IconButton onClick={showPassword.onToggle} edge="end">
                      <Iconify
                        icon={showPassword.value ? 'solar:eye-bold' : 'solar:eye-closed-bold'}
                      />
                    </IconButton>
                  </InputAdornment>
                ),
              },
            }}
          />

          <LoadingButton
            fullWidth
            size="large"
            type="submit"
            variant="contained"
            loading={isSubmitting}
            loadingIndicator="Iniciando sesiÃ³n..."
            startIcon={!isSubmitting ? <Iconify icon="mdi:login" /> : undefined}
          >
            Iniciar sesiÃ³n
          </LoadingButton>
        </Box>
      </Form>

      <Typography
        variant="caption"
        sx={{ display: 'block', textAlign: 'center', mt: 3, color: 'text.disabled' }}
      >
        Â© {new Date().getFullYear()} AgentFlow Â· Enterprise AI Orchestration Platform
      </Typography>
    </Box>
  );
}

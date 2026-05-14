import { z as zod } from 'zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useBoolean } from 'minimal-shared/hooks';
import { zodResolver } from '@hookform/resolvers/zod';

import Box from '@mui/material/Box';
import Link from '@mui/material/Link';
import Alert from '@mui/material/Alert';
import Stack from '@mui/material/Stack';
import Button from '@mui/material/Button';
import Collapse from '@mui/material/Collapse';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import LoadingButton from '@mui/lab/LoadingButton';
import InputAdornment from '@mui/material/InputAdornment';

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
    .email({ message: 'Ingresa un email válido' }),
  password: zod
    .string()
    .min(1, { message: 'La contraseña es requerida' })
    .min(6, { message: 'Mínimo 6 caracteres' }),
});

const TRUST_POINTS = [
  { icon: 'solar:shield-check-bold-duotone', label: 'Auditoría WORM' },
  { icon: 'solar:chat-round-call-bold-duotone', label: 'Omnicanal' },
  { icon: 'solar:chart-2-bold-duotone', label: 'Operación visible' },
];

// ----------------------------------------------------------------------

export function JwtSignInView() {
  const router = useRouter();
  const loginOpen = useBoolean(false);
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
    <Box
      sx={{
        width: '100%',
        maxWidth: 440,
        p: { xs: 2.5, sm: 3 },
        borderRadius: 2,
        bgcolor: 'background.paper',
        border: '1px solid',
        borderColor: 'divider',
        boxShadow: '0 24px 80px rgba(16, 35, 29, 0.10)',
      }}
    >
      <Stack spacing={3}>
        <Box sx={{ textAlign: 'center' }}>
          <Box
            component="img"
            src="/logo/logo-full.svg"
            alt="Annonai"
            sx={{ width: 172, height: 'auto', mx: 'auto', mb: 2 }}
          />

          <Typography variant="h4" sx={{ fontWeight: 800, mb: 1, color: '#10231D' }}>
            Automatiza conversaciones que venden y resuelven
          </Typography>

          <Typography variant="body2" sx={{ color: 'text.secondary', maxWidth: 340, mx: 'auto' }}>
            Agentes de IA para atención, ventas y operaciones, con control empresarial desde el
            primer día.
          </Typography>
        </Box>

        <Stack direction="row" spacing={1} useFlexGap flexWrap="wrap" justifyContent="center">
          {TRUST_POINTS.map(({ icon, label }) => (
            <Box
              key={label}
              sx={{
                gap: 0.75,
                px: 1.25,
                py: 0.75,
                display: 'flex',
                borderRadius: 1,
                alignItems: 'center',
                typography: 'caption',
                color: 'text.secondary',
                bgcolor: 'rgba(14, 126, 87, 0.08)',
              }}
            >
              <Iconify icon={icon} width={16} sx={{ color: '#0E7E57' }} />
              {label}
            </Box>
          ))}
        </Stack>

        <Stack spacing={1.25}>
          <Button
            fullWidth
            size="large"
            variant="contained"
            color="primary"
            onClick={loginOpen.onToggle}
            endIcon={
              <Iconify icon={loginOpen.value ? 'solar:alt-arrow-up-bold' : 'solar:login-3-bold'} />
            }
            sx={{ bgcolor: '#10231D', '&:hover': { bgcolor: '#0E7E57' } }}
          >
            {loginOpen.value ? 'Ocultar acceso' : 'Entrar a la plataforma'}
          </Button>

          <Button
            fullWidth
            size="large"
            variant="outlined"
            href="mailto:sales@annonai.com"
            startIcon={<Iconify icon="solar:letter-bold-duotone" />}
            sx={{ borderColor: 'divider', color: '#10231D' }}
          >
            Hablar con ventas
          </Button>
        </Stack>

        <Collapse in={loginOpen.value} unmountOnExit>
          <Stack spacing={2.5}>
            {!!errorMessage && <Alert severity="error">{errorMessage}</Alert>}

            <Form methods={methods} onSubmit={onSubmit}>
              <Box sx={{ gap: 2, display: 'flex', flexDirection: 'column' }}>
                <Field.Text
                  name="email"
                  label="Email corporativo"
                  slotProps={{ inputLabel: { shrink: true } }}
                />

                <Field.Text
                  name="password"
                  label="Contraseña"
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
                  loadingIndicator="Iniciando sesión..."
                  startIcon={!isSubmitting ? <Iconify icon="mdi:login" /> : undefined}
                  sx={{ bgcolor: '#10231D', '&:hover': { bgcolor: '#0E7E57' } }}
                >
                  Iniciar sesión
                </LoadingButton>
              </Box>
            </Form>
          </Stack>
        </Collapse>

        <Typography variant="caption" sx={{ textAlign: 'center', color: 'text.disabled' }}>
          Contacto comercial:{' '}
          <Link href="mailto:sales@annonai.com" color="inherit" underline="always">
            sales@annonai.com
          </Link>
        </Typography>
      </Stack>
    </Box>
  );
}

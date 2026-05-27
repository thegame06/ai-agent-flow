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
    <Box
      sx={{
        width: { xs: '100%', md: 420 },
        borderRadius: 1.5,
        bgcolor: 'rgba(255, 255, 255, 0.92)',
        border: '1px solid',
        borderColor: 'rgba(16, 35, 29, 0.10)',
        boxShadow: '0 24px 70px rgba(16, 35, 29, 0.14)',
        backdropFilter: 'blur(16px)',
      }}
    >
      <Stack direction="row" spacing={1} alignItems="center" sx={{ p: 2, pb: 1, minHeight: 56 }}>
        <Box
          component="img"
          src="/logo/logo-single.svg"
          alt="Annonai"
          sx={{ width: 34, height: 34, flexShrink: 0 }}
        />

        <Box sx={{ minWidth: 0, flex: '1 1 auto' }}>
          <Typography variant="subtitle2" sx={{ color: '#10231D', fontWeight: 800, lineHeight: 1 }}>
            Iniciar sesión
          </Typography>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            Accede al panel de operación Annonai
          </Typography>
        </Box>

        <Button
          size="small"
          variant="outlined"
          endIcon={<Iconify icon="solar:arrow-right-up-outline" />}
          href="https://annonai.com"
          target="_blank"
          rel="noreferrer"
          sx={{ display: { xs: 'none', sm: 'inline-flex' } }}
        >
          Sitio web
        </Button>
      </Stack>

      <Box sx={{ px: 2, pb: 2 }}>
        <Box
          sx={{
            p: 2,
            borderRadius: 1,
            bgcolor: '#FFFFFF',
            border: '1px solid',
            borderColor: 'divider',
          }}
        >
          <Stack spacing={2}>
            {!!errorMessage && <Alert severity="error">{errorMessage}</Alert>}

            <Form methods={methods} onSubmit={onSubmit}>
              <Box sx={{ gap: 1.5, display: 'flex', flexDirection: 'column' }}>
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

            <Typography variant="caption" sx={{ textAlign: 'center', color: 'text.disabled' }}>
              Contacto comercial:{' '}
              <Link href="mailto:sales@annonai.com" color="inherit" underline="always">
                sales@annonai.com
              </Link>
            </Typography>
          </Stack>
        </Box>
      </Box>
    </Box>
  );
}

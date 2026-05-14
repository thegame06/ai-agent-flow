import type { RouteObject } from 'react-router';

import { Outlet } from 'react-router';
import { lazy, Suspense } from 'react';

import { AuthSplitLayout } from 'src/layouts/auth-split';

import { SplashScreen } from 'src/components/loading-screen';

import { GuestGuard } from 'src/auth/guard';

// ----------------------------------------------------------------------

/** **************************************
 * Jwt
 *************************************** */
const Jwt = {
  SignInPage: lazy(() => import('src/pages/auth/jwt/sign-in')),
  SignUpPage: lazy(() => import('src/pages/auth/jwt/sign-up')),
};

const authJwt = {
  path: 'jwt',
  children: [
    {
      path: 'sign-in',
      element: (
        <GuestGuard>
          <AuthSplitLayout
            cssVars={{ '--layout-auth-content-width': '420px' }}
            slotProps={{
              main: {
                sx: { position: 'relative', bgcolor: '#F6FAF7' },
              },
              section: {
                title: 'Orquestación de agentes de IA para empresas reales',
                subtitle: 'Diseña, despliega y audita workflows conversacionales omnicanal.',
              },
              content: {
                sx: (theme) => ({
                  p: 0,
                  zIndex: 20,
                  width: { xs: 'calc(100% - 32px)', md: 'auto' },
                  position: { xs: 'absolute', md: 'fixed' },
                  top: { xs: 78, md: 18 },
                  right: { xs: 16, md: 88 },
                  alignItems: 'flex-end',
                  [theme.breakpoints.up('md')]: {
                    justifyContent: 'flex-start',
                  },
                }),
              },
            }}
          >
            <Jwt.SignInPage />
          </AuthSplitLayout>
        </GuestGuard>
      ),
    },
    {
      path: 'sign-up',
      element: (
        <GuestGuard>
          <AuthSplitLayout>
            <Jwt.SignUpPage />
          </AuthSplitLayout>
        </GuestGuard>
      ),
    },
  ],
};

// ----------------------------------------------------------------------

export const authRoutes: RouteObject[] = [
  {
    path: 'auth',
    element: (
      <Suspense fallback={<SplashScreen />}>
        <Outlet />
      </Suspense>
    ),
    children: [authJwt],
  },
];

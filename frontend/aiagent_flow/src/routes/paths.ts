// ----------------------------------------------------------------------

const ROOTS = {
  AUTH: '/auth',
  DASHBOARD: '/dashboard',
};

// ----------------------------------------------------------------------

export const paths = {
  faqs: '/faqs',
  minimalStore: 'https://mui.com/store/items/minimal-dashboard/',
  // AUTH
  auth: {
    amplify: {
      signIn: `${ROOTS.AUTH}/amplify/sign-in`,
      verify: `${ROOTS.AUTH}/amplify/verify`,
      signUp: `${ROOTS.AUTH}/amplify/sign-up`,
      updatePassword: `${ROOTS.AUTH}/amplify/update-password`,
      resetPassword: `${ROOTS.AUTH}/amplify/reset-password`,
    },
    jwt: {
      signIn: `${ROOTS.AUTH}/jwt/sign-in`,
      signUp: `${ROOTS.AUTH}/jwt/sign-up`,
    },
    firebase: {
      signIn: `${ROOTS.AUTH}/firebase/sign-in`,
      verify: `${ROOTS.AUTH}/firebase/verify`,
      signUp: `${ROOTS.AUTH}/firebase/sign-up`,
      resetPassword: `${ROOTS.AUTH}/firebase/reset-password`,
    },
    auth0: {
      signIn: `${ROOTS.AUTH}/auth0/sign-in`,
    },
    supabase: {
      signIn: `${ROOTS.AUTH}/supabase/sign-in`,
      verify: `${ROOTS.AUTH}/supabase/verify`,
      signUp: `${ROOTS.AUTH}/supabase/sign-up`,
      updatePassword: `${ROOTS.AUTH}/supabase/update-password`,
      resetPassword: `${ROOTS.AUTH}/supabase/reset-password`,
    },
  },
  // DASHBOARD
  dashboard: {
    root: ROOTS.DASHBOARD,
    overview: `${ROOTS.DASHBOARD}/overview`,
    agents: `${ROOTS.DASHBOARD}/agents`,
    agentDesigner: `${ROOTS.DASHBOARD}/agents/designer`,
    agentEdit: (id: string) => `${ROOTS.DASHBOARD}/agents/designer/${id}`,
    executions: `${ROOTS.DASHBOARD}/executions`,
    executionDetail: (id: string) => `${ROOTS.DASHBOARD}/executions/${id}`,
    checkpoints: `${ROOTS.DASHBOARD}/checkpoints`,
    tools: `${ROOTS.DASHBOARD}/tools`,
    evaluations: `${ROOTS.DASHBOARD}/evaluations`,
    governance: {
      root: `${ROOTS.DASHBOARD}/governance`,
      policies: `${ROOTS.DASHBOARD}/governance/policies`,
      audit: `${ROOTS.DASHBOARD}/governance/audit`,
    },
    system: {
      root: `${ROOTS.DASHBOARD}/system`,
      models: `${ROOTS.DASHBOARD}/system/models`,
      authProfiles: `${ROOTS.DASHBOARD}/system/auth-profiles`,
      mcp: `${ROOTS.DASHBOARD}/system/mcp`,
      channels: `${ROOTS.DASHBOARD}/system/channels`,
      settings: `${ROOTS.DASHBOARD}/system/settings`,
    },
  },
};

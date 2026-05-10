import type { CommonColors } from '@mui/material/styles/createPalette';

import type { PaletteColorNoChannels } from './core/palette';
import type { ThemeDirection, ThemeColorScheme, ThemeCssVariables } from './types';

// ----------------------------------------------------------------------

type ThemeConfig = {
  classesPrefix: string;
  modeStorageKey: string;
  direction: ThemeDirection;
  defaultMode: ThemeColorScheme;
  cssVariables: ThemeCssVariables;
  fontFamily: Record<'primary' | 'secondary', string>;
  palette: Record<
    'primary' | 'secondary' | 'info' | 'success' | 'warning' | 'error',
    PaletteColorNoChannels
  > & {
    common: Pick<CommonColors, 'black' | 'white'>;
    grey: Record<
      '50' | '100' | '200' | '300' | '400' | '500' | '600' | '700' | '800' | '900',
      string
    >;
  };
};

export const themeConfig: ThemeConfig = {
  /** **************************************
   * Base
   *************************************** */
  direction: 'ltr',
  defaultMode: 'light',
  modeStorageKey: 'theme-mode',
  classesPrefix: 'annonai',
  /** **************************************
   * Typography
   *************************************** */
  fontFamily: {
    primary: 'Aptos, Public Sans Variable, sans-serif',
    secondary: 'Barlow, Aptos, sans-serif',
  },
  /** **************************************
   * Palette
   *************************************** */
  palette: {
    primary: {
      lighter: '#DDF7EA',
      light: '#7EDCB1',
      main: '#0E7C5A',
      dark: '#0A5D46',
      darker: '#073D31',
      contrastText: '#FFFFFF',
    },
    secondary: {
      lighter: '#D9FBFF',
      light: '#7DEAF2',
      main: '#00A7B5',
      dark: '#007987',
      darker: '#004E59',
      contrastText: '#FFFFFF',
    },
    info: {
      lighter: '#CAFDF5',
      light: '#61F3F3',
      main: '#00B8D9',
      dark: '#006C9C',
      darker: '#003768',
      contrastText: '#FFFFFF',
    },
    success: {
      lighter: '#D3FCD2',
      light: '#77ED8B',
      main: '#22C55E',
      dark: '#118D57',
      darker: '#065E49',
      contrastText: '#ffffff',
    },
    warning: {
      lighter: '#FFF5CC',
      light: '#FFD666',
      main: '#FFAB00',
      dark: '#B76E00',
      darker: '#7A4100',
      contrastText: '#1C252E',
    },
    error: {
      lighter: '#FFE9D5',
      light: '#FFAC82',
      main: '#FF5630',
      dark: '#B71D18',
      darker: '#7A0916',
      contrastText: '#FFFFFF',
    },
    grey: {
      '50': '#FBFDF9',
      '100': '#F4F8F3',
      '200': '#E7EEE6',
      '300': '#D4DED1',
      '400': '#A8B7A4',
      '500': '#768675',
      '600': '#566456',
      '700': '#3D4B40',
      '800': '#20332D',
      '900': '#10231D',
    },
    common: { black: '#000000', white: '#FFFFFF' },
  },
  /** **************************************
   * Css variables
   *************************************** */
  cssVariables: {
    cssVarPrefix: '',
    colorSchemeSelector: 'data-color-scheme',
  },
};

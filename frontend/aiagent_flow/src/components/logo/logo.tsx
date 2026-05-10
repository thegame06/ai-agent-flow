import type { LinkProps } from '@mui/material/Link';

import { forwardRef } from 'react';
import { mergeClasses } from 'minimal-shared/utils';

import Link from '@mui/material/Link';
import { styled } from '@mui/material/styles';

import { RouterLink } from 'src/routes/components';

import { logoClasses } from './classes';

export type LogoProps = LinkProps & {
  isSingle?: boolean;
  disabled?: boolean;
};

export const Logo = forwardRef<HTMLAnchorElement, LogoProps>((props, ref) => {
  const { className, href = '/', isSingle = true, disabled, sx, ...other } = props;

  return (
    <LogoRoot
      ref={ref}
      component={RouterLink}
      href={href}
      aria-label="Annonai"
      underline="none"
      className={mergeClasses([logoClasses.root, className])}
      sx={[
        () => ({
          width: 40,
          height: 40,
          ...(!isSingle && { width: 150, height: 38 }),
          ...(disabled && { pointerEvents: 'none' }),
        }),
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
      {...other}
    >
      <img
        alt={isSingle ? 'Annonai logo' : 'Annonai'}
        src={isSingle ? '/logo/logo-single.svg' : '/logo/logo-full.svg'}
        width="100%"
        height="100%"
      />
    </LogoRoot>
  );
});

const LogoRoot = styled(Link)(() => ({
  flexShrink: 0,
  color: 'transparent',
  display: 'inline-flex',
  verticalAlign: 'middle',
  '& img': {
    display: 'block',
  },
}));

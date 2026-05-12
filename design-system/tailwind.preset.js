const tokens = require('./tokens.json');

module.exports = {
  darkMode: ['class', '[data-theme="dark"]'],
  theme: {
    screens: {
      tablet: '768px',
      desktop: '1280px',
      wide: '1536px'
    },
    extend: {
      colors: {
        brand: tokens.colors.brand,
        neutral: tokens.colors.neutral,
        success: tokens.colors.success,
        warning: tokens.colors.warning,
        danger: tokens.colors.danger,
        info: tokens.colors.info,
        app: {
          light: tokens.colors.light.app,
          dark: tokens.colors.dark.app
        },
        surface: {
          DEFAULT: tokens.colors.light.surface,
          muted: tokens.colors.light['surface-muted'],
          dark: tokens.colors.dark.surface,
          'dark-muted': tokens.colors.dark['surface-muted']
        }
      },
      fontFamily: tokens.typography.fontFamily,
      fontSize: tokens.typography.fontSize,
      fontWeight: tokens.typography.fontWeight,
      spacing: tokens.spacing,
      borderRadius: tokens.radius,
      boxShadow: tokens.shadow,
      transitionDuration: tokens.motion.duration,
      transitionTimingFunction: tokens.motion.easing
    }
  }
};

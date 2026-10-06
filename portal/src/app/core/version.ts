// Replaced at build time through the "define" option of angular.json (from package.json).
declare const MC_APP_VERSION: string | undefined;

export const APP_VERSION: string = typeof MC_APP_VERSION === 'string' ? MC_APP_VERSION : '0.0.0-dev';

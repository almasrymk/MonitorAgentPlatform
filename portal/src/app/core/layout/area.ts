/** The root of the current area: `/app`, `/admin/customers/{tenantId}` (workspace) or `/admin`. */
export function areaRoot(url: string): string {
  const path = url.split(/[?#]/)[0];
  const workspace = /^\/admin\/customers\/[^/]+/.exec(path);
  if (workspace) {
    return workspace[0];
  }
  return path.startsWith('/app') ? '/app' : '/admin';
}

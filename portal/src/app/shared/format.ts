import { I18nService } from '../core/i18n/i18n.service';

/** "just now", "5 min ago", "3 h ago", "2 d ago" for a past timestamp. */
export function relativeTime(i18n: I18nService, at: string | null | undefined, now: number = Date.now()): string {
  if (!at) {
    return '—';
  }
  const seconds = Math.max(0, Math.round((now - new Date(at).getTime()) / 1000));
  if (seconds < 60) {
    return i18n.t('time.justNow');
  }
  if (seconds < 3600) {
    return i18n.t('time.minutesAgo', { n: Math.floor(seconds / 60) });
  }
  if (seconds < 86400) {
    return i18n.t('time.hoursAgo', { n: Math.floor(seconds / 3600) });
  }
  return i18n.t('time.daysAgo', { n: Math.floor(seconds / 86400) });
}

/** Uptime as "3d 4h", "5h 12m" or "8m". */
export function uptime(i18n: I18nService, seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) {
    return '—';
  }
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  if (days > 0) {
    return i18n.t('time.uptimeDays', { d: days, h: hours });
  }
  if (hours > 0) {
    return i18n.t('time.uptimeHours', { h: hours, m: minutes });
  }
  return i18n.t('time.uptimeMinutes', { m: minutes });
}

/** Percent with at most one decimal, or an em dash. */
export function percent(value: number | null | undefined): string {
  return value === null || value === undefined ? '—' : `${Math.round(value * 10) / 10}%`;
}

import { KpiTileData } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { Tone } from '../../shared/ui/kpi-tile';

export interface TileView {
  key: string;
  label: string;
  value: number;
  icon: string;
  tone: Tone;
  delta: number | null;
  deltaPercent: number | null;
  caption: string | undefined;
  goodWhen: 'up' | 'down';
}

const LOOK: Record<string, { icon: string; tone: Tone; goodWhen?: 'up' | 'down' }> = {
  customers: { icon: 'customers', tone: 'info' },
  locations: { icon: 'location', tone: 'info' },
  devices: { icon: 'devices', tone: 'brand' },
  online: { icon: 'check', tone: 'success' },
  offline: { icon: 'devices', tone: 'neutral', goodWhen: 'down' },
  healthy: { icon: 'check', tone: 'success' },
  warning: { icon: 'alert', tone: 'warning', goodWhen: 'down' },
  critical: { icon: 'alert', tone: 'danger', goodWhen: 'down' },
  licensed: { icon: 'subscription', tone: 'info' },
  unlicensed: { icon: 'subscription', tone: 'neutral', goodWhen: 'down' },
  newDevices: { icon: 'plus', tone: 'brand' },
};

/** KPI tiles of the dashboards: label, icon and colour by key; caption "vs last 30 days" or "% of total". */
export function tileViews(i18n: I18nService, tiles: KpiTileData[]): TileView[] {
  return tiles.map((t) => {
    const look = LOOK[t.key] ?? { icon: 'dashboard', tone: 'neutral' as Tone };
    const caption = t.delta !== null ? i18n.t('dashboard.vsLast30') : t.percentOfTotal !== null ? i18n.t('dashboard.ofTotal', { percent: t.percentOfTotal }) : undefined;
    return {
      key: t.key,
      label: i18n.t(`dashboard.tile.${t.key}`),
      value: t.value,
      icon: look.icon,
      tone: look.tone,
      delta: t.delta,
      deltaPercent: t.deltaPercent,
      caption,
      goodWhen: look.goodWhen ?? 'up',
    };
  });
}

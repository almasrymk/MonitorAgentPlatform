import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input } from '@angular/core';

import { Chart, tokenColor } from './chart';

export interface TrendSeries {
  name: string;
  /** A design token without the leading dashes, e.g. `mc-series-cpu`. */
  color: string;
  points: { at: string; value: number | null }[];
}

/** Line / area chart over time (device history, incident trend). */
@Component({
  selector: 'mc-trend-chart',
  imports: [Chart],
  template: `<mc-chart [option]="option()" [height]="height()" [ariaLabel]="summary()" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrendChart {
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly series = input.required<TrendSeries[]>();
  readonly unit = input('%');
  readonly area = input(true);
  readonly height = input(160);
  readonly max = input<number | null>(100);

  /** "CPU: last 27%, max 91%" for screen readers. */
  readonly summary = computed(() =>
    this.series()
      .map((s) => {
        const values = s.points.map((p) => p.value).filter((v): v is number => v !== null);
        return values.length ? `${s.name}: last ${values[values.length - 1]}${this.unit()}, max ${Math.max(...values)}${this.unit()}` : `${s.name}: no data`;
      })
      .join('; '),
  );

  readonly option = computed(() => {
    const element = this.host.nativeElement as HTMLElement;
    const grid = tokenColor('mc-border', element);
    const text = tokenColor('mc-text-muted', element);
    return {
      animation: false,
      grid: { left: 36, right: 8, top: 8, bottom: 24 },
      tooltip: { trigger: 'axis' },
      xAxis: { type: 'time', axisLine: { lineStyle: { color: grid } }, axisLabel: { color: text, fontSize: 10, hideOverlap: true }, splitLine: { show: false } },
      yAxis: { type: 'value', max: this.max() ?? undefined, min: 0, axisLabel: { color: text, fontSize: 10 }, splitLine: { lineStyle: { color: grid, opacity: 0.5 } } },
      series: this.series().map((s) => {
        const color = tokenColor(s.color, element);
        return {
          name: s.name,
          type: 'line',
          showSymbol: false,
          smooth: true,
          lineStyle: { width: 2, color },
          itemStyle: { color },
          areaStyle: this.area() ? { color, opacity: 0.15 } : undefined,
          data: s.points.map((p) => [p.at, p.value]),
        };
      }),
    };
  });
}

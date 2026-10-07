import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, effect, ElementRef, inject, input, viewChild } from '@angular/core';

export interface DonutSegment {
  key: string;
  label: string;
  value: number;
  /** A design token name without the leading dashes, e.g. `mc-success`. */
  color: string;
}

interface ChartHandle {
  setOption(option: object, notMerge?: boolean): void;
  resize(): void;
  dispose(): void;
}

/**
 * Donut chart on Apache ECharts (loaded on demand), with the total in the centre and an HTML legend with count
 * and percentage. The chart has a text summary in `aria-label` (07 section 4).
 */
@Component({
  selector: 'mc-donut-chart',
  template: `
    <div class="chart-wrap">
      <div #canvas class="canvas" role="img" [attr.aria-label]="summary()" [style.block-size.px]="size()" [style.inline-size.px]="size()"></div>
      <div class="center" aria-hidden="true">
        <span class="total" data-testid="donut-total">{{ total() }}</span>
        @if (centerLabel()) {
          <span class="caption">{{ centerLabel() }}</span>
        }
      </div>
    </div>
    <ul class="legend">
      @for (s of rows(); track s.key) {
        <li data-testid="donut-legend">
          <span class="dot" [style.background]="'var(--' + s.color + ')'"></span>
          <span class="name">{{ s.label }}</span>
          <strong>{{ s.value }}</strong>
          <span class="pct">{{ s.percent }}%</span>
        </li>
      }
    </ul>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-5); flex-wrap: wrap; }
    .chart-wrap { position: relative; }
    .center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
    .total { font-size: var(--mc-fs-xl); font-weight: var(--mc-fw-bold); }
    .caption { font-size: var(--mc-fs-xs); color: var(--mc-text-muted); }
    .legend { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--mc-space-2); flex: 1; min-inline-size: 10em; }
    li { display: grid; grid-template-columns: 10px 1fr auto 3.5em; align-items: center; gap: var(--mc-space-2); font-size: var(--mc-fs-sm); }
    .dot { inline-size: 10px; block-size: 10px; border-radius: 50%; }
    .pct { color: var(--mc-text-muted); text-align: end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DonutChart {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private chart: ChartHandle | null = null;
  private observer: ResizeObserver | null = null;

  readonly segments = input.required<DonutSegment[]>();
  readonly centerLabel = input<string>();
  readonly ariaLabel = input<string>();
  readonly size = input(160);

  readonly total = computed(() => this.segments().reduce((sum, s) => sum + s.value, 0));
  readonly rows = computed(() => {
    const total = this.total();
    return this.segments().map((s) => ({ ...s, percent: total === 0 ? 0 : Math.round((s.value / total) * 1000) / 10 }));
  });
  readonly summary = computed(() => this.ariaLabel() ?? this.rows().map((r) => `${r.label} ${r.value} (${r.percent}%)`).join(', '));

  constructor() {
    afterNextRender(() => void this.init());
    effect(() => {
      const rows = this.rows();
      this.chart?.setOption(this.option(rows), true);
    });
    inject(DestroyRef).onDestroy(() => {
      this.observer?.disconnect();
      this.chart?.dispose();
    });
  }

  private async init(): Promise<void> {
    try {
      const [core, charts, renderers] = await Promise.all([import('echarts/core'), import('echarts/charts'), import('echarts/renderers')]);
      core.use([charts.PieChart, renderers.CanvasRenderer]);
      this.chart = core.init(this.canvas().nativeElement, undefined, { renderer: 'canvas' }) as unknown as ChartHandle;
      this.chart.setOption(this.option(this.rows()), true);
      if (typeof ResizeObserver !== 'undefined') {
        this.observer = new ResizeObserver(() => this.chart?.resize());
        this.observer.observe(this.canvas().nativeElement);
      }
    } catch {
      // No canvas (tests, very old browsers): the legend and the text summary still carry the data.
      this.chart = null;
    }
  }

  private option(rows: (DonutSegment & { percent: number })[]): object {
    const style = getComputedStyle(this.host.nativeElement as HTMLElement);
    const color = (token: string) => style.getPropertyValue(`--${token}`).trim() || '#888';
    const data = rows.filter((r) => r.value > 0).map((r) => ({ name: r.label, value: r.value, itemStyle: { color: color(r.color) } }));
    return {
      animation: false,
      series: [
        {
          type: 'pie',
          radius: ['68%', '90%'],
          avoidLabelOverlap: true,
          label: { show: false },
          labelLine: { show: false },
          itemStyle: { borderWidth: 2, borderColor: color('mc-bg-card') },
          data: data.length ? data : [{ name: '', value: 1, itemStyle: { color: color('mc-bg-card-raised') } }],
        },
      ],
    };
  }
}

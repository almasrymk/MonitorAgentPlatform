import { afterNextRender, ChangeDetectionStrategy, Component, DestroyRef, effect, ElementRef, inject, input, viewChild } from '@angular/core';

interface ChartHandle {
  setOption(option: object, notMerge?: boolean): void;
  resize(): void;
  dispose(): void;
}

/** The value of a design token (e.g. `mc-series-cpu`) for charts, which cannot read CSS variables themselves. */
export function tokenColor(token: string, element: Element = document.documentElement): string {
  return getComputedStyle(element).getPropertyValue(`--${token}`).trim() || '#888888';
}

/**
 * `mc-chart`: an Apache ECharts canvas for any option (line, area, gauge). ECharts is loaded on demand; without a
 * canvas (tests) only the text summary in `aria-label` remains (07 section 4).
 */
@Component({
  selector: 'mc-chart',
  template: `<div #canvas class="canvas" role="img" [attr.aria-label]="ariaLabel()" [style.block-size.px]="height()"></div>`,
  styles: `
    :host { display: block; min-inline-size: 0; }
    .canvas { inline-size: 100%; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Chart {
  private readonly canvas = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private chart: ChartHandle | null = null;
  private observer: ResizeObserver | null = null;

  readonly option = input.required<object>();
  readonly height = input(180);
  readonly ariaLabel = input('');

  constructor() {
    afterNextRender(() => void this.init());
    effect(() => {
      const option = this.option();
      this.chart?.setOption(option, true);
    });
    inject(DestroyRef).onDestroy(() => {
      this.observer?.disconnect();
      this.chart?.dispose();
    });
  }

  private async init(): Promise<void> {
    try {
      const echarts = await import('./echarts-setup');
      this.chart = echarts.init(this.canvas().nativeElement, undefined, { renderer: 'canvas' }) as unknown as ChartHandle;
      this.chart.setOption(this.option(), true);
      if (typeof ResizeObserver !== 'undefined') {
        this.observer = new ResizeObserver(() => this.chart?.resize());
        this.observer.observe(this.canvas().nativeElement);
      }
    } catch {
      this.chart = null;
    }
  }
}

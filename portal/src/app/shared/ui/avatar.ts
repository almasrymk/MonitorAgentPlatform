import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Initials on the avatar colour. */
@Component({
  selector: 'mc-avatar',
  template: '{{ initials() }}',
  host: { '[attr.aria-label]': 'name()', role: 'img', '[style.--size.px]': 'size()' },
  styles: `
    :host { display: inline-flex; align-items: center; justify-content: center; inline-size: var(--size); block-size: var(--size); border-radius: 50%; background: var(--mc-avatar); color: #fff; font-weight: var(--mc-fw-semibold); font-size: calc(var(--size) * 0.4); flex: none; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Avatar {
  readonly name = input.required<string>();
  readonly size = input(32);
  /** One letter for a company, two for a person ("Admin User" -> "AU"). */
  readonly letters = input<1 | 2>(2);
  protected readonly initials = computed(() => {
    const words = this.name().trim().split(/\s+/).filter(Boolean);
    const letters = words.slice(0, this.letters()).map((w) => w[0]?.toUpperCase() ?? '');
    return letters.join('') || '?';
  });
}

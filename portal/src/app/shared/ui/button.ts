import { Directive, computed, input } from '@angular/core';

export type ButtonVariant = 'primary-solid' | 'primary-outline' | 'secondary' | 'danger-outline' | 'success-outline' | 'ghost' | 'icon';

/** `<button mcButton="primary-solid">` - native buttons with the variants of 07 section 4. */
@Directive({
  selector: 'button[mcButton], a[mcButton]',
  host: { '[class]': 'classes()' },
})
export class Button {
  readonly mcButton = input<ButtonVariant | ''>('secondary');
  readonly size = input<'sm' | 'md'>('md');
  protected readonly classes = computed(() => `mc-btn mc-btn--${this.mcButton() || 'secondary'} mc-btn--${this.size()}`);
}

import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

export interface SelectOption {
  value: string;
  label: string;
}

@Component({
  selector: 'mc-select',
  template: `
    <select [value]="value()" [attr.aria-label]="label()" (change)="value.set($any($event.target).value)">
      @for (option of options(); track option.value) {
        <option [value]="option.value" [selected]="option.value === value()">{{ option.label }}</option>
      }
    </select>
  `,
  styles: `
    :host { display: inline-block; }
    select { block-size: 36px; padding-inline: var(--mc-space-3); background: var(--mc-bg-input); color: var(--mc-text); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); font: inherit; min-inline-size: 140px; }
    select:focus-visible { outline: none; box-shadow: var(--mc-focus-ring); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Select {
  readonly options = input.required<SelectOption[]>();
  readonly label = input.required<string>();
  readonly value = model('');
}

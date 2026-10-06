import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

/** Creates a component, sets its inputs and waits for the first render. */
export async function render<T>(component: Type<T>, inputs: Record<string, unknown> = {}): Promise<{ fixture: ComponentFixture<T>; host: HTMLElement }> {
  const fixture = TestBed.createComponent(component);
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, host: fixture.nativeElement as HTMLElement };
}

export function text(host: HTMLElement, selector: string): string {
  return host.querySelector(selector)?.textContent?.trim() ?? '';
}

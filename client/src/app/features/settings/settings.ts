import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LibraryStore } from '../../core/state/library-store';
import { ThemeService } from '../../core/theme/theme.service';
import { Avatar } from '../../shared/components/avatar';
import { Person } from '../../core/models/knowledge.models';

@Component({
  selector: 'dh-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar],
  host: { class: 'block' },
  templateUrl: './settings.html',
})
export class SettingsPage {
  protected readonly theme = inject(ThemeService);
  protected readonly store = inject(LibraryStore);

  protected readonly currentUser: Person = {
    id: 'u1',
    name: 'Ana Ruiz',
    initials: 'AR',
    tint: '#7c5cff',
  };

  protected readonly themes = [
    {
      mode: 'dark' as const,
      label: 'Dark',
      hint: 'Default. Easier for long reading sessions.',
      icon: 'pi-moon',
      swatch: '#0f1117',
      fg: '#b09bff',
    },
    {
      mode: 'light' as const,
      label: 'Light',
      hint: 'Higher contrast in bright rooms.',
      icon: 'pi-sun',
      swatch: '#ffffff',
      fg: '#6b45f5',
    },
  ];

  protected setView(event: Event): void {
    this.store.viewMode.set((event.target as HTMLSelectElement).value as 'list' | 'grid');
  }
}

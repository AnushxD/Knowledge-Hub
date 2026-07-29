import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { LibraryStore } from '../core/state/library-store';

/**
 * A quick way into the assistant from any screen.
 *
 * It deliberately does not answer anything itself. Streaming, citation
 * verification and refusal rendering all live on the assistant screen, and a
 * second implementation of them here would be a second place for a grounding
 * bug to hide. The dock takes a question and hands it over.
 *
 * What it does own is the explanation: which sources an answer can come from,
 * and the rules the answer obeys. That is worth having one click away rather
 * than only on the screen you reach after deciding to trust it.
 */
@Component({
  selector: 'dh-ai-dock',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  // `contents` keeps the host out of the shell's flex layout — everything it
  // renders is fixed-position anyway.
  host: { class: 'contents' },
  templateUrl: './ai-dock.html',
})
export class AiDock {
  private readonly store = inject(LibraryStore);
  private readonly router = inject(Router);

  protected readonly open = signal(false);
  protected readonly draft = signal('');

  protected readonly indexed = computed(() => this.store.stats()?.indexed ?? 0);

  /**
   * Nothing to ground an answer in yet. Worth saying before someone types a
   * question and gets a refusal that reads like a fault.
   */
  protected readonly hasIndexedContent = computed(() => this.indexed() > 0);

  protected ask(): void {
    const question = this.draft().trim();
    if (!question) return;

    this.draft.set('');
    this.open.set(false);

    // Carried in the URL rather than in shared state, so the resulting
    // conversation is linkable and survives a reload like any other.
    void this.router.navigate(['/chat'], { queryParams: { q: question } });
  }
}

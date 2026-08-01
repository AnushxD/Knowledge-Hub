import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { KnowledgeGateway } from '../../core/data/knowledge-gateway';
import { LibraryStore } from '../../core/state/library-store';
import { ChatMessage, ChatSession, Citation } from '../../core/models/knowledge.models';
import { EmptyState } from '../../shared/components/empty-state';
import { TooltipDirective } from '../../shared/directives/tooltip.directive';
import { CitationText } from './citation-text';

/** A turn as rendered, including the one still streaming. */
interface Turn {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  citations: Citation[];
  isRefusal: boolean;
  /** Sources that could not be searched for this answer. Usually empty. */
  degradations: string[];
  /** True while tokens are still arriving for this turn. */
  streaming: boolean;
}

@Component({
  selector: 'dh-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, EmptyState, TooltipDirective, CitationText],
  host: { class: 'block' },
  templateUrl: './chat.html',
})
export class ChatPage {
  private readonly gateway = inject(KnowledgeGateway);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(LibraryStore);

  private readonly transcriptEl = viewChild<ElementRef<HTMLElement>>('transcript');
  private stream?: Subscription;

  protected readonly draft = signal('');
  protected readonly turns = signal<Turn[]>([]);
  protected readonly sessions = signal<ChatSession[]>([]);
  protected readonly sessionId = signal<string | null>(null);
  protected readonly sending = signal(false);
  protected readonly failure = signal<string | null>(null);

  /**
   * Sources for the answer being generated, shown before the first token so
   * the grounding is visible while the answer is still being written.
   */
  protected readonly pendingSources = signal<Citation[]>([]);

  /**
   * One chip per source being read, not one per passage.
   *
   * Keyed on whatever identifies it to a reader: a document id collapses
   * several chunks of one file into one chip, and an external passage — which
   * has no document id — falls back to its link, then its title.
   */
  protected readonly readingSources = computed(() => {
    const seen = new Map<string, Citation>();
    for (const source of this.pendingSources()) {
      const key = source.documentId ?? source.url ?? source.title;
      if (!seen.has(key)) seen.set(key, source);
    }
    return [...seen.values()];
  });

  protected readonly indexed = computed(() => this.store.stats()?.indexed ?? 0);
  protected readonly canSend = computed(() => this.draft().trim().length > 0 && !this.sending());

  protected readonly suggestions = [
    'How do I connect remotely?',
    'What happens if ingestion fails?',
    'What are the password requirements?',
  ];

  constructor() {
    this.loadSessions();

    // A question handed over from the assistant dock, as ?q=. Asked once, then
    // dropped from the URL: leaving it there would re-ask on every reload, and
    // a shared link would silently start someone else's conversation.
    const params = this.route.snapshot.queryParamMap;
    const handedOver = params.get('q')?.trim();
    const prefill = params.get('draft');

    if (handedOver || prefill) {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { q: null, draft: null },
        replaceUrl: true,
      });
    }

    // `q` is a finished question and is asked; `draft` only fills the box,
    // because "ask about this document" is a starting point, not a question.
    if (handedOver) this.runSuggestion(handedOver);
    else if (prefill) this.draft.set(prefill);
  }

  protected onInput(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  /** Enter sends; Shift+Enter is a newline, as in every chat product. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.send();
    }
  }

  protected send(): void {
    const question = this.draft().trim();
    if (!question || this.sending()) return;

    this.draft.set('');
    this.failure.set(null);
    this.pendingSources.set([]);
    this.sending.set(true);

    this.turns.update((turns) => [
      ...turns,
      {
        id: `local-${Date.now()}`,
        role: 'user',
        content: question,
        citations: [],
        isRefusal: false,
        degradations: [],
        streaming: false,
      },
      {
        id: 'pending',
        role: 'assistant',
        content: '',
        citations: [],
        isRefusal: false,
        degradations: [],
        streaming: true,
      },
    ]);

    this.scrollToEnd();

    this.stream = this.gateway.ask({ question, sessionId: this.sessionId() }).subscribe({
      next: (event) => {
        switch (event.type) {
          case 'session':
            this.sessionId.set(event.sessionId);
            break;

          case 'sources':
            this.pendingSources.set(event.sources);
            break;

          case 'token':
            this.appendToPending(event.text);
            break;

          case 'done':
            this.completePending(
              event.content,
              event.citations,
              event.isRefusal,
              event.messageId,
              event.degradations ?? [],
            );
            break;

          case 'error':
            this.fail(event.reason);
            break;
        }
      },
      error: (error: unknown) =>
        this.fail(error instanceof Error ? error.message : 'The assistant is unavailable.'),
      complete: () => {
        this.sending.set(false);
        this.loadSessions();
      },
    });
  }

  /** Abandons the in-flight answer; the fetch is aborted server-side too. */
  protected stop(): void {
    this.stream?.unsubscribe();
    this.sending.set(false);
    this.turns.update((turns) =>
      turns.filter((turn) => !(turn.streaming && turn.content.length === 0)),
    );
    this.turns.update((turns) =>
      turns.map((turn) => (turn.streaming ? { ...turn, streaming: false } : turn)),
    );
  }

  protected newConversation(): void {
    this.stream?.unsubscribe();
    this.sessionId.set(null);
    this.turns.set([]);
    this.pendingSources.set([]);
    this.failure.set(null);
    this.sending.set(false);
  }

  protected openSession(session: ChatSession): void {
    this.stream?.unsubscribe();
    this.sending.set(false);
    this.failure.set(null);
    this.pendingSources.set([]);
    this.sessionId.set(session.id);

    this.gateway.chatTranscript(session.id).subscribe({
      next: (transcript) => {
        this.turns.set(transcript.messages.map(toTurn));
        this.scrollToEnd();
      },
      error: () => this.failure.set('That conversation could not be loaded.'),
    });
  }

  protected deleteSession(session: ChatSession, event: Event): void {
    event.stopPropagation();

    this.gateway.deleteChatSession(session.id).subscribe({
      next: () => {
        if (this.sessionId() === session.id) this.newConversation();
        this.loadSessions();
      },
      error: () => this.failure.set('That conversation could not be deleted.'),
    });
  }

  protected runSuggestion(text: string): void {
    this.draft.set(text);
    this.send();
  }

  private appendToPending(text: string): void {
    this.turns.update((turns) =>
      turns.map((turn) => (turn.streaming ? { ...turn, content: turn.content + text } : turn)),
    );
    this.scrollToEnd();
  }

  private completePending(
    content: string,
    citations: Citation[],
    isRefusal: boolean,
    messageId: string,
    degradations: string[],
  ): void {
    this.turns.update((turns) =>
      turns.map((turn) =>
        turn.streaming
          ? {
              ...turn,
              id: messageId,
              // Replaces what streamed. An answer the server would not stand
              // behind must not stay on screen just because it arrived.
              content,
              citations,
              isRefusal,
              degradations,
              streaming: false,
            }
          : turn,
      ),
    );
    this.pendingSources.set([]);
    this.scrollToEnd();
  }

  private fail(reason: string): void {
    this.failure.set(reason);
    this.sending.set(false);
    // Drop the empty placeholder so a failure doesn't leave a blank bubble.
    this.turns.update((turns) =>
      turns.filter((turn) => !(turn.streaming && turn.content.length === 0)),
    );
    this.turns.update((turns) =>
      turns.map((turn) => (turn.streaming ? { ...turn, streaming: false } : turn)),
    );
  }

  private loadSessions(): void {
    this.gateway.chatSessions().subscribe({
      next: (sessions) => this.sessions.set(sessions),
      error: () => this.sessions.set([]),
    });
  }

  private scrollToEnd(): void {
    // After the signal update has rendered.
    queueMicrotask(() => {
      const element = this.transcriptEl()?.nativeElement;
      if (element) element.scrollTop = element.scrollHeight;
    });
  }
}

function toTurn(message: ChatMessage): Turn {
  return {
    id: message.id,
    role: message.role,
    content: message.content,
    citations: message.citations,
    isRefusal: message.isRefusal,
    degradations: message.degradations ?? [],
    streaming: false,
  };
}

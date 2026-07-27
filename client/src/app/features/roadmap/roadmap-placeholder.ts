import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Reserved routes. The roadmap in CLAUDE.md is deliberate — search lands in
 * phase 2, the assistant in phase 3, MCP sources in phase 4 — so these screens
 * exist as designed placeholders rather than half-built features. They keep
 * the navigation model honest and document the intent for whoever builds them.
 */
@Component({
  selector: 'dh-roadmap-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  host: { class: 'block' },
  template: `
    <div class="dh-aurora mx-auto max-w-3xl px-6 py-14 lg:px-10">
      <span
        class="inline-flex items-center gap-2 rounded-full border px-3 py-1 text-[11.5px] font-medium"
        style="border-color: color-mix(in oklab, var(--dh-ai-from) 35%, transparent);
               background: color-mix(in oklab, var(--dh-ai-from) 12%, transparent);
               color: var(--dh-brand-400)"
      >
        <i class="pi pi-map text-[11px]"></i>
        Roadmap phase {{ phase() }}
      </span>

      <h1 class="mt-4 text-[27px] leading-tight font-semibold text-ink">{{ heading() }}</h1>
      <p class="mt-3 max-w-2xl text-[14px] leading-relaxed text-muted">{{ blurb() }}</p>

      <div class="mt-8 grid gap-3 sm:grid-cols-2">
        @for (item of features(); track item.title) {
          <div class="dh-card p-4">
            <span
              class="dh-ai-surface mb-3 grid size-9 place-items-center rounded-[11px] text-[14px] text-brand-400"
            >
              <i class="pi" [class]="item.icon"></i>
            </span>
            <p class="text-[13px] font-semibold text-ink">{{ item.title }}</p>
            <p class="mt-1 text-[12.5px] leading-relaxed text-muted">{{ item.detail }}</p>
          </div>
        }
      </div>

      <div class="dh-card mt-6 p-5">
        <p class="dh-eyebrow mb-3">Blocked on</p>
        <ul class="space-y-2">
          @for (dep of dependencies(); track dep) {
            <li class="flex items-start gap-2.5 text-[12.5px] text-muted">
              <i class="pi pi-lock mt-0.5 text-[11px] text-subtle"></i>
              {{ dep }}
            </li>
          }
        </ul>
      </div>

      <div class="mt-7 flex flex-wrap gap-2">
        <a routerLink="/browse" class="dh-btn-primary">
          <i class="pi pi-folder-open text-[12px]"></i>
          Go to the library
        </a>
        <a routerLink="/" class="dh-btn-ghost">Back to home</a>
      </div>
    </div>
  `,
})
export class RoadmapPlaceholder {
  readonly phase = input.required<number>();
  readonly heading = input.required<string>();
  readonly blurb = input.required<string>();
  readonly features = input.required<{ icon: string; title: string; detail: string }[]>();
  readonly dependencies = input.required<string[]>();
}

@Component({
  selector: 'dh-search-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  template: `
    <dh-roadmap-placeholder
      [phase]="2"
      heading="Hybrid search"
      blurb="Keyword and semantic retrieval over every indexed chunk, merged into one ranked list. Until the ingestion pipeline produces embeddings there is nothing to search, so this ships with phase 2 rather than as an empty box."
      [features]="features"
      [dependencies]="dependencies"
    />
  `,
})
export class SearchPlaceholder {
  protected readonly features = [
    {
      icon: 'pi-align-left',
      title: 'Keyword matching',
      detail: 'Postgres full-text search over extracted content, with the matched snippet shown.',
    },
    {
      icon: 'pi-compass',
      title: 'Semantic matching',
      detail: 'pgvector cosine similarity, so “how do I run this locally” finds “Docker setup”.',
    },
    {
      icon: 'pi-sort-alt',
      title: 'One merged ranking',
      detail: 'Reciprocal rank fusion across both, with a badge saying which strategy matched.',
    },
    {
      icon: 'pi-filter',
      title: 'Facets that carry over',
      detail: 'The same folder, type, tag and status filters used in the library.',
    },
  ];
  protected readonly dependencies = [
    'Phase 2 — text extraction and chunking pipeline',
    'Phase 2 — embedding provider behind IEmbeddingProvider',
    'Phase 2 — pgvector index over DocumentChunks',
  ];
}

@Component({
  selector: 'dh-chat-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  template: `
    <dh-roadmap-placeholder
      [phase]="3"
      heading="AI assistant"
      blurb="Answers grounded strictly in retrieved content, with a clickable citation behind every claim. The rule that matters most: when retrieval comes back empty it says so, rather than guessing."
      [features]="features"
      [dependencies]="dependencies"
    />
  `,
})
export class ChatPlaceholder {
  protected readonly features = [
    {
      icon: 'pi-link',
      title: 'Citations that resolve',
      detail: 'Each claim links to /docs/:id?chunk=N — the exact passage, already wired up.',
    },
    {
      icon: 'pi-eye',
      title: 'Visible retrieval',
      detail: 'The sources it searched, and what it found, shown before the answer streams.',
    },
    {
      icon: 'pi-ban',
      title: 'A designed “I don’t know”',
      detail: 'Empty retrieval produces an explicit no-answer card, never a plausible guess.',
    },
    {
      icon: 'pi-sliders-h',
      title: 'Scoped questions',
      detail: 'Ask against the whole library, one folder, or the document you are reading.',
    },
  ];
  protected readonly dependencies = [
    'Phase 2 — working hybrid search to retrieve against',
    'Phase 3 — RAG orchestrator in the Service layer',
    'Phase 3 — ILlmProvider wired to the Claude API with a strict grounding prompt',
  ];
}

@Component({
  selector: 'dh-sources-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  template: `
    <dh-roadmap-placeholder
      [phase]="4"
      heading="Knowledge sources"
      blurb="Uploaded documents are one source of truth; the team's repositories are another. Both sit behind IKnowledgeSource, so adding repository search later changes the aggregator — not the assistant, and not this UI."
      [features]="features"
      [dependencies]="dependencies"
    />
  `,
})
export class SourcesPlaceholder {
  protected readonly features = [
    {
      icon: 'pi-database',
      title: 'Documents',
      detail: 'Always active. Hybrid search over everything uploaded here.',
    },
    {
      icon: 'pi-github',
      title: 'Repositories via MCP',
      detail: 'Stubbed locally, real in production — the same interface either way.',
    },
    {
      icon: 'pi-heart',
      title: 'Per-source health',
      detail: 'Connection state, last sync, and result counts, so a silent source is visible.',
    },
    {
      icon: 'pi-toggle-on',
      title: 'Per-source toggles',
      detail: 'Turn a source off for a question without disconnecting it.',
    },
  ];
  protected readonly dependencies = [
    'Phase 3 — a working assistant to aggregate sources for',
    'Phase 4 — IKnowledgeSource and CompositeKnowledgeSource in the Integrations layer',
    'Phase 7 — the real MCP client, once the server is available',
  ];
}

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RoadmapPlaceholder } from './roadmap-placeholder';

@Component({
  selector: 'dh-chat-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  templateUrl: './chat-placeholder.html',
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

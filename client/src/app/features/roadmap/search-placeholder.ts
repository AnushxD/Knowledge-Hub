import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RoadmapPlaceholder } from './roadmap-placeholder';

@Component({
  selector: 'dh-search-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  templateUrl: './search-placeholder.html',
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

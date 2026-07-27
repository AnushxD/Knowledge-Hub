import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RoadmapPlaceholder } from './roadmap-placeholder';

@Component({
  selector: 'dh-sources-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RoadmapPlaceholder],
  templateUrl: './sources-placeholder.html',
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

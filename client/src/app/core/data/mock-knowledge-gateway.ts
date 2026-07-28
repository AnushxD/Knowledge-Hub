import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, map, of, timer } from 'rxjs';
import { delayWhen, take } from 'rxjs/operators';
import {
  ActivityEvent,
  DocumentDetail,
  DocumentQuery,
  DocumentSection,
  DocumentSummary,
  DocumentVersion,
  Folder,
  IngestionStatus,
  LibraryStats,
  Person,
  SearchQuery,
  SearchResponse,
  SearchResult,
} from '../models/knowledge.models';
import { kindFromFileName } from '../utils/file-kind';
import { KnowledgeGateway } from './knowledge-gateway';

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

const ago = (ms: number) => new Date(Date.now() - ms).toISOString();

const PEOPLE: Person[] = [
  { id: 'u1', name: 'Ana Ruiz', initials: 'AR', tint: '#7c5cff' },
  { id: 'u2', name: 'Ravi Shankar', initials: 'RS', tint: '#22d3ee' },
  { id: 'u3', name: 'Mei Chen', initials: 'MC', tint: '#f472b6' },
  { id: 'u4', name: 'Tom Novak', initials: 'TN', tint: '#10b981' },
  { id: 'u5', name: 'Priya Nair', initials: 'PN', tint: '#f97316' },
];

const [ANA, RAVI, MEI, TOM, PRIYA] = PEOPLE;

const FOLDERS: Folder[] = [
  {
    id: 'f-eng',
    parentId: null,
    name: 'Engineering',
    path: 'Engineering',
    documentCount: 0,
    color: '#7c5cff',
  },
  {
    id: 'f-onb',
    parentId: 'f-eng',
    name: 'Onboarding',
    path: 'Engineering/Onboarding',
    documentCount: 0,
  },
  {
    id: 'f-env',
    parentId: 'f-onb',
    name: 'Environment Setup',
    path: 'Engineering/Onboarding/Environment Setup',
    documentCount: 0,
  },
  {
    id: 'f-arch',
    parentId: 'f-eng',
    name: 'Architecture',
    path: 'Engineering/Architecture',
    documentCount: 0,
  },
  {
    id: 'f-run',
    parentId: 'f-eng',
    name: 'Runbooks',
    path: 'Engineering/Runbooks',
    documentCount: 0,
  },
  {
    id: 'f-prod',
    parentId: null,
    name: 'Product',
    path: 'Product',
    documentCount: 0,
    color: '#22d3ee',
  },
  { id: 'f-spec', parentId: 'f-prod', name: 'Specs', path: 'Product/Specs', documentCount: 0 },
  { id: 'f-res', parentId: 'f-prod', name: 'Research', path: 'Product/Research', documentCount: 0 },
  {
    id: 'f-ops',
    parentId: null,
    name: 'Operations',
    path: 'Operations',
    documentCount: 0,
    color: '#10b981',
  },
  {
    id: 'f-dep',
    parentId: 'f-ops',
    name: 'Deployment',
    path: 'Operations/Deployment',
    documentCount: 0,
  },
  {
    id: 'f-sec',
    parentId: 'f-ops',
    name: 'Security',
    path: 'Operations/Security',
    documentCount: 0,
  },
  {
    id: 'f-des',
    parentId: null,
    name: 'Design',
    path: 'Design',
    documentCount: 0,
    color: '#f472b6',
  },
];

interface Seed {
  id: string;
  folderId: string;
  title: string;
  fileName: string;
  sizeBytes: number;
  version: number;
  tags: string[];
  owner: Person;
  updatedAt: string;
  status: IngestionStatus;
  indexProgress?: number;
  chunkCount?: number;
  failureReason?: string;
  description?: string;
  starred?: boolean;
}

const SEEDS: Seed[] = [
  {
    id: 'd-1',
    folderId: 'f-env',
    title: 'Dev Environment Setup',
    fileName: 'dev-environment-setup.pdf',
    sizeBytes: 2_215_400,
    version: 3,
    tags: ['setup', 'postgres', 'docker'],
    owner: ANA,
    updatedAt: ago(2 * DAY),
    status: 'indexed',
    chunkCount: 48,
    starred: true,
    description:
      'Everything a new engineer needs to get the API, client and local infrastructure running on day one.',
  },
  {
    id: 'd-2',
    folderId: 'f-env',
    title: 'Local Postgres & pgvector notes',
    fileName: 'local-postgres-notes.md',
    sizeBytes: 12_800,
    version: 1,
    tags: ['postgres', 'pgvector'],
    owner: RAVI,
    updatedAt: ago(52 * MINUTE),
    status: 'indexing',
    indexProgress: 62,
    description:
      'Connection strings, extension install, and the gotchas we hit with vector indexes.',
  },
  {
    id: 'd-3',
    folderId: 'f-onb',
    title: 'Engineering Handbook (scanned)',
    fileName: 'engineering-handbook.pdf',
    sizeBytes: 8_412_000,
    version: 1,
    tags: ['handbook', 'policy'],
    owner: ANA,
    updatedAt: ago(3 * DAY),
    status: 'failed',
    failureReason:
      'Text extraction returned no content — the PDF looks like scanned images. OCR fallback is not enabled yet.',
  },
  {
    id: 'd-4',
    folderId: 'f-onb',
    title: 'First Week Checklist',
    fileName: 'first-week-checklist.docx',
    sizeBytes: 84_200,
    version: 2,
    tags: ['onboarding'],
    owner: PRIYA,
    updatedAt: ago(6 * DAY),
    status: 'indexed',
    chunkCount: 11,
  },
  {
    id: 'd-5',
    folderId: 'f-arch',
    title: 'Knowledge Hub Technical Blueprint',
    fileName: 'architecture-blueprint.md',
    sizeBytes: 18_607,
    version: 4,
    tags: ['architecture', 'rag', 'mcp'],
    owner: MEI,
    updatedAt: ago(5 * HOUR),
    status: 'indexed',
    chunkCount: 96,
    starred: true,
    description:
      'System architecture, layering rules, ingestion pipeline and the MCP abstraction strategy.',
  },
  {
    id: 'd-6',
    folderId: 'f-arch',
    title: 'Service Layer Conventions',
    fileName: 'service-layer-conventions.md',
    sizeBytes: 9_940,
    version: 2,
    tags: ['architecture', 'conventions'],
    owner: MEI,
    updatedAt: ago(9 * DAY),
    status: 'indexed',
    chunkCount: 22,
  },
  {
    id: 'd-7',
    folderId: 'f-arch',
    title: 'Data Model — ERD',
    fileName: 'data-model-erd.drawio',
    sizeBytes: 141_000,
    version: 1,
    tags: ['architecture', 'database'],
    owner: TOM,
    updatedAt: ago(11 * DAY),
    status: 'indexed',
    chunkCount: 6,
  },
  {
    id: 'd-8',
    folderId: 'f-arch',
    title: 'Ingestion Pipeline Sequence',
    fileName: 'ingestion-sequence.png',
    sizeBytes: 486_000,
    version: 1,
    tags: ['architecture', 'ingestion'],
    owner: TOM,
    updatedAt: ago(12 * DAY),
    status: 'indexed',
    chunkCount: 2,
  },
  {
    id: 'd-9',
    folderId: 'f-run',
    title: 'Incident Response Runbook',
    fileName: 'incident-response.md',
    sizeBytes: 27_300,
    version: 7,
    tags: ['runbook', 'oncall'],
    owner: TOM,
    updatedAt: ago(20 * HOUR),
    status: 'indexed',
    chunkCount: 41,
  },
  {
    id: 'd-10',
    folderId: 'f-run',
    title: 'Database Restore Procedure',
    fileName: 'db-restore.md',
    sizeBytes: 15_100,
    version: 3,
    tags: ['runbook', 'postgres'],
    owner: RAVI,
    updatedAt: ago(4 * DAY),
    status: 'indexed',
    chunkCount: 18,
  },
  {
    id: 'd-11',
    folderId: 'f-run',
    title: 'Hangfire Job Failures — Triage',
    fileName: 'hangfire-triage.md',
    sizeBytes: 8_600,
    version: 1,
    tags: ['runbook', 'hangfire'],
    owner: RAVI,
    updatedAt: ago(38 * MINUTE),
    status: 'pending',
  },
  {
    id: 'd-12',
    folderId: 'f-spec',
    title: 'Knowledge Hub — Product Requirements',
    fileName: 'khub-prd.docx',
    sizeBytes: 320_500,
    version: 6,
    tags: ['prd', 'spec'],
    owner: PRIYA,
    updatedAt: ago(1 * DAY),
    status: 'indexed',
    chunkCount: 64,
    starred: true,
  },
  {
    id: 'd-13',
    folderId: 'f-spec',
    title: 'Search Relevance Requirements',
    fileName: 'search-relevance.md',
    sizeBytes: 14_200,
    version: 2,
    tags: ['spec', 'search'],
    owner: PRIYA,
    updatedAt: ago(2 * DAY),
    status: 'indexed',
    chunkCount: 19,
  },
  {
    id: 'd-14',
    folderId: 'f-spec',
    title: 'Citation UX Specification',
    fileName: 'citation-ux.pptx',
    sizeBytes: 4_120_000,
    version: 3,
    tags: ['spec', 'ux', 'citations'],
    owner: MEI,
    updatedAt: ago(3 * DAY),
    status: 'indexed',
    chunkCount: 28,
  },
  {
    id: 'd-15',
    folderId: 'f-res',
    title: 'User Interviews — Doc Discovery',
    fileName: 'user-interviews-q2.docx',
    sizeBytes: 210_000,
    version: 1,
    tags: ['research'],
    owner: PRIYA,
    updatedAt: ago(16 * DAY),
    status: 'indexed',
    chunkCount: 37,
  },
  {
    id: 'd-16',
    folderId: 'f-res',
    title: 'Competitive Landscape',
    fileName: 'competitive-landscape.xlsx',
    sizeBytes: 96_400,
    version: 2,
    tags: ['research'],
    owner: MEI,
    updatedAt: ago(21 * DAY),
    status: 'indexed',
    chunkCount: 9,
  },
  {
    id: 'd-17',
    folderId: 'f-res',
    title: 'Embedding Model Benchmarks',
    fileName: 'embedding-benchmarks.csv',
    sizeBytes: 44_800,
    version: 1,
    tags: ['research', 'embeddings'],
    owner: RAVI,
    updatedAt: ago(7 * DAY),
    status: 'indexed',
    chunkCount: 4,
  },
  {
    id: 'd-18',
    folderId: 'f-dep',
    title: 'IIS Deployment Guide',
    fileName: 'iis-deployment.pdf',
    sizeBytes: 1_840_000,
    version: 5,
    tags: ['deployment', 'iis'],
    owner: TOM,
    updatedAt: ago(2 * DAY),
    status: 'indexed',
    chunkCount: 52,
    description:
      'Step-by-step publish, app pool configuration and the permissions checklist for the org Windows box.',
  },
  {
    id: 'd-19',
    folderId: 'f-dep',
    title: 'GitHub Actions Pipeline',
    fileName: 'ci-pipeline.yml',
    sizeBytes: 6_300,
    version: 4,
    tags: ['ci', 'deployment'],
    owner: RAVI,
    updatedAt: ago(3 * HOUR),
    status: 'indexed',
    chunkCount: 5,
  },
  {
    id: 'd-20',
    folderId: 'f-dep',
    title: 'Azure App Service Migration Plan',
    fileName: 'azure-migration.pptx',
    sizeBytes: 3_610_000,
    version: 2,
    tags: ['azure', 'deployment'],
    owner: TOM,
    updatedAt: ago(8 * DAY),
    status: 'indexing',
    indexProgress: 24,
  },
  {
    id: 'd-21',
    folderId: 'f-sec',
    title: 'Secrets Management Policy',
    fileName: 'secrets-policy.md',
    sizeBytes: 11_900,
    version: 3,
    tags: ['security', 'policy'],
    owner: ANA,
    updatedAt: ago(5 * DAY),
    status: 'indexed',
    chunkCount: 16,
  },
  {
    id: 'd-22',
    folderId: 'f-sec',
    title: 'Entra ID SSO Configuration',
    fileName: 'entra-sso-config.pdf',
    sizeBytes: 940_000,
    version: 1,
    tags: ['security', 'auth'],
    owner: ANA,
    updatedAt: ago(14 * DAY),
    status: 'indexed',
    chunkCount: 24,
  },
  {
    id: 'd-23',
    folderId: 'f-sec',
    title: 'Access Review Q2',
    fileName: 'access-review-q2.xlsx',
    sizeBytes: 128_000,
    version: 1,
    tags: ['security', 'audit'],
    owner: ANA,
    updatedAt: ago(26 * DAY),
    status: 'failed',
    failureReason: 'File is password protected. Remove protection and re-upload to index it.',
  },
  {
    id: 'd-24',
    folderId: 'f-des',
    title: 'Design Tokens Reference',
    fileName: 'design-tokens.md',
    sizeBytes: 22_100,
    version: 5,
    tags: ['design-system'],
    owner: MEI,
    updatedAt: ago(4 * HOUR),
    status: 'indexed',
    chunkCount: 31,
  },
  {
    id: 'd-25',
    folderId: 'f-des',
    title: 'Component Library Audit',
    fileName: 'component-audit.xlsx',
    sizeBytes: 78_500,
    version: 2,
    tags: ['design-system'],
    owner: MEI,
    updatedAt: ago(10 * DAY),
    status: 'indexed',
    chunkCount: 12,
  },
  {
    id: 'd-26',
    folderId: 'f-des',
    title: 'Brand Palette',
    fileName: 'brand-palette.svg',
    sizeBytes: 34_200,
    version: 1,
    tags: ['design-system', 'brand'],
    owner: MEI,
    updatedAt: ago(30 * DAY),
    status: 'indexed',
    chunkCount: 1,
  },
];

/**
 * Same rough characters-per-token estimate the server's chunker uses, so the
 * mock's numbers look like the real ones rather than like round figures.
 */
const estimateTokens = (body: string) => Math.max(1, Math.ceil(body.length / 4));

const withTokens = (sections: Omit<DocumentSection, 'tokenCount'>[]): DocumentSection[] =>
  sections.map((section) => ({ ...section, tokenCount: estimateTokens(section.body) }));

/** Real content for the hero document so the preview and citations feel true. */
const HERO_SECTIONS: DocumentSection[] = withTokens([
  {
    chunkId: 1,
    heading: '1. Prerequisites',
    body: 'You need Docker Desktop, the .NET SDK and Node.js LTS installed before anything else. The team works on macOS day to day; the Windows box is only used for IIS deployment testing, so do not expect to run IIS locally.',
  },
  {
    chunkId: 2,
    heading: '2.1 Starting local infrastructure with Docker',
    body: 'Run `docker compose up -d` from the repository root. This starts PostgreSQL (with the pgvector extension pre-installed) and Azurite, the Azure Blob Storage emulator. Wait for both containers to report healthy before starting the API — the API fails fast if it cannot reach either.',
  },
  {
    chunkId: 3,
    heading: '2.2 Connection strings and configuration',
    body: 'The local connection string lives in appsettings.Development.json, which is safe to commit because it contains no real credentials. Blob storage uses UseDevelopmentStorage=true, which points the Azure SDK at Azurite. Real secrets — the LLM API key in particular — go into dotnet user-secrets and never into any appsettings file.',
  },
  {
    chunkId: 4,
    heading: '3. Applying database migrations',
    body: 'From server/src/DocHub.Api run `dotnet ef database update`. The initial migration creates the relational schema and enables the vector extension. If the extension step fails, confirm you are on the pgvector-enabled Postgres image rather than the stock one.',
  },
  {
    chunkId: 5,
    heading: '4. Running the API and client',
    body: 'Start the backend with `dotnet run` from server/src/DocHub.Api — this uses Kestrel, not IIS. In a second terminal, run `ng serve` from client. The Angular dev server proxies API calls, so no CORS configuration is needed for local development.',
  },
  {
    chunkId: 6,
    heading: '5. Verifying the ingestion pipeline',
    body: 'Upload any small Markdown file and watch the Hangfire dashboard at /hangfire. You should see an ingestion job move through extract, chunk and embed stages. The document status in the UI moves from Pending to Indexing to Indexed as the job progresses.',
  },
  {
    chunkId: 7,
    heading: '6. Common problems',
    body: 'If uploads succeed but documents never leave Pending, the Hangfire server is probably not running — check the API logs on startup. If embeddings fail with a 401, the LLM API key is missing from user-secrets.',
  },
]);

/** A readable window of `body` centred on the first matching term. */
function snippetAround(body: string, terms: string[]): string {
  const lower = body.toLowerCase();
  const at = terms.map((term) => lower.indexOf(term)).find((index) => index >= 0) ?? 0;

  const start = Math.max(0, at - 90);
  const end = Math.min(body.length, start + 300);

  return (
    (start > 0 ? '…' : '') + body.slice(start, end).trim() + (end < body.length ? '…' : '')
  );
}

function sectionsFor(seed: Seed): DocumentSection[] {
  if (seed.id === 'd-1') return HERO_SECTIONS;
  const count = Math.max(3, Math.min(7, Math.round((seed.chunkCount ?? 9) / 6)));
  return withTokens(
    Array.from({ length: count }, (_, i) => ({
      chunkId: i + 1,
      heading: `${i + 1}. ${['Overview', 'Scope', 'Approach', 'Details', 'Configuration', 'Operations', 'Appendix'][i] ?? 'Section'}`,
      body:
        `This section of “${seed.title}” covers ${['the purpose and audience', 'what is in and out of scope', 'the approach the team agreed on', 'the detailed steps involved', 'configuration values and defaults', 'day-two operational concerns', 'supporting reference material'][i] ?? 'additional material'}. ` +
        'Running against the real API replaces this text with the actual extracted content, chunked at roughly 800 tokens with 15% overlap so citations can point at an exact section rather than a whole file.',
    })),
  );
}

function versionsFor(seed: Seed): DocumentVersion[] {
  const notes = [
    'Initial upload',
    'Corrected the connection string example',
    'Added troubleshooting section',
    'Refreshed screenshots',
    'Reviewed for the new release',
    'Clarified prerequisites',
    'Post-incident amendments',
  ];
  return Array.from({ length: seed.version }, (_, i) => {
    const version = seed.version - i;
    return {
      version,
      changedBy: i === 0 ? seed.owner : PEOPLE[(version + 2) % PEOPLE.length],
      changedAt: ago(i === 0 ? Date.now() - new Date(seed.updatedAt).getTime() : (i * 9 + 3) * DAY),
      note: notes[(version - 1) % notes.length],
      sizeBytes: Math.round(seed.sizeBytes * (1 - i * 0.06)),
      current: i === 0,
    };
  });
}

interface Db {
  folders: Folder[];
  documents: DocumentSummary[];
  activity: ActivityEvent[];
}

function toSummary(seed: Seed): DocumentSummary {
  const extension = seed.fileName.split('.').pop() ?? '';
  return {
    ...seed,
    kind: kindFromFileName(seed.fileName),
    extension,
  };
}

@Injectable()
export class MockKnowledgeGateway extends KnowledgeGateway {
  private readonly db$ = new BehaviorSubject<Db>({
    folders: FOLDERS.map((f) => ({ ...f })),
    documents: SEEDS.map(toSummary),
    activity: [
      {
        id: 'a1',
        type: 'uploaded',
        actor: RAVI,
        target: 'Local Postgres & pgvector notes',
        targetId: 'd-2',
        at: ago(52 * MINUTE),
      },
      {
        id: 'a2',
        type: 'uploaded',
        actor: RAVI,
        target: 'Hangfire Job Failures — Triage',
        targetId: 'd-11',
        at: ago(38 * MINUTE),
      },
      {
        id: 'a3',
        type: 'indexed',
        actor: RAVI,
        target: 'GitHub Actions Pipeline',
        targetId: 'd-19',
        at: ago(3 * HOUR),
      },
      {
        id: 'a4',
        type: 'updated',
        actor: MEI,
        target: 'Design Tokens Reference',
        targetId: 'd-24',
        at: ago(4 * HOUR),
      },
      {
        id: 'a5',
        type: 'updated',
        actor: MEI,
        target: 'Knowledge Hub Technical Blueprint',
        targetId: 'd-5',
        at: ago(5 * HOUR),
      },
      {
        id: 'a6',
        type: 'indexed',
        actor: TOM,
        target: 'Incident Response Runbook',
        targetId: 'd-9',
        at: ago(20 * HOUR),
      },
      {
        id: 'a7',
        type: 'updated',
        actor: PRIYA,
        target: 'Knowledge Hub — Product Requirements',
        targetId: 'd-12',
        at: ago(1 * DAY),
      },
      {
        id: 'a8',
        type: 'failed',
        actor: ANA,
        target: 'Engineering Handbook (scanned)',
        targetId: 'd-3',
        at: ago(3 * DAY),
      },
      { id: 'a9', type: 'folder-created', actor: ANA, target: 'Design', at: ago(4 * DAY) },
    ],
  });

  /**
   * Simulates network latency so screens exercise their real loading states.
   * The first read of the session is slow (cold start); later reads are quick,
   * and live updates pushed from `db$` are instant.
   */
  private warm = false;

  private read<T>(project: (db: Db) => T): Observable<T> {
    let firstOfSubscription = true;
    return this.db$.pipe(
      map(project),
      delayWhen(() => {
        if (!firstOfSubscription) return timer(0);
        firstOfSubscription = false;
        const wait = timer(this.warm ? 140 : 420);
        this.warm = true;
        return wait;
      }),
    );
  }

  constructor() {
    super();
    this.simulateIngestion();
  }

  /**
   * Advances any document that is mid-ingestion, so the status vocabulary in
   * the UI is visibly live rather than a static badge. The real app gets these
   * transitions from the API (polling now, SignalR later).
   */
  private simulateIngestion(): void {
    setInterval(() => {
      const db = this.db$.value;
      let changed = false;
      const documents = db.documents.map((doc) => {
        if (doc.status === 'indexing') {
          const progress = Math.min(100, (doc.indexProgress ?? 0) + Math.random() * 7);
          changed = true;
          if (progress >= 100) {
            return {
              ...doc,
              status: 'indexed' as const,
              indexProgress: undefined,
              // Rough stand-in for real chunking: binary formats carry far less
              // extractable text per byte than plain text does.
              chunkCount: Math.max(
                1,
                Math.round(
                  doc.sizeBytes /
                    (doc.kind === 'markdown' || doc.kind === 'text' || doc.kind === 'code'
                      ? 1_800
                      : 45_000),
                ),
              ),
            };
          }
          return { ...doc, indexProgress: progress };
        }
        if (doc.status === 'pending' && Math.random() < 0.08) {
          changed = true;
          return { ...doc, status: 'indexing' as const, indexProgress: 2 };
        }
        return doc;
      });
      if (changed) this.db$.next({ ...db, documents });
    }, 1400);
  }

  private descendantIds(folders: Folder[], rootId: string): Set<string> {
    const ids = new Set<string>([rootId]);
    let grew = true;
    while (grew) {
      grew = false;
      for (const folder of folders) {
        if (folder.parentId && ids.has(folder.parentId) && !ids.has(folder.id)) {
          ids.add(folder.id);
          grew = true;
        }
      }
    }
    return ids;
  }

  folders(): Observable<Folder[]> {
    return this.read((db) =>
      db.folders.map((folder) => ({
        ...folder,
        documentCount: db.documents.filter((d) =>
          this.descendantIds(db.folders, folder.id).has(d.folderId),
        ).length,
      })),
    );
  }

  documents(query: DocumentQuery): Observable<DocumentSummary[]> {
    return this.read((db) => {
      const scope =
        query.folderId && query.recursive !== false
          ? this.descendantIds(db.folders, query.folderId)
          : query.folderId
            ? new Set([query.folderId])
            : null;

      const text = query.text?.trim().toLowerCase();
      let result = db.documents.filter((doc) => {
        if (scope && !scope.has(doc.folderId)) return false;
        if (query.starredOnly && !doc.starred) return false;
        if (query.statuses?.length && !query.statuses.includes(doc.status)) return false;
        if (query.kinds?.length && !query.kinds.includes(doc.kind)) return false;
        if (query.ownerId && doc.owner.id !== query.ownerId) return false;
        if (query.tags?.length && !query.tags.some((t) => doc.tags.includes(t))) return false;
        if (text) {
          const haystack =
            `${doc.title} ${doc.fileName} ${doc.tags.join(' ')} ${doc.description ?? ''}`.toLowerCase();
          if (!haystack.includes(text)) return false;
        }
        return true;
      });

      const sorters: Record<string, (a: DocumentSummary, b: DocumentSummary) => number> = {
        'updated-desc': (a, b) => +new Date(b.updatedAt) - +new Date(a.updatedAt),
        'updated-asc': (a, b) => +new Date(a.updatedAt) - +new Date(b.updatedAt),
        'name-asc': (a, b) => a.title.localeCompare(b.title),
        'name-desc': (a, b) => b.title.localeCompare(a.title),
        'size-desc': (a, b) => b.sizeBytes - a.sizeBytes,
      };
      result = [...result].sort(sorters[query.sort ?? 'updated-desc']);
      return result;
    });
  }

  document(id: string): Observable<DocumentDetail | undefined> {
    return this.read((db) => {
      const doc = db.documents.find((d) => d.id === id);
      if (!doc) return undefined;
      const seed = SEEDS.find((s) => s.id === id)!;

      const breadcrumb: Folder[] = [];
      let cursor = db.folders.find((f) => f.id === doc.folderId);
      while (cursor) {
        breadcrumb.unshift(cursor);
        cursor = db.folders.find((f) => f.id === cursor!.parentId);
      }

      return {
        ...doc,
        breadcrumb,
        versions: versionsFor(seed),
        sections: sectionsFor(seed),
        citedInAnswers: doc.status === 'indexed' ? (Number(doc.id.replace('d-', '')) * 3) % 17 : 0,
        createdAt: ago(60 * DAY),
      } satisfies DocumentDetail;
    });
  }

  stats(): Observable<LibraryStats> {
    return this.read((db) => ({
      documents: db.documents.length,
      indexed: db.documents.filter((d) => d.status === 'indexed').length,
      indexing: db.documents.filter((d) => d.status === 'indexing' || d.status === 'pending')
        .length,
      failed: db.documents.filter((d) => d.status === 'failed').length,
      folders: db.folders.length,
      storageBytes: db.documents.reduce((sum, d) => sum + d.sizeBytes, 0),
      chunks: db.documents.reduce((sum, d) => sum + (d.chunkCount ?? 0), 0),
    }));
  }

  activity(limit = 8): Observable<ActivityEvent[]> {
    return this.read((db) => db.activity.slice(0, limit));
  }

  people(): Observable<Person[]> {
    return of(PEOPLE);
  }

  allTags(): Observable<string[]> {
    return this.read((db) => [...new Set(db.documents.flatMap((d) => d.tags))].sort());
  }

  /**
   * Substring matching over the seeded section text.
   *
   * Deliberately not a fake of semantic search: pretending to understand a
   * question the mock cannot answer would make the screen look right while
   * hiding whether the real pipeline works. Every result here is reported as a
   * keyword match, which is exactly what it is.
   */
  search(query: SearchQuery): Observable<SearchResponse> {
    const text = query.text.trim();
    const terms = text
      .split(/[\s"',.?!():;]+/)
      .filter((term) => term.length > 1)
      .map((term) => term.toLowerCase());

    return this.read((db) => {
      const results: SearchResult[] = [];

      for (const document of db.documents) {
        if (document.status !== 'indexed') continue;
        if (query.folderId && document.folderId !== query.folderId) continue;
        if (query.kinds?.length && !query.kinds.includes(document.kind)) continue;
        if (query.ownerId && document.owner.id !== query.ownerId) continue;
        if (query.tags?.length && !query.tags.some((tag) => document.tags.includes(tag))) continue;

        const folder = db.folders.find((candidate) => candidate.id === document.folderId);

        for (const section of sectionsFor(SEEDS.find((s) => s.id === document.id) ?? SEEDS[0])) {
          const haystack = `${section.heading} ${section.body}`.toLowerCase();
          const hits = terms.filter((term) => haystack.includes(term)).length;
          if (!hits) continue;

          results.push({
            documentId: document.id,
            title: document.title,
            fileName: document.fileName,
            kind: document.kind,
            extension: document.extension,
            folderId: document.folderId,
            folderPath: folder?.path ?? '',
            chunkId: section.chunkId,
            heading: section.heading,
            snippet: snippetAround(section.body, terms),
            score: hits / terms.length,
            matchedBy: 'keyword',
          });
        }
      }

      results.sort((a, b) => b.score - a.score);
      const top = results.slice(0, 20);

      return {
        query: text,
        totalMatches: results.length,
        elapsedMs: 12,
        terms,
        results: top,
        diagnostics: {
          keywordMatches: results.length,
          vectorMatches: 0,
          embeddingProvider: 'mock (no embeddings)',
          vectorSearchAvailable: false,
          vectorSearchError:
            'This is the mock gateway — semantic matching needs the real API and an embedding provider.',
        },
      } satisfies SearchResponse;
    });
  }

  createFolder(parentId: string | null, name: string): Observable<Folder> {
    const db = this.db$.value;
    const parent = db.folders.find((f) => f.id === parentId);
    const folder: Folder = {
      id: `f-${Math.random().toString(36).slice(2, 8)}`,
      parentId,
      name,
      path: parent ? `${parent.path}/${name}` : name,
      documentCount: 0,
    };
    this.db$.next({
      ...db,
      folders: [...db.folders, folder],
      activity: [
        {
          id: `a-${Date.now()}`,
          type: 'folder-created',
          actor: ANA,
          target: name,
          at: new Date().toISOString(),
        },
        ...db.activity,
      ],
    });
    return of(folder);
  }

  renameFolder(id: string, name: string): Observable<void> {
    const db = this.db$.value;
    this.db$.next({
      ...db,
      folders: db.folders.map((f) =>
        f.id === id ? { ...f, name, path: f.path.replace(/[^/]+$/, name) } : f,
      ),
    });
    return of(void 0);
  }

  deleteFolder(id: string): Observable<void> {
    const db = this.db$.value;
    const doomed = this.descendantIds(db.folders, id);
    this.db$.next({
      ...db,
      folders: db.folders.filter((f) => !doomed.has(f.id)),
      documents: db.documents.filter((d) => !doomed.has(d.folderId)),
    });
    return of(void 0);
  }

  uploadFiles(folderId: string, files: File[]): Observable<void> {
    const db = this.db$.value;
    const created = files.map<DocumentSummary>((file, i) => ({
      id: `d-${Date.now()}-${i}`,
      folderId,
      title: file.name.replace(/\.[^.]+$/, ''),
      fileName: file.name,
      kind: kindFromFileName(file.name),
      extension: file.name.split('.').pop() ?? '',
      sizeBytes: file.size,
      version: 1,
      tags: [],
      owner: ANA,
      updatedAt: new Date().toISOString(),
      status: 'pending',
    }));
    this.db$.next({
      ...db,
      documents: [...created, ...db.documents],
      activity: [
        ...created.map((doc) => ({
          id: `a-${doc.id}`,
          type: 'uploaded' as const,
          actor: ANA,
          target: doc.title,
          targetId: doc.id,
          at: doc.updatedAt,
        })),
        ...db.activity,
      ],
    });
    return of(void 0);
  }

  retryIngestion(documentId: string): Observable<void> {
    const db = this.db$.value;
    this.db$.next({
      ...db,
      documents: db.documents.map((d) =>
        d.id === documentId
          ? { ...d, status: 'indexing', indexProgress: 1, failureReason: undefined }
          : d,
      ),
    });
    return of(void 0);
  }

  toggleStar(documentId: string): Observable<void> {
    const db = this.db$.value;
    this.db$.next({
      ...db,
      documents: db.documents.map((d) => (d.id === documentId ? { ...d, starred: !d.starred } : d)),
    });
    return of(void 0);
  }

  moveDocument(documentId: string, folderId: string): Observable<void> {
    const db = this.db$.value;
    this.db$.next({
      ...db,
      documents: db.documents.map((d) => (d.id === documentId ? { ...d, folderId } : d)),
    });
    return of(void 0);
  }

  deleteDocument(documentId: string): Observable<void> {
    const db = this.db$.value;
    this.db$.next({ ...db, documents: db.documents.filter((d) => d.id !== documentId) });
    return of(void 0);
  }
}

/** Convenience for one-shot reads in resolvers/tests. */
export const firstValue = <T>(source: Observable<T>) => source.pipe(take(1));

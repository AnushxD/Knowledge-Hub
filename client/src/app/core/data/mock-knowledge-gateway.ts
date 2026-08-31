import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, from, map, of, throwError, timer } from 'rxjs';
import { concatMap, delay, delayWhen, take } from 'rxjs/operators';
import {
  Account,
  ActivityEvent,
  AskRequest,
  AuthOptions,
  NewAccount,
  ChatEvent,
  ChatSession,
  ChatTranscript,
  Citation,
  DocumentDetail,
  DocumentQuery,
  DocumentSection,
  DocumentSummary,
  Folder,
  IngestionStatus,
  KnowledgeSource,
  KnowledgeSourceSummary,
  LibraryStats,
  Person,
  Repository,
  RepositoryConnection,
  RepositoryProbe,
  RepositorySettings,
  RepositorySettingsDraft,
  RepositorySource,
  RepositorySourceDraft,
  SearchQuery,
  SearchResponse,
  SearchResult,
  SignedInUser,
  UserRole,
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
  tags: string[];
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
    tags: ['setup', 'postgres', 'docker'],
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
    tags: ['postgres', 'pgvector'],
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
    tags: ['handbook', 'policy'],
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
    tags: ['onboarding'],
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
    tags: ['architecture', 'rag', 'mcp'],
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
    tags: ['architecture', 'conventions'],
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
    tags: ['architecture', 'database'],
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
    tags: ['architecture', 'ingestion'],
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
    tags: ['runbook', 'oncall'],
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
    tags: ['runbook', 'postgres'],
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
    tags: ['runbook', 'hangfire'],
    updatedAt: ago(38 * MINUTE),
    status: 'pending',
  },
  {
    id: 'd-12',
    folderId: 'f-spec',
    title: 'Knowledge Hub — Product Requirements',
    fileName: 'khub-prd.docx',
    sizeBytes: 320_500,
    tags: ['prd', 'spec'],
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
    tags: ['spec', 'search'],
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
    tags: ['spec', 'ux', 'citations'],
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
    tags: ['research'],
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
    tags: ['research'],
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
    tags: ['research', 'embeddings'],
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
    tags: ['deployment', 'iis'],
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
    tags: ['ci', 'deployment'],
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
    tags: ['azure', 'deployment'],
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
    tags: ['security', 'policy'],
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
    tags: ['security', 'auth'],
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
    tags: ['security', 'audit'],
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
    tags: ['design-system'],
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
    tags: ['design-system'],
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
    tags: ['design-system', 'brand'],
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

  return (start > 0 ? '…' : '') + body.slice(start, end).trim() + (end < body.length ? '…' : '');
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

interface Db {
  folders: Folder[];
  documents: DocumentSummary[];
  activity: ActivityEvent[];
}

/**
 * The project this mock stands in for. Every seeded file is a path inside it,
 * because that is what a document is now — the folder tree is its directories.
 */
const PROJECT = 'platform/handbook';
const BRANCH = 'main';

function toSummary(seed: Seed): DocumentSummary {
  const extension = seed.fileName.split('.').pop() ?? '';
  const folder = FOLDERS.find((candidate) => candidate.id === seed.folderId);
  const repositoryPath = folder ? `${folder.path}/${seed.fileName}` : seed.fileName;

  return {
    ...seed,
    kind: kindFromFileName(seed.fileName),
    extension,
    repositoryPath,
    webUrl: `https://gitlab.example.org/${PROJECT}/-/blob/${BRANCH}/${repositoryPath}`,
    commitSha: '9f2c1ab5d3e47f8091a2b6c4d5e6f708192a3b4c',
    lastSyncedAt: ago(9 * MINUTE),
  };
}

@Injectable()
export class MockKnowledgeGateway extends KnowledgeGateway {
  private readonly db$ = new BehaviorSubject<Db>({
    folders: FOLDERS.map((f) => ({ ...f })),
    documents: SEEDS.map(toSummary),
    activity: [
      // Mostly actorless: the repository changed, and no one in the hub did
      // it. The two entries with a person are the two things a person can
      // still do — edit hub-local metadata, and ask for a sync.
      {
        id: 'a1',
        type: 'synced',
        actor: ANA,
        target: `${PROJECT}@${BRANCH}`,
        at: ago(9 * MINUTE),
      },
      {
        id: 'a2',
        type: 'changed',
        actor: null,
        target: 'Hangfire Job Failures — Triage',
        targetId: 'd-11',
        at: ago(38 * MINUTE),
      },
      {
        id: 'a3',
        type: 'indexed',
        actor: null,
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
        type: 'added',
        actor: null,
        target: 'Knowledge Hub Technical Blueprint',
        targetId: 'd-5',
        at: ago(5 * HOUR),
      },
      {
        id: 'a6',
        type: 'indexed',
        actor: null,
        target: 'Incident Response Runbook',
        targetId: 'd-9',
        at: ago(20 * HOUR),
      },
      {
        id: 'a7',
        type: 'removed',
        actor: null,
        target: 'Retired VPN client notes',
        at: ago(1 * DAY),
      },
      {
        id: 'a8',
        type: 'failed',
        actor: null,
        target: 'Engineering Handbook (scanned)',
        targetId: 'd-3',
        at: ago(3 * DAY),
      },
      { id: 'a9', type: 'synced', actor: null, target: `${PROJECT}@${BRANCH}`, at: ago(4 * DAY) },
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
        sections: sectionsFor(seed),
        citedInAnswers: doc.status === 'indexed' ? (Number(doc.id.replace('d-', '')) * 3) % 17 : 0,
        createdAt: ago(60 * DAY),
      } satisfies DocumentDetail;
    });
  }

  /**
   * The seeded sections stitched back into one Markdown document, so the
   * rendered preview has something structural to show.
   */
  documentText(id: string): Observable<string> {
    return this.read((db) => {
      const doc = db.documents.find((d) => d.id === id);
      if (!doc) return '';

      const seed = SEEDS.find((s) => s.id === id)!;

      return sectionsFor(seed)
        .map((section) => `## ${section.heading}\n\n${section.body}`)
        .join('\n\n');
    });
  }

  /**
   * Null on purpose: there is no stored file behind a seeded document, and the
   * preview says so rather than pointing a frame at nothing.
   */
  documentContentUrl(): string | null {
    return null;
  }

  /** Null for the same reason — there is no file here to save. */
  documentDownloadUrl(): string | null {
    return null;
  }

  stats(): Observable<LibraryStats> {
    return this.read((db) => ({
      documents: db.documents.length,
      indexed: db.documents.filter((d) => d.status === 'indexed').length,
      indexing: db.documents.filter((d) => d.status === 'indexing' || d.status === 'pending')
        .length,
      failed: db.documents.filter((d) => d.status === 'failed').length,
      folders: db.folders.length,
      contentBytes: db.documents.reduce((sum, d) => sum + d.sizeBytes, 0),
      chunks: db.documents.reduce((sum, d) => sum + (d.chunkCount ?? 0), 0),
    }));
  }

  activity(limit = 8): Observable<ActivityEvent[]> {
    return this.read((db) => db.activity.slice(0, limit));
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

  /**
   * Replays a canned answer over the same event shape the real gateway emits.
   *
   * Deliberately not a fake model: it retrieves real seeded passages and then
   * says it cannot answer from them. Inventing a plausible answer here would
   * make the screen look finished while hiding whether grounding actually
   * works — the one property this feature exists to guarantee.
   */
  ask(request: AskRequest): Observable<ChatEvent> {
    const sources: Citation[] = HERO_SECTIONS.slice(0, 3).map((section, index) => ({
      marker: index + 1,
      kind: 'document',
      title: 'Dev Environment Setup',
      heading: section.heading,
      documentId: 'd-1',
      chunkId: section.chunkId,
      sourceName: 'documents',
    }));

    const answer =
      'This is the mock gateway, so no model is running — nothing here is a real answer. ' +
      'The sources above are genuinely retrieved from the seeded content, and the real ' +
      'assistant would cite them like [1]. Point the app at the API to ask for real.';

    const events: ChatEvent[] = [
      { type: 'session', sessionId: request.sessionId ?? 'mock-session', title: request.question },
      { type: 'sources', sources },
      ...answer.split(' ').map((word) => ({ type: 'token' as const, text: `${word} ` })),
      {
        type: 'done',
        messageId: 'mock-message',
        content: answer,
        citations: [sources[0]],
        isRefusal: false,
      },
    ];

    // Paced so the streaming UI is exercised rather than filled in one frame.
    return from(events).pipe(concatMap((event) => of(event).pipe(delay(28))));
  }

  // ---- authentication ------------------------------------------------------
  // Always signed in as an admin. The mock exists to develop screens without a
  // backend, and a login wall in front of that would defeat the purpose — the
  // real gateway is what exercises the auth path.

  private readonly mockUser: SignedInUser = {
    id: 'u1',
    name: 'Ana Ruiz',
    email: 'ana@documenthub.local',
    initials: 'AR',
    role: 'Admin',
    hasPassword: true,
  };

  private readonly mockAccounts: Account[] = PEOPLE.map((person, index) => ({
    id: person.id,
    name: person.name,
    email: `${person.name.split(' ')[0].toLowerCase()}@documenthub.local`,
    role: (index === 0 ? 'Admin' : index < 3 ? 'Editor' : 'Viewer') as UserRole,
    hasPassword: index !== 4,
    isLockedOut: false,
    createdAt: ago(30 * DAY),
  }));

  currentUser(): Observable<SignedInUser | null> {
    return of(this.mockUser).pipe(delay(80));
  }

  authOptions(): Observable<AuthOptions> {
    return of({ googleEnabled: true }).pipe(delay(40));
  }

  signIn(): Observable<SignedInUser> {
    return of(this.mockUser).pipe(delay(200));
  }

  signOut(): Observable<void> {
    return of(void 0).pipe(delay(80));
  }

  /**
   * Accepts one password, so both outcomes are reachable without a backend.
   * The failure path is the one worth developing against — it is what a
   * mistyped current password looks like.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    if (currentPassword !== 'mock-password') {
      return throwError(() => new Error('The current password is incorrect.'));
    }

    if (newPassword.length < 7) {
      return throwError(() => new Error('Passwords must be at least 7 characters.'));
    }

    return of(void 0).pipe(delay(400));
  }

  accounts(): Observable<Account[]> {
    return of([...this.mockAccounts]).pipe(delay(120));
  }

  createAccount(input: NewAccount): Observable<Account> {
    const account: Account = {
      id: `u-${this.mockAccounts.length + 1}`,
      name: input.name,
      email: input.email,
      role: input.role,
      hasPassword: !!input.password,
      isLockedOut: false,
      createdAt: new Date().toISOString(),
    };

    this.mockAccounts.push(account);
    return of(account).pipe(delay(150));
  }

  changeAccountRole(id: string, role: UserRole): Observable<Account> {
    const account = this.mockAccounts.find((candidate) => candidate.id === id)!;
    account.role = role;
    return of(account).pipe(delay(120));
  }

  setAccountEnabled(id: string, enabled: boolean): Observable<Account> {
    const account = this.mockAccounts.find((candidate) => candidate.id === id)!;
    account.isLockedOut = !enabled;
    return of(account).pipe(delay(120));
  }

  // Two, so the screen is developed against the shape it has in production —
  // one of them overridden and one on configuration, which are drawn
  // differently.
  // One to start with, so both the populated list and the add-a-second flow
  // are reachable without a backend.
  private mockRepositorySources: RepositorySource[] = [
    {
      name: 'code-search',
      displayName: 'Code search',
      endpoint: 'http://mcp-cs.internal:8080',
      toolName: 'search_codebase',
      isEnabled: true,
      updatedAt: ago(3 * DAY),
    },
  ];

  repositorySources(): Observable<RepositorySource[]> {
    return of(this.mockRepositorySources.map((source) => ({ ...source }))).pipe(delay(80));
  }

  addRepositorySource(name: string, draft: RepositorySourceDraft): Observable<RepositorySource> {
    // Same rules as the API, so the error paths are reachable here too: the
    // name goes in a URL and has to be unique.
    if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(name)) {
      return throwError(
        () =>
          new Error(
            'The name may use lower-case letters, digits and hyphens only — for example ' +
              "'code-search'.",
          ),
      );
    }

    if (this.mockRepositorySources.some((source) => source.name === name)) {
      return throwError(() => new Error(`A repository server named '${name}' already exists.`));
    }

    const created: RepositorySource = { name, ...draft, updatedAt: new Date().toISOString() };
    this.mockRepositorySources = [...this.mockRepositorySources, created];

    return of({ ...created }).pipe(delay(150));
  }

  saveRepositorySource(name: string, draft: RepositorySourceDraft): Observable<RepositorySource> {
    const existing = this.mockRepositorySources.find((source) => source.name === name);

    // Matches the API, which 404s a name nobody added.
    if (!existing) return throwError(() => new Error(`No repository server named '${name}'.`));

    const updated: RepositorySource = {
      ...existing,
      ...draft,
      updatedAt: new Date().toISOString(),
    };

    this.mockRepositorySources = this.mockRepositorySources.map((source) =>
      source.name === name ? updated : source,
    );

    return of({ ...updated }).pipe(delay(150));
  }

  removeRepositorySource(name: string): Observable<void> {
    this.mockRepositorySources = this.mockRepositorySources.filter(
      (source) => source.name !== name,
    );

    return of(undefined).pipe(delay(120));
  }

  testRepositorySource(endpoint: string): Observable<RepositoryProbe> {
    // Three outcomes, matching the API: a server that speaks MCP, one that
    // answers HTTP and does not, and nothing at all. All three are drawn
    // differently, so all three have to be reachable without a backend.
    if (!/^https?:\/\//.test(endpoint)) {
      return of({
        isReachable: false,
        speaksMcp: false,
        detail: 'Could not connect (mock gateway).',
        tools: [],
        searchedTools: [],
        suggestedToolName: null,
        repositories: [],
      }).pipe(delay(400));
    }

    if (endpoint.includes('not-mcp')) {
      return of({
        isReachable: true,
        speaksMcp: false,
        detail:
          'Something answered (200 OK), but the MCP handshake failed. Check this is the MCP endpoint rather than the service’s home page.',
        tools: [],
        searchedTools: [],
        suggestedToolName: null,
        repositories: [],
      }).pipe(delay(400));
    }

    const tools = ['search_codebase', 'get_answer', 'get_architecture', 'get_symbol', 'list_repos'];

    return of({
      isReachable: true,
      speaksMcp: true,
      detail:
        'Connected. Searching would use 4 of its 5 tools: "search_codebase", "get_answer", "get_architecture", "get_symbol". Indexes 3 repositories.',
      tools,
      // Every tool but list_repos, which takes no search text: the mock has to
      // show the gap between "exposes" and "is asked", because that gap is the
      // whole reason the screen distinguishes them.
      searchedTools: ['search_codebase', 'get_answer', 'get_architecture', 'get_symbol'],
      suggestedToolName: 'search_codebase',
      repositories: ['hub', 'worker', 'docs'],
    }).pipe(delay(400));
  }

  knowledgeSources(): Observable<KnowledgeSourceSummary[]> {
    // Instant, as the API is: this call contacts nothing.
    return of<KnowledgeSourceSummary[]>(
      MockKnowledgeGateway.Sources.map(({ name, displayName, description }) => ({
        name,
        displayName,
        description,
      })),
    ).pipe(delay(40));
  }

  knowledgeSourceStatuses(): Observable<KnowledgeSource[]> {
    // Slow on purpose — a real deployment pays an MCP handshake per remote
    // server here, and the screen has to stay usable while it does.
    return of<KnowledgeSource[]>(MockKnowledgeGateway.Sources).pipe(delay(1800));
  }

  /**
   * Mirrors what a default local deployment really reports, including the
   * inactive repository source. A mock that showed everything green would make
   * the screen look finished while hiding the state it exists to show.
   */
  private static readonly Sources: KnowledgeSource[] = [
    {
      name: 'documents',
      displayName: 'Documents',
      description: 'Everything uploaded to the hub, searched by keyword and by meaning together.',
      state: 'active',
      detail:
        'Searched on every question. Only documents that finished ingestion are retrievable — anything still processing or failed is neither searchable nor citable.',
    },
    {
      name: 'repositories',
      displayName: 'Repositories',
      description: "Source code and READMEs from the team's repositories, reached over MCP.",
      state: 'inactive',
      detail:
        'No repository servers have been added, so answers are grounded in documents only. An administrator can add one on this screen.',
    },
  ];

  chatSessions(): Observable<ChatSession[]> {
    return this.read(() => []);
  }

  chatTranscript(sessionId: string): Observable<ChatTranscript> {
    return this.read(() => ({
      session: {
        id: sessionId,
        title: 'Mock conversation',
        messageCount: 0,
        createdAt: ago(0),
        updatedAt: ago(0),
      },
      messages: [],
    }));
  }

  deleteChatSession(): Observable<void> {
    return of(void 0);
  }

  /**
   * When the pretend sync finishes. Held as state rather than returned once,
   * because the store discards what `syncRepository` resolves to and re-reads
   * this — so a "running" outcome that lived only in that response was never
   * seen by any screen, and the button and the sync summary both sat on
   * "succeeded" throughout.
   */
  private syncingUntil = 0;

  repository(): Observable<Repository> {
    return this.read((db) => ({
      projectPath: PROJECT,
      branch: BRANCH,
      subPath: 'docs',
      webUrl: `https://gitlab.example.org/${PROJECT}`,
      outcome: (Date.now() < this.syncingUntil ? 'running' : 'succeeded') as
        'running' | 'succeeded',
      commitSha: '9f2c1ab5d3e47f8091a2b6c4d5e6f708192a3b4c',
      startedAt: ago(9 * MINUTE),
      finishedAt: ago(9 * MINUTE),
      error: null,
      added: 0,
      updated: 2,
      removed: 1,
      // A repository is mostly code, and the screen says so rather than
      // implying the whole tree is searchable.
      skipped: Math.max(0, 148 - db.documents.length),
      // Non-zero on purpose: a requeue is the state worth being able to see
      // without a backend, since it is the one that says "the library was
      // short of the repository and this run went and fetched the rest".
      requeued: 3,
      isConfigured: true,
    }));
  }

  /**
   * The repository settings, held in memory so the admin form can be worked on
   * without a backend. Secrets are booleans here exactly as they are over the
   * wire — a mock that handed a token back would let a screen be built around
   * one the real API will never send.
   */
  private settings: RepositorySettings = {
    baseUrl: 'https://gitlab.example.org',
    projectPath: PROJECT,
    branch: BRANCH,
    subPath: 'docs',
    hasToken: true,
    hasWebhookSecret: true,
    tokenIsUnreadable: false,
    webhookSecretIsUnreadable: false,
    isConfigured: true,
    isSaved: true,
    updatedAt: ago(3 * DAY),
  };

  repositorySettings(): Observable<RepositorySettings> {
    return of(this.settings).pipe(delay(120));
  }

  saveRepositorySettings(draft: RepositorySettingsDraft): Observable<RepositorySettings> {
    this.settings = {
      ...this.settings,
      baseUrl: draft.baseUrl.trim().replace(/\/$/, ''),
      projectPath: draft.projectPath.trim().replace(/^\/|\/$/g, ''),
      branch: draft.branch.trim(),
      subPath: draft.subPath.trim().replace(/^\/|\/$/g, ''),

      // The three states the API keeps: undefined leaves the stored secret
      // alone, empty clears it, anything else replaces it.
      hasToken: draft.token === undefined ? this.settings.hasToken : draft.token.length > 0,
      hasWebhookSecret:
        draft.webhookSecret === undefined
          ? this.settings.hasWebhookSecret
          : draft.webhookSecret.length > 0,
      isConfigured: true,
      isSaved: true,
      updatedAt: new Date().toISOString(),
    };

    return of(this.settings).pipe(delay(200));
  }

  testRepositorySettings(draft: RepositorySettingsDraft): Observable<RepositoryConnection> {
    // Two outcomes worth being able to reach without a backend: it reads, and
    // the sub-path names nothing — the mistake that mirrors an empty library.
    const subPathFound = !draft.subPath.trim().startsWith('nowhere');

    return of<RepositoryConnection>({
      isReachable: true,
      projectFound: true,
      branchFound: true,
      subPathFound,
      usedToken: draft.token !== '' && (draft.token !== undefined || this.settings.hasToken),
      detail: subPathFound
        ? `Read '${draft.projectPath}' on branch '${draft.branch}' (mock gateway).`
        : `'${draft.subPath}' holds no files on that branch. The hub would mirror nothing.`,
      projectName: draft.projectPath,
      defaultBranch: BRANCH,
      webUrl: `https://gitlab.example.org/${draft.projectPath}`,
    }).pipe(delay(400));
  }

  /**
   * Pretends to mirror. The seeded tree is the repository here, so there is
   * nothing to fetch — but the state has to move through "running", because
   * that is the state the real screen spends its time polling.
   */
  syncRepository(): Observable<Repository> {
    // Comfortably longer than the store's 2.5s poll, so at least one re-read
    // lands inside the window. Equal to it raced, and the screen jumped
    // straight back to "succeeded" as though the button had done nothing.
    this.syncingUntil = Date.now() + 7000;
    return this.repository();
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
}

/** Convenience for one-shot reads in resolvers/tests. */
export const firstValue = <T>(source: Observable<T>) => source.pipe(take(1));

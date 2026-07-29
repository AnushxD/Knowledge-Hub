import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Generates src/styles/icons.css — one CSS mask per icon class.
 * Source glyphs: Lucide (ISC licensed), via the lucide-static package.
 */

const ICON_DIR = resolve('node_modules/lucide-static/icons');
const OUT = resolve('src/styles/icons.css');

// class name (kept from the previous icon set, so markup is untouched) -> lucide icon
const MAP = {
  'align-left': 'align-left',
  'angle-double-left': 'chevrons-left',
  'angle-right': 'chevron-right',
  ban: 'ban',
  bars: 'menu',
  bell: 'bell',
  box: 'package',
  'check-circle': 'circle-check',
  'chevron-right': 'chevron-right',
  'cloud-upload': 'cloud-upload',
  code: 'file-code',
  cog: 'settings',
  comments: 'message-square',
  compass: 'compass',
  database: 'database',
  desktop: 'presentation',
  download: 'download',
  'ellipsis-h': 'ellipsis',
  'exclamation-triangle': 'triangle-alert',
  eye: 'eye',
  'eye-slash': 'eye-off',
  file: 'file',
  'file-edit': 'file-pen',
  'file-excel': 'sheet',
  'file-pdf': 'file-text',
  'file-word': 'file-type',
  filter: 'filter',
  'filter-slash': 'filter-x',
  folder: 'folder',
  'folder-open': 'folder-open',
  'folder-plus': 'folder-plus',
  // Lucide carries no brand marks, so repositories get a generic VCS glyph.
  github: 'git-branch',
  heart: 'activity',
  home: 'house',
  image: 'image',
  inbox: 'inbox',
  'info-circle': 'info',
  link: 'link',
  list: 'list',
  lock: 'lock',
  map: 'map',
  moon: 'moon',
  plus: 'plus',
  refresh: 'refresh-cw',
  search: 'search',
  send: 'send',
  'share-alt': 'share-2',
  sitemap: 'workflow',
  'sliders-h': 'sliders-horizontal',
  'sort-alt': 'arrow-up-down',
  sparkles: 'sparkles',
  star: 'star',
  'star-fill': 'star',
  sun: 'sun',
  sync: 'refresh-cw',
  'th-large': 'layout-grid',
  times: 'x',
  'times-circle': 'circle-x',
  'toggle-on': 'toggle-right',
  trash: 'trash-2',
  upload: 'upload',
  users: 'users',
};

/** Classes that should render as a solid glyph rather than a stroke outline. */
const FILLED = new Set(['star-fill']);

const encode = (svg) =>
  encodeURIComponent(svg.replace(/\s+/g, ' ').trim()).replace(/'/g, '%27').replace(/"/g, '%22');

const missing = [];
const rules = [];

for (const [name, icon] of Object.entries(MAP)) {
  let svg;
  try {
    svg = readFileSync(resolve(ICON_DIR, `${icon}.svg`), 'utf8');
  } catch {
    missing.push(`${name} -> ${icon}`);
    continue;
  }
  if (FILLED.has(name)) svg = svg.replace('<svg', '<svg fill="currentColor"');
  rules.push(`.pi-${name} { --pi: url("data:image/svg+xml,${encode(svg)}"); }`);
}

const css = `/* ============================================================================
   GENERATED FILE — do not edit by hand.
   Regenerate with: node tools/gen-icons.mjs   (from the client/ directory)

   Icons are Lucide (https://lucide.dev), ISC licensed, inlined as CSS masks.
   Rendering them as masks rather than an icon font means:
     - the glyph inherits currentColor, so it themes automatically
     - size comes from font-size (1em), so text-[13px] still controls it
     - no webfont request, no FOUT, and nothing to license
========================================================================= */

.pi {
  display: inline-block;
  width: 1em;
  height: 1em;
  flex-shrink: 0;
  vertical-align: -0.135em;
  background-color: currentColor;
  -webkit-mask: var(--pi) no-repeat center / contain;
  mask: var(--pi) no-repeat center / contain;
}

${rules.join('\n')}
`;

mkdirSync(resolve('src/styles'), { recursive: true });
writeFileSync(OUT, css);
console.log(`Wrote ${rules.length} icons to ${OUT}`);
if (missing.length) console.warn('MISSING:', missing.join(', '));

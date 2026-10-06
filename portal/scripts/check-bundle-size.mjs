// Gzip budgets of 07 section 2: initial JS <= 350 KB (warning) / 450 KB (error); any lazy chunk <= 250 KB,
// except the ECharts chunk (<= 350 KB). Run after `ng build`.
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';

const dist = 'dist/portal/browser';
const kb = (bytes) => Math.round(bytes / 1024);
const gz = (file) => gzipSync(readFileSync(join(dist, file)), { level: 9 }).length;

const index = readFileSync(join(dist, 'index.html'), 'utf8');
const initial = new Set([...index.matchAll(/(?:src|href)="([^"]+\.js)"/g)].map((m) => m[1]));
const js = readdirSync(dist).filter((f) => f.endsWith('.js'));

let failed = false;
const initialSize = [...initial].reduce((sum, file) => sum + gz(file), 0);
console.log(`Initial JS (gzip): ${kb(initialSize)} KB`);
if (initialSize > 450 * 1024) {
  console.error('ERROR: initial bundle exceeds 450 KB gzip.');
  failed = true;
} else if (initialSize > 350 * 1024) {
  console.warn('WARNING: initial bundle exceeds 350 KB gzip.');
}

for (const file of js.filter((f) => !initial.has(f))) {
  const size = gz(file);
  const content = readFileSync(join(dist, file), 'utf8');
  const limit = content.includes('echarts') ? 350 : 250;
  if (size > limit * 1024) {
    console.error(`ERROR: lazy chunk ${file} is ${kb(size)} KB gzip (limit ${limit} KB).`);
    failed = true;
  }
}

process.exit(failed ? 1 : 0);

// Fails when a stylesheet uses physical left/right properties. Only logical properties are allowed so RTL works (D18).
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

const forbidden = [
  /\b(margin|padding|border)-(left|right)\b/,
  /(^|[\s;{])(left|right)\s*:/,
  /text-align\s*:\s*(left|right)\b/,
  /float\s*:\s*(left|right)\b/,
  /border-(top|bottom)-(left|right)-radius/,
];

function* files(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      yield* files(path);
    } else if (/\.(s?css|html)$/.test(name)) {
      yield path;
    }
  }
}

const problems = [];
for (const file of files('src')) {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);
  lines.forEach((line, i) => {
    if (forbidden.some((rule) => rule.test(line))) {
      problems.push(`${file}:${i + 1}: ${line.trim()}`);
    }
  });
}

if (problems.length > 0) {
  console.error('Physical CSS properties found (use logical properties):\n' + problems.join('\n'));
  process.exit(1);
}
console.log('Logical CSS check passed.');

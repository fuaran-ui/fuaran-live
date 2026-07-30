// The brand duplication lock: the injected Theme (app/shared/Brand.fs) and the
// shared brand stylesheet (app/brand/fuaran-brand.css) both emit the same
// --fuaran-* variables, and whichever mounts later wins in the cascade — so
// they may NEVER disagree. This test parses the stylesheet's light and dark
// blocks and asserts every variable the compiled Theme emits carries the
// identical value in the matching CSS block.
//
// Requires `pnpm run fable:app` to have produced app/output/ (same as
// closedLoop.test.ts).

import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it, expect } from 'vitest';

import { lightTheme, darkTheme } from '../app/output/shared/Brand.js';
import { toCssVariables } from '../app/output/fuaran-dotnet/src/Fuaran.UI.Renderer.Core/Theme.js';

const here = dirname(fileURLToPath(import.meta.url));
const css = readFileSync(join(here, '../app/brand/fuaran-brand.css'), 'utf8');

/** Extract `--name: value;` pairs from a CSS slice. */
const declsOf = (slice: string): Map<string, string> => {
  const out = new Map<string, string>();
  for (const m of slice.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+);/g)) {
    out.set(m[1], m[2].trim().replace(/\s+/g, ' '));
  }
  return out;
};

const darkStart = css.indexOf(":root[data-theme='dark']");
expect(darkStart).toBeGreaterThan(0);
// The section banner (box-drawing rule), NOT the file-header layer list —
// which also contains the words "5. Brand components".
const componentsStart = css.indexOf('─── 5. Brand components');
expect(componentsStart).toBeGreaterThan(darkStart);
const lightDecls = declsOf(css.slice(0, darkStart));
const darkDecls = declsOf(css.slice(darkStart, componentsStart));

/** The variable families the stylesheet owns and the Theme also emits. */
const shared = (name: string): boolean =>
  /^--fuaran-tone-[a-z]+-(bg|fg|border)$/.test(name) ||
  /^--fuaran-tone-[a-z]+-(hover|focus|active|disabled)-(bg|fg|border)$/.test(name) ||
  /^--fuaran-focus-ring-/.test(name);

describe('Brand.fs themes agree with fuaran-brand.css', () => {
  for (const [label, theme, decls] of [
    ['light', lightTheme, lightDecls],
    ['dark', darkTheme, darkDecls],
  ] as const) {
    it(`${label} theme matches the ${label} CSS block`, () => {
      const pairs: Iterable<[string, string]> = toCssVariables(theme);
      const mismatches: string[] = [];
      let compared = 0;
      for (const [name, value] of pairs) {
        if (!shared(name)) continue;
        const cssValue = decls.get(name);
        if (cssValue === undefined) {
          mismatches.push(`${name}: emitted by Theme but absent from the CSS block`);
        } else if (cssValue.toLowerCase() !== value.trim().toLowerCase()) {
          mismatches.push(`${name}: Theme=${value.trim()} CSS=${cssValue}`);
        }
        compared++;
      }
      expect(mismatches).toEqual([]);
      // 21 idle + 84 interaction + 4 focus-ring = 109 shared variables.
      expect(compared).toBe(109);
    });
  }
});

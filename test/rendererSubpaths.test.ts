// =============================================================================
//  The renderer's entry points, held by what is IMPORTED (Phase 2200).
//
//  `@fuaran-ui/renderer` 0.27.0 moved the math enhancer, the debug surface and the
//  relay out of its root entry into `./enhance-math`, `./debug` and `./relay`
//  subpaths, so a host that wants only the renderer does not carry them. Adopting
//  that is a property of every import in this repo, and it has two halves the
//  compiler does not both see:
//   - a TypeScript root import of a moved name stops compiling (`pnpm typecheck`);
//   - a Fable `[<Import("name", "@fuaran-ui/renderer")>]` is a STRING, so a moved
//     name there compiles and only fails once the bundle is built or run.
//
//  This reads both forms from source and holds the same rule for each: every name
//  imported from the package root, or from one of its subpaths, is really exported
//  by that entry of the installed package. It needs no build step.
// =============================================================================

import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const PACKAGE = '@fuaran-ui/renderer';

interface RendererImport {
  /** The entry point: the package root, or a subpath such as `enhance-math`. */
  readonly entry: string;
  readonly name: string;
}

const escaped = PACKAGE.replace(/[/@]/g, '\\$&');
// `import { a, type B, c as d } from '@fuaran-ui/renderer[/sub]'`, across lines.
const tsImport = new RegExp(
  `import\\s+(?:type\\s+)?\\{([^}]*)\\}\\s*from\\s*['"]${escaped}(?:/([\\w-]+))?['"]`,
  'g',
);
// `[<Import("name", "@fuaran-ui/renderer[/sub]")>]`
const fableImport = new RegExp(
  `Import\\(\\s*"(\\w+)"\\s*,\\s*"${escaped}(?:/([\\w-]+))?"\\s*\\)`,
  'g',
);

/** Every named import of the renderer package in a source file, TS and Fable forms. */
export function rendererImports(source: string): RendererImport[] {
  const found: RendererImport[] = [];
  for (const m of source.matchAll(tsImport)) {
    for (const part of m[1]!.split(',')) {
      const name = part
        .trim()
        .replace(/^type\s+/, '')
        .split(/\s+as\s+/)[0]!;
      if (name !== '') found.push({ entry: m[2] ?? '.', name });
    }
  }
  for (const m of source.matchAll(fableImport)) found.push({ entry: m[2] ?? '.', name: m[1]! });
  return found;
}

const skipDirs = new Set(['node_modules', 'output', 'obj', 'bin', 'fable_modules', '.git']);

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    if (entry.isDirectory()) {
      return skipDirs.has(entry.name) || entry.name.startsWith('dist')
        ? []
        : sourceFiles(join(dir, entry.name));
    }
    return /\.(tsx?|mjs|fs)$/.test(entry.name) ? [join(dir, entry.name)] : [];
  });
}

describe('the renderer scanner', () => {
  it('reads aliased, type-only and multi-line TypeScript imports', () => {
    const source = [
      `import { FuaranRenderer, type Theme as T,`,
      `  emptySources } from '@fuaran-ui/renderer';`,
      `import { enhanceMath } from '@fuaran-ui/renderer/enhance-math';`,
      `import '@fuaran-ui/renderer/css';`,
    ].join('\n');
    expect(rendererImports(source)).toEqual([
      { entry: '.', name: 'FuaranRenderer' },
      { entry: '.', name: 'Theme' },
      { entry: '.', name: 'emptySources' },
      { entry: 'enhance-math', name: 'enhanceMath' },
    ]);
  });

  it('reads the Fable Import attribute, which the compiler cannot check', () => {
    expect(
      rendererImports(`[<Import("enhanceMath", "@fuaran-ui/renderer/enhance-math")>]`),
    ).toEqual([{ entry: 'enhance-math', name: 'enhanceMath' }]);
    expect(rendererImports(`[<Import("enhanceMath", "@fuaran-ui/renderer")>]`)).toEqual([
      { entry: '.', name: 'enhanceMath' },
    ]);
  });
});

describe('every renderer import in this repository', () => {
  const imports = sourceFiles(repoRoot)
    .filter((file) => !file.endsWith('rendererSubpaths.test.ts'))
    .flatMap((file) =>
      rendererImports(readFileSync(file, 'utf8')).map((i) => ({
        ...i,
        file: relative(repoRoot, file).replaceAll('\\', '/'),
      })),
    );

  it('finds the imports it is meant to hold', () => {
    // A scan that reads nothing would pass every assertion below; pin the two
    // entry points this repo is known to consume.
    expect(imports).toContainEqual(
      expect.objectContaining({ file: 'src/hosts/tsHostEntry.tsx', name: 'FuaranRenderer' }),
    );
    expect(imports).toContainEqual(
      expect.objectContaining({ file: 'fable-host/Host.fs', entry: 'enhance-math' }),
    );
  });

  it('names only what the entry it imports from really exports', async () => {
    const entries = new Map<string, Record<string, unknown>>();
    const missing: string[] = [];
    for (const { entry, name, file } of imports) {
      const specifier = entry === '.' ? PACKAGE : `${PACKAGE}/${entry}`;
      if (!entries.has(specifier))
        entries.set(specifier, await import(/* @vite-ignore */ specifier));
      // A type-only name leaves no runtime export; keep to the names the code
      // really loads, which is every one of the moved functions.
      if (!(name in entries.get(specifier)!) && /^[a-z]/.test(name)) {
        missing.push(`${file}: ${name} from ${specifier}`);
      }
    }
    expect(missing).toEqual([]);
  });
});

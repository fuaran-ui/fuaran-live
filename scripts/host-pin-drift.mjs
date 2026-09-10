// The corpus-drift sentinel for THIS repo's host pins (Phase 1669).
//
// Phase 1583 put a sentinel on fuaran-py: how far its committed corpus snapshot has
// fallen behind the authority. This is the same idea aimed at the thing fuaran-live
// actually holds. It bundles no snapshot; what it pins is HOSTS — `@fuaran-ui/*` at
// exact-ish ranges in package.json, and `fuaran-py==X` in the workflows — while its
// conformance gate reads the corpus UNPINNED, at whatever HEAD is. So the drift that
// bites here is not a stale copy of the spec; it is a pinned host that predates a
// corpus move, which no amount of reading this repo can reveal.
//
// It bit twice on 2026-09-07 alone — `Chart.stacked` in the morning, `Tabs.activeIndex`
// in the afternoon — in the same shape each time: the corpus gained an omit-at-default
// encoding, the reference host followed it, and the pinned hosts kept emitting the
// member. The first anyone knew was a red conformance gate, and because the showcase
// deploy halts on a red gate by design, the first SYMPTOM was a halted deploy. This
// step exists so that arrives as a named warning on an ordinary push instead.
//
// ── WHAT IT MEASURES, AND WHAT THAT CAN AND CANNOT SAY ───────────────────────────
//
// For each pinned host: the corpus commits that landed AFTER the pinned version was
// published. A corpus commit newer than the release cannot possibly be reflected in
// it, so this is a sound upper bound on "the corpus has moved past what this pin
// emits" — computed from two facts both registries publish, with no guess about what
// any commit means.
//
// It is an upper bound and not a verdict, said plainly because the difference is the
// whole reason this is a WARN and not a gate: most corpus commits move no encoding a
// given host emits, so a named pin is a prompt to look, never a defect on its own. The
// conformance suite is what decides; this only makes the distance visible earlier. A
// gate here would redden `main` for a corpus typo, and a gate that is red for nothing
// is one people learn to step over.
//
// The converse limit matters too: silence here does NOT mean the pins are current. A
// corpus commit that landed BEFORE the pinned release is invisible to this check even
// if the release ignored it — the release date bounds what the host COULD have carried,
// never what it did.
//
// Network is required (two registry lookups) and a failure is reported as UNVERIFIED
// rather than as a clean bill or an accusation — the posture `fuaran-program`'s pin
// check settled on. A shallow corpus checkout is likewise reported as unmeasurable,
// naming the remedy: the distance is a question about history, and `fetch-depth: 1`
// holds none.
//
// Usage:  node scripts/host-pin-drift.mjs [--corpus <path>]
// Exit:   0 nothing behind (or unmeasurable) · 2 at least one pin is behind

import { existsSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');

const argCorpus = (() => {
  const i = process.argv.indexOf('--corpus');
  return i >= 0 ? process.argv[i + 1] : undefined;
})();
const corpusRoot = resolve(argCorpus ?? resolve(repoRoot, '..', 'wire-format-fixtures'));

/** The fixture families a host is certified against — the bytes a pin can be behind. */
const CORPUS_PATHS = ['nodes', 'ops', 'reject', 'lenient', 'manifest.json'];

const out = [];
const say = (line = '') => out.push(line);

const git = (...args) => spawnSync('git', ['-C', corpusRoot, ...args], { encoding: 'utf8' });

/**
 * The npm hosts IN SCOPE, and the ones deliberately out of it.
 *
 * Scope is DERIVED, not listed: it is the `@fuaran-ui/*` packages the conformance
 * arms themselves import, because those are the hosts a corpus move can redden the
 * gate through. A hand list would decay in the usual direction — the arm grows an
 * import and the sentinel keeps reporting on the set someone wrote down once.
 *
 * The rest of the dependency set is reported as OUT OF SCOPE by name rather than
 * dropped. `@fuaran-ui/op-stream` certifies against the chain corpus and
 * `@fuaran-ui/charts` / `@fuaran-ui/ai-tools` against neither node nor op fixtures,
 * so measuring them against the fixture families says nothing true: the first run of
 * this check named all three as 5, 5 and 38 commits behind, which is a finding that
 * can never clear, and a finding that is always present is one people learn to scroll
 * past. "Not measured" must not read as "measured clean" either, which is why they
 * are printed.
 */
const npmSubjects = () => {
  const pkg = JSON.parse(readFileSync(resolve(repoRoot, 'package.json'), 'utf8'));
  const declared = new Map(
    Object.entries(pkg.dependencies ?? {})
      .filter(([name]) => name.startsWith('@fuaran-ui/'))
      .map(([name, range]) => [name, String(range).replace(/^[\^~>=\s]+/, '')]),
  );

  const armDir = resolve(repoRoot, 'tests', 'projection-conformance');
  const listed = spawnSync('git', ['-C', repoRoot, 'ls-files', 'tests/projection-conformance'], {
    encoding: 'utf8',
  });
  const imported = new Set();
  for (const rel of (listed.status === 0 ? listed.stdout.split('\n') : []).filter((f) =>
    /\.(ts|tsx|mts)$/.test(f),
  )) {
    const text = readFileSync(resolve(repoRoot, rel), 'utf8');
    for (const m of text.matchAll(/from\s+'(@fuaran-ui\/[a-z0-9-]+)'/g)) imported.add(m[1]);
  }

  const inScope = [...declared].filter(([name]) => imported.has(name));
  const outOfScope = [...declared].filter(([name]) => !imported.has(name)).map(([n]) => n);
  return { inScope, outOfScope, armDir };
};

/**
 * The `fuaran-py` pin, read from the workflows — where it lives. There is no
 * requirements file here: the Python host is installed by an exact `pip install`
 * line, so the workflow IS the declaration, and reading it is what keeps this
 * check measuring the pin CI uses rather than a second copy of it.
 *
 * A disagreement between workflows is reported rather than resolved: three files
 * carry the line and they are meant to move together, so two answers is itself
 * the finding.
 */
const pyPins = () => {
  const dir = resolve(repoRoot, '.github', 'workflows');
  const found = new Map();
  const r = spawnSync('git', ['-C', repoRoot, 'ls-files', '.github/workflows'], {
    encoding: 'utf8',
  });
  const files = (r.status === 0 ? r.stdout.split('\n') : []).filter((f) => f.endsWith('.yml'));
  for (const rel of files) {
    const text = readFileSync(resolve(repoRoot, rel), 'utf8');
    for (const m of text.matchAll(/fuaran-py==([0-9][0-9A-Za-z.\-+]*)/g)) {
      const where = found.get(m[1]) ?? [];
      where.push(rel.replace(`${dir}/`, ''));
      found.set(m[1], where);
    }
  }
  return found;
};

const iso = (t) => (typeof t === 'string' ? t : undefined);

const fetchJson = async (url) => {
  const res = await fetch(url, { headers: { accept: 'application/json' } });
  if (!res.ok) throw new Error(`HTTP ${res.status} for ${url}`);
  return res.json();
};

/** npm: the publish time of one version, plus the newest version. */
const npmFacts = async (name) => {
  const doc = await fetchJson(`https://registry.npmjs.org/${name.replace('/', '%2f')}`);
  return { times: doc.time ?? {}, latest: doc['dist-tags']?.latest };
};

/** PyPI: the upload time of one version, plus the newest version. */
const pypiFacts = async (name, version) => {
  const [latestDoc, versionDoc] = await Promise.all([
    fetchJson(`https://pypi.org/pypi/${name}/json`),
    fetchJson(`https://pypi.org/pypi/${name}/${version}/json`).catch(() => null),
  ]);
  const uploads = (versionDoc?.urls ?? []).map((u) => u.upload_time_iso_8601).filter(iso);
  return { published: uploads.sort()[0], latest: latestDoc?.info?.version };
};

/** Corpus commits newer than `since`, over the fixture families only. */
const corpusCommitsSince = (since) => {
  const r = git(
    'log',
    `--since=${since}`,
    '--format=%h%x09%cI%x09%s',
    '--no-merges',
    '--',
    ...CORPUS_PATHS,
  );
  if (r.status !== 0) return { error: (r.stderr || '').trim() || 'git log failed' };
  return {
    commits: r.stdout
      .split('\n')
      .filter((l) => l.trim() !== '')
      .map((l) => {
        const [sha, when, ...rest] = l.split('\t');
        return { sha, when, subject: rest.join('\t') };
      }),
  };
};

const main = async () => {
  say('# Host-pin corpus drift (Phase 1669)');
  say();

  if (!existsSync(resolve(corpusRoot, 'manifest.json'))) {
    say(`UNMEASURABLE: no corpus at ${corpusRoot} (expected a manifest.json under it).`);
    say('Check the corpus out as a sibling, or pass --corpus <path>.');
    return 0;
  }

  const head = git('rev-parse', 'HEAD');
  if (head.status !== 0) {
    say(`UNMEASURABLE: ${corpusRoot} is not a git checkout — the distance is a question`);
    say('about history, and there is none to read.');
    return 0;
  }
  const shallow = existsSync(resolve(corpusRoot, '.git', 'shallow'));
  say(`corpus: ${corpusRoot} @ ${head.stdout.trim().slice(0, 12)}`);
  if (shallow) {
    say();
    say('UNMEASURABLE: the corpus checkout is SHALLOW, so it holds HEAD and no history.');
    say('Give the corpus checkout `fetch-depth: 0` — reporting "unknown" on every run');
    say('would make this step decoration.');
    return 0;
  }
  say();

  const { inScope, outOfScope } = npmSubjects();
  if (inScope.length === 0) {
    say('UNMEASURABLE: no @fuaran-ui/* import found under tests/projection-conformance/,');
    say('so the in-scope host set derived empty. Either the arms moved, or this check');
    say('is reading the wrong tree — it is NOT evidence that the pins are current.');
    return 0;
  }

  const subjects = [];
  for (const [name, version] of inScope) subjects.push({ kind: 'npm', name, version });
  for (const [version, where] of pyPins())
    subjects.push({ kind: 'pypi', name: 'fuaran-py', version, where });

  let behind = 0;
  let unverified = 0;
  const npmCache = new Map();

  for (const s of subjects) {
    let published;
    let latest;
    try {
      if (s.kind === 'npm') {
        if (!npmCache.has(s.name)) npmCache.set(s.name, await npmFacts(s.name));
        const f = npmCache.get(s.name);
        published = iso(f.times[s.version]);
        latest = f.latest;
      } else {
        const f = await pypiFacts(s.name, s.version);
        published = f.published;
        latest = f.latest;
      }
    } catch (err) {
      say(`UNVERIFIED  ${s.name} ${s.version} — ${String(err.message ?? err)}`);
      unverified += 1;
      continue;
    }

    if (published === undefined) {
      // A pin the registry does not serve is a different defect, and not this
      // check's to diagnose — but it must not read as "current".
      say(`UNVERIFIED  ${s.name} ${s.version} — the registry serves no such version`);
      unverified += 1;
      continue;
    }

    const found = corpusCommitsSince(published);
    if ('error' in found) {
      say(`UNVERIFIED  ${s.name} ${s.version} — ${found.error}`);
      unverified += 1;
      continue;
    }

    const at = `${s.name} ${s.version} (published ${published.slice(0, 10)})`;
    if (found.commits.length === 0) {
      say(`ok          ${at} — no corpus fixture commit is newer`);
      continue;
    }

    behind += 1;
    const remedy =
      latest !== undefined && latest !== s.version
        ? `a newer release exists: ${latest}`
        : 'no newer release exists yet — the host leg has to ship first';
    say(`BEHIND      ${at}`);
    say(`            ${found.commits.length} corpus fixture commit(s) landed after it; ${remedy}`);
    if (s.where !== undefined) say(`            pinned in: ${s.where.join(', ')}`);
    for (const c of found.commits.slice(0, 8))
      say(`              ${c.sha} ${c.when.slice(0, 10)} ${c.subject}`);
    if (found.commits.length > 8) say(`              … and ${found.commits.length - 8} more`);
  }

  if (outOfScope.length > 0) {
    say();
    say(`not measured (the conformance arms import none of these): ${outOfScope.join(', ')}`);
    say('They certify against other families or none, so a fixture-commit distance would');
    say('say nothing true about them. Printed rather than dropped: not measured is not clean.');
  }

  say();
  if (behind === 0 && unverified === 0) {
    say('No pinned host predates a corpus fixture commit.');
    return 0;
  }
  if (behind === 0) {
    say(`Nothing measured as behind; ${unverified} pin(s) UNVERIFIED (see above).`);
    return 0;
  }
  say(
    `${behind} pin(s) predate a corpus fixture commit${unverified > 0 ? `; ${unverified} unverified` : ''}.`,
  );
  say();
  say('This is a WARNING and an UPPER BOUND, not a verdict: most corpus commits move no');
  say('encoding a given host emits. It is the prompt to check the conformance arms before');
  say('the deploy does it for you — a pinned host that predates an omit-at-default change');
  say('is what halted the showcase deploy twice on 2026-09-07.');
  return 2;
};

main()
  .then((code) => {
    console.log(out.join('\n'));
    process.exit(code);
  })
  .catch((err) => {
    console.log(out.join('\n'));
    console.error(`[host-pin-drift] ${String(err?.stack ?? err)}`);
    // An unexpected fault is not evidence about the pins, so it must not read as one.
    process.exit(0);
  });

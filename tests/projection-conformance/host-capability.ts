// The host capability manifest, consumer side — WIRE_FORMAT.md §27 (Phase 1582).
//
// A conformance arm holds fixtures aside when its pinned HOST cannot author the
// construct they exercise. Until §27 that was a hand-written table of fixture
// ids: exactly as honest as its last re-measurement, decaying in both directions
// — an entry outliving the release that closed its gap, a shortfall nobody wrote
// down reading as a pass. `./quarantine.ts` records six dated passes of exactly
// that decay.
//
// §27 replaces the list with a projection. A host publishes, generated from its
// own type model, the set of wire constructs it can AUTHOR; this module derives
// what each fixture exercises by walking it against the corpus IDL, and the
// expected-unmodelled set is the difference. A wrong entry then cannot exist,
// because there are no entries.
//
// ── WHAT THIS MODULE IS CAREFUL ABOUT ────────────────────────────────────────
//
// **Silence is not a claim (§27.4 rule 2).** A manifest declares, per token
// family and per scope key, whether it says anything at all. A construct absent
// from an UNCLAIMED family means the host declined to guess, not that it lacks
// the construct — and treating the two alike is how a computed set becomes a
// worse hand-written list, because it looks derived. `isClaimed` is the whole of
// that rule and every consumer here goes through it.
//
// **A manifest describes ONE release (§27.4 rule 5).** `resolveManifest` refuses
// one whose `hostVersion` is not the version of the interpreter the arm actually
// executes, names both, and falls back. The Python arm runs a repo-local `.venv`
// pinned to a PyPI release, not the sibling working tree, so this is the live
// case rather than a hypothetical: a release that carries no generator yields no
// manifest, and the arm keeps its pre-manifest table until the pin moves.
//
// **The residual is declared and self-retiring (§27.4 rule 6).** Where a manifest
// makes no claim there is a hole, and a fixture whose only host gap falls in it
// is neither computed-unmodelled nor honestly failable. A table entry may fill
// that hole by naming the unclaimed family it stands on — and `staleResiduals`
// fails it the moment the manifest DOES claim that family, so it cannot outlive
// the gap it names.

// ── the manifest document (§27.3) ────────────────────────────────────────────

export const MANIFEST_FORMAT = 'fuaran.host-capability/1';

/** The six token families §27.2 names. */
export type TokenFamily =
  'kinds' | 'kindFields' | 'unionCases' | 'caseFields' | 'recordFields' | 'hostedCases';

export interface FamilyDeclaration {
  readonly covered: boolean;
  /** Why an uncovered family is uncovered, or what a narrowed `scope` leaves out. */
  readonly reason?: string;
  /** The scope keys claimed. Absent means the whole family. */
  readonly scope?: readonly string[];
}

export interface CapabilityManifest {
  readonly $manifest: string;
  readonly host: string;
  readonly hostVersion: string;
  readonly corpusAuthority?: string | null;
  readonly generator: string;
  readonly families: Readonly<Partial<Record<TokenFamily, FamilyDeclaration>>>;
  readonly tokens: readonly string[];
}

/** What the executing host said about itself, as the arm's executor reports it. */
export interface HostDeclaration {
  readonly host?: unknown;
  readonly hostVersion?: unknown;
  readonly capabilityManifest?: unknown;
  readonly capabilityError?: unknown;
}

const isRecord = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

/**
 * Decode a manifest, or say in one sentence why it is not one.
 *
 * It REFUSES an unrecognised `$manifest` outright rather than reading the members
 * it happens to know (§27.3): a document from a later format may mean something
 * different by the same field names, and a consumer that reads it anyway has
 * turned a version marker into decoration.
 */
export const parseManifest = (value: unknown): CapabilityManifest | { error: string } => {
  if (!isRecord(value)) return { error: 'not an object' };
  if (value.$manifest !== MANIFEST_FORMAT)
    return {
      error: `unrecognised $manifest '${String(value.$manifest)}' (expected ${MANIFEST_FORMAT})`,
    };
  if (typeof value.host !== 'string' || value.host === '') return { error: 'no host' };
  if (typeof value.hostVersion !== 'string' || value.hostVersion === '')
    return { error: 'no hostVersion' };
  if (typeof value.generator !== 'string') return { error: 'no generator' };
  if (!isRecord(value.families)) return { error: 'no families' };
  if (!Array.isArray(value.tokens) || value.tokens.some((t) => typeof t !== 'string'))
    return { error: 'tokens is not an array of strings' };

  const families: Partial<Record<TokenFamily, FamilyDeclaration>> = {};
  for (const [name, raw] of Object.entries(value.families)) {
    if (!isRecord(raw) || typeof raw.covered !== 'boolean')
      return { error: `family '${name}' has no boolean 'covered'` };
    const scope = raw.scope;
    if (scope !== undefined && (!Array.isArray(scope) || scope.some((s) => typeof s !== 'string')))
      return { error: `family '${name}' has a non-string scope` };
    families[name as TokenFamily] = {
      covered: raw.covered,
      reason: typeof raw.reason === 'string' ? raw.reason : undefined,
      scope: scope as readonly string[] | undefined,
    };
  }

  return {
    $manifest: MANIFEST_FORMAT,
    host: value.host,
    hostVersion: value.hostVersion,
    corpusAuthority: typeof value.corpusAuthority === 'string' ? value.corpusAuthority : null,
    generator: value.generator,
    families,
    tokens: value.tokens as readonly string[],
  };
};

// ── the token grammar (§27.2) ────────────────────────────────────────────────

const isTag = (segment: string): boolean => /^[A-Z]/.test(segment);

export interface TokenShape {
  readonly family: TokenFamily;
  /** The scope key a family's `scope` lists, when the family has one. */
  readonly scopeKey?: string;
}

/**
 * Which family a token belongs to, and under which scope key.
 *
 * The grammar carries no family marker; it is read off the ALTERNATION of case.
 * A discriminator is PascalCase and a wire key is camelCase throughout this
 * format, and a hosted token is always exactly one segment longer than the field
 * token it extends — so the six shapes are told apart without a table.
 */
export const classify = (token: string): TokenShape | undefined => {
  const s = token.split('.');
  if (s.length < 2 || s.some((seg) => seg === '')) return undefined;

  if (s[0] === 'Kind') {
    if (s.length === 2) return { family: 'kinds' };
    if (s.length === 3) return { family: 'kindFields' };
    if (s.length === 4) return { family: 'hostedCases', scopeKey: s.slice(0, 3).join('.') };
    return undefined;
  }
  if (s.length === 2)
    return isTag(s[1]!)
      ? { family: 'unionCases', scopeKey: s[0] }
      : { family: 'recordFields', scopeKey: s[0] };
  if (s.length === 3)
    return isTag(s[1]!)
      ? { family: 'caseFields', scopeKey: s[0] }
      : { family: 'hostedCases', scopeKey: s.slice(0, 2).join('.') };
  if (s.length === 4) return { family: 'hostedCases', scopeKey: s.slice(0, 3).join('.') };
  return undefined;
};

/** Whether the manifest makes ANY claim about this token — §27.4 rule 2. */
export const isClaimed = (manifest: CapabilityManifest, token: string): boolean => {
  const shape = classify(token);
  if (shape === undefined) return false;
  const declaration = manifest.families[shape.family];
  if (declaration === undefined || !declaration.covered) return false;
  if (declaration.scope === undefined) return true;
  return shape.scopeKey !== undefined && declaration.scope.includes(shape.scopeKey);
};

/** Whether a family (optionally at a scope key) is claimed — the residual's falsifier. */
export const isFamilyClaimed = (
  manifest: CapabilityManifest,
  family: TokenFamily,
  scopeKey?: string,
): boolean => {
  const declaration = manifest.families[family];
  if (declaration === undefined || !declaration.covered) return false;
  if (declaration.scope === undefined) return true;
  return scopeKey !== undefined && declaration.scope.includes(scopeKey);
};

// ── deriving what a fixture exercises, from the corpus IDL ───────────────────

export interface IdlType {
  readonly $type: string;
  readonly name?: string;
  readonly of?: IdlType;
  readonly values?: IdlType;
}
export interface IdlField {
  readonly name: string;
  readonly type: IdlType;
}
export interface Idl {
  readonly kinds: readonly { readonly tag: string; readonly fields: readonly IdlField[] }[];
  readonly unions: readonly {
    readonly name: string;
    readonly cases: readonly { readonly tag: string; readonly fields: readonly IdlField[] }[];
  }[];
  readonly records: readonly { readonly name: string; readonly fields: readonly IdlField[] }[];
  readonly nodeFields: readonly IdlField[];
}

/**
 * Every construct token a wire document exercises.
 *
 * The walk is IDL-GUIDED rather than a sweep for `$type` strings, and that is
 * what makes a token positional: the same discriminator means different things
 * at different slots — `I18n` is a `TextSource` case in a label and a `Binding`
 * case in a value, and this host models one and not the other. A sweep would
 * conflate them and report the wrong construct as missing.
 *
 * Where a slot holds no discriminated object — a bare string in a `TextSource`,
 * which is the canonical spelling of its `Literal` case — nothing is emitted. No
 * document carries a discriminator there, so no host can lack one.
 */
export const deriveTokens = (document: unknown, idl: Idl): Set<string> => {
  const kinds = new Map(idl.kinds.map((k) => [k.tag, k]));
  const unions = new Map(idl.unions.map((u) => [u.name, u]));
  const records = new Map(idl.records.map((r) => [r.name, r]));
  const out = new Set<string>();

  const walkNode = (value: unknown): void => {
    if (!isRecord(value)) return;
    if ('kind' in value) walkKind(value.kind);
    for (const field of idl.nodeFields) {
      if (field.name === 'kind') continue;
      if (field.name in value) walk(value[field.name], field.type, `Node.${field.name}`);
    }
  };

  const walkKind = (value: unknown): void => {
    if (!isRecord(value) || typeof value.$type !== 'string') return;
    const tag = value.$type;
    out.add(`Kind.${tag}`);
    const kind = kinds.get(tag);
    if (kind === undefined) return;
    for (const field of kind.fields) {
      if (!(field.name in value)) continue;
      const token = `Kind.${tag}.${field.name}`;
      out.add(token);
      walk(value[field.name], field.type, token);
    }
  };

  const walk = (value: unknown, type: IdlType, owner: string): void => {
    switch (type.$type) {
      case 'union': {
        const union = type.name === undefined ? undefined : unions.get(type.name);
        if (union === undefined || !isRecord(value) || typeof value.$type !== 'string') return;
        const tag = value.$type;
        out.add(`${union.name}.${tag}`);
        const kase = union.cases.find((c) => c.tag === tag);
        if (kase === undefined) return;
        for (const field of kase.fields) {
          if (!(field.name in value)) continue;
          const token = `${union.name}.${tag}.${field.name}`;
          out.add(token);
          walk(value[field.name], field.type, token);
        }
        return;
      }
      case 'record': {
        const record = type.name === undefined ? undefined : records.get(type.name);
        if (record === undefined || !isRecord(value)) return;
        for (const field of record.fields) {
          if (!(field.name in value)) continue;
          const token = `${record.name}.${field.name}`;
          out.add(token);
          walk(value[field.name], field.type, token);
        }
        return;
      }
      case 'list':
        if (Array.isArray(value) && type.of !== undefined)
          for (const item of value) walk(item, type.of, owner);
        return;
      case 'map':
        if (isRecord(value) && type.values !== undefined)
          for (const item of Object.values(value)) walk(item, type.values, owner);
        return;
      case 'node':
        walkNode(value);
        return;
      case 'kind':
        walkKind(value);
        return;
      case 'hosted':
        // One level, and only where the payload discriminates itself: the IDL
        // says nothing about what is inside, so the discriminator is all there
        // is to name. Deeper structure is outside the §27.2 grammar.
        for (const item of Array.isArray(value) ? value : [value])
          if (isRecord(item) && typeof item.$type === 'string') out.add(`${owner}.${item.$type}`);
        return;
      default:
        // enum / str / bool / int / float / fn / json / var / op — no construct
        // to name, or (for `op`) a vocabulary this arm's corpus slice never holds.
        return;
    }
  };

  walkNode(document);
  return out;
};

// ── resolving, computing, and the residual ───────────────────────────────────

export type ManifestResolution =
  | { readonly mode: 'computed'; readonly manifest: CapabilityManifest }
  | { readonly mode: 'fallback'; readonly reason: string };

/**
 * The §27.4 rule-5 version binding, plus the ordinary absences around it.
 *
 * The mismatch report names BOTH versions, because the useful sentence is never
 * "no manifest" — it is "the manifest describes 0.5.0 and this arm executes
 * 0.4.0", which tells a reader whether the fix is to move the pin or to publish.
 */
export const resolveManifest = (declaration: HostDeclaration): ManifestResolution => {
  const observed =
    typeof declaration.hostVersion === 'string' ? declaration.hostVersion : undefined;
  const where = observed === undefined ? 'the executing host' : `the executing host (${observed})`;

  if (declaration.capabilityManifest === undefined || declaration.capabilityManifest === null) {
    const why =
      typeof declaration.capabilityError === 'string' && declaration.capabilityError !== ''
        ? declaration.capabilityError
        : 'it publishes none';
    return { mode: 'fallback', reason: `no capability manifest from ${where}: ${why}` };
  }

  const parsed = parseManifest(declaration.capabilityManifest);
  if ('error' in parsed)
    return {
      mode: 'fallback',
      reason: `the manifest from ${where} is unreadable: ${parsed.error}`,
    };

  if (observed === undefined)
    return {
      mode: 'fallback',
      reason: `the manifest declares ${parsed.host} ${parsed.hostVersion}, but this arm could not read the executing host's version — §27.4 rule 5 refuses an unbound manifest`,
    };
  if (parsed.hostVersion !== observed)
    return {
      mode: 'fallback',
      reason: `the manifest describes ${parsed.host} ${parsed.hostVersion} and this arm executes ${observed} — §27.4 rule 5 refuses it; move the pin, or publish a manifest for the pinned release`,
    };

  return { mode: 'computed', manifest: parsed };
};

/**
 * The expected-unmodelled set: fixture id → the CLAIMED tokens it exercises that
 * the manifest does not declare. §27.4 rule 1.
 */
export const computeExpectedUnmodelled = (
  manifest: CapabilityManifest,
  tokensByFixture: ReadonlyMap<string, ReadonlySet<string>>,
): Map<string, string[]> => {
  const declared = new Set(manifest.tokens);
  const out = new Map<string, string[]>();
  for (const [id, tokens] of tokensByFixture) {
    const missing = [...tokens].filter((t) => isClaimed(manifest, t) && !declared.has(t)).sort();
    if (missing.length > 0) out.set(id, missing);
  }
  return out;
};

/** A declared residual: a fixture held aside on a claim the manifest does NOT make. */
export interface Residual {
  readonly id: string;
  readonly family: TokenFamily;
  readonly scopeKey?: string;
}

/**
 * Residual entries the manifest has overtaken — §27.4 rule 6.
 *
 * The moment a manifest claims the family a residual stands on, that entry is
 * either already computed or contradicted; either way it is stale, and saying so
 * is what stops the residual from silently becoming the hand-written list again.
 */
export const staleResiduals = (
  manifest: CapabilityManifest,
  residuals: Iterable<Residual>,
): string[] =>
  [...residuals]
    .filter((r) => isFamilyClaimed(manifest, r.family, r.scopeKey))
    .map(
      (r) =>
        `'${r.id}' stands on '${r.family}'${r.scopeKey === undefined ? '' : ` at '${r.scopeKey}'`}, which ${manifest.host} ${manifest.hostVersion} now claims — the computed set covers it, so REMOVE the residual`,
    )
    .sort();

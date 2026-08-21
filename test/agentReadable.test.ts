// =============================================================================
//  The Agent-Readable Page — the affordance-annotation lock.
//
//  WHY THIS FILE EXISTS. The showcase page hangs a declared-affordance vocabulary
//  on its own controls as `data-fuaran-*` attributes, and those attributes are a
//  CONTRACT a reader relies on: an agent or an assistive client parses the JSON
//  payloads and acts on them. The page's own "what an agent sees" pane reads them
//  back out of the DOM, so a malformed payload would degrade that pane silently
//  rather than failing anything — exactly the drift class the in-page wire
//  emitters were locked against.
//
//  The page exports its whole annotation set as one canonical JSON document
//  (`annotationsJson`), minted by the same `Fuaran.Core` canonical encoder the
//  wire format uses, over the shipped affordance vocabulary's own `toWire`
//  projections. This file certifies the resulting payloads:
//
//    * every published control carries the unconditional attributes;
//    * every closed-set token is in its closed set (shape / effect);
//    * every JSON payload parses and has the declared shape;
//    * NO null appears anywhere — an open end of a bound is OMITTED, which is
//      the one property a client's range arithmetic depends on and the one a
//      naive encoder would break first;
//    * `aria-description` is prose with no `{value}` slot left unsubstituted.
//
//  GO-RED SELF-TEST. The validator is applied to deliberately perturbed copies of
//  the real document at the bottom of this file, so the lock is proven to bite
//  rather than merely to pass. That is cheaper and more durable than a one-off
//  manual mutation, which leaves no trace once reverted.
// =============================================================================

import { describe, expect, it } from 'vitest';

import { annotationsJson } from '../app/showcase/output/AgentReadable.js';

const SHAPES = ['text', 'number', 'boolean', 'choice', 'unknown'];
const EFFECTS = ['read', 'write', 'navigate', 'invoke'];
const HINT_KINDS = ['oneOf', 'numberRange', 'textLength'];

interface Entry {
  field: string;
  attributes: Record<string, string>;
}

/** Every value reachable in a parsed JSON structure, leaves included. */
function walk(value: unknown, visit: (leaf: unknown) => void): void {
  visit(value);
  if (Array.isArray(value)) {
    for (const item of value) walk(item, visit);
  } else if (value !== null && typeof value === 'object') {
    for (const item of Object.values(value as Record<string, unknown>)) walk(item, visit);
  }
}

/**
 * The whole contract, as a function so the go-red self-test can run it over a
 * perturbed document. Throws on the first violation.
 */
function certify(document: string): Entry[] {
  const entries = JSON.parse(document) as Entry[];
  if (!Array.isArray(entries) || entries.length === 0) {
    throw new Error('the annotation document is not a non-empty array');
  }

  for (const entry of entries) {
    const attrs = entry.attributes;
    if (!attrs || typeof attrs !== 'object') {
      throw new Error(`${entry.field}: no attributes object`);
    }

    // Every published control carries these four unconditionally.
    for (const required of [
      'data-fuaran-module',
      'data-fuaran-field',
      'data-fuaran-shape',
      'data-fuaran-controllable',
      'data-fuaran-commands',
    ]) {
      if (typeof attrs[required] !== 'string' || attrs[required] === '') {
        throw new Error(`${entry.field}: missing ${required}`);
      }
    }

    // The marker attribute IS the field id — the pane's read keys off it, so a
    // drift here would silently mis-address a control.
    if (attrs['data-fuaran-field'] !== entry.field) {
      throw new Error(`${entry.field}: data-fuaran-field disagrees with the field id`);
    }

    if (!SHAPES.includes(attrs['data-fuaran-shape'])) {
      throw new Error(
        `${entry.field}: shape "${attrs['data-fuaran-shape']}" is outside the closed set`,
      );
    }

    if (!['true', 'false'].includes(attrs['data-fuaran-controllable'])) {
      throw new Error(`${entry.field}: controllable is not a bare boolean token`);
    }

    const commands = JSON.parse(attrs['data-fuaran-commands']) as {
      phrase: string;
      effect: string;
    }[];
    if (!Array.isArray(commands) || commands.length === 0) {
      throw new Error(`${entry.field}: commands is not a non-empty array`);
    }
    for (const command of commands) {
      if (typeof command.phrase !== 'string' || command.phrase === '') {
        throw new Error(`${entry.field}: a command has no phrase`);
      }
      if (!EFFECTS.includes(command.effect)) {
        throw new Error(`${entry.field}: effect "${command.effect}" is outside the closed set`);
      }
    }

    if (attrs['data-fuaran-aliases'] !== undefined) {
      const aliases = JSON.parse(attrs['data-fuaran-aliases']) as {
        alias: string;
        value: string;
      }[];
      if (!Array.isArray(aliases) || aliases.length === 0) {
        throw new Error(`${entry.field}: an aliases attribute was emitted but carries nothing`);
      }
      for (const alias of aliases) {
        if (typeof alias.alias !== 'string' || typeof alias.value !== 'string') {
          throw new Error(`${entry.field}: an alias pair is not two strings`);
        }
      }
    }

    if (attrs['data-fuaran-values'] !== undefined) {
      const hint = JSON.parse(attrs['data-fuaran-values']) as Record<string, unknown>;
      if (!HINT_KINDS.includes(hint.kind as string)) {
        throw new Error(`${entry.field}: value hint kind "${hint.kind}" is outside the closed set`);
      }
      if (hint.kind === 'oneOf' && (!Array.isArray(hint.values) || hint.values.length === 0)) {
        throw new Error(`${entry.field}: a oneOf hint carries no values`);
      }

      // The load-bearing property: an OPEN end of a bound is omitted, never
      // nulled. A client doing range arithmetic branches on key presence.
      walk(hint, (leaf) => {
        if (leaf === null) {
          throw new Error(
            `${entry.field}: a null reached the value hint — an open bound must be omitted`,
          );
        }
      });
    }

    const aria = attrs['aria-description'];
    if (aria !== undefined) {
      if (aria.includes('{value}')) {
        throw new Error(`${entry.field}: aria-description leaks an unsubstituted {value} slot`);
      }
    }
  }

  return entries;
}

describe('the agent-readable page publishes a well-formed affordance declaration', () => {
  const entries = certify(annotationsJson);

  it('publishes every control of the toy application', () => {
    expect(entries.map((e) => e.field)).toEqual([
      'catalogue-title',
      'catalogue-format',
      'catalogue-branch',
      'catalogue-copies',
      'catalogue-notify',
      'catalogue-queue',
    ]);
  });

  it('states the read/write axis explicitly — the queue is readable, not settable', () => {
    const byField = new Map(entries.map((e) => [e.field, e.attributes]));
    expect(byField.get('catalogue-queue')!['data-fuaran-controllable']).toBe('false');
    expect(byField.get('catalogue-copies')!['data-fuaran-controllable']).toBe('true');

    // A readable-only control declares only reads: an agent that trusts the
    // declaration must not find a write phrase on something it may not set.
    const queueCommands = JSON.parse(byField.get('catalogue-queue')!['data-fuaran-commands']) as {
      effect: string;
    }[];
    expect(queueCommands.every((c) => c.effect === 'read')).toBe(true);
  });

  it('omits an open bound rather than nulling it', () => {
    const title = entries.find((e) => e.field === 'catalogue-title')!;
    const hint = JSON.parse(title.attributes['data-fuaran-values']) as Record<string, unknown>;
    expect(hint.kind).toBe('textLength');
    expect(hint.minLength).toBe(2);
    // The declaration has no upper bound, so the key is ABSENT — not null, and
    // not a sentinel.
    expect('maxLength' in hint).toBe(false);
  });

  it('omits the hint entirely where nothing is declared', () => {
    const notify = entries.find((e) => e.field === 'catalogue-notify')!;
    expect(notify.attributes['data-fuaran-values']).toBeUndefined();
    expect(notify.attributes['data-fuaran-aliases']).toBeUndefined();
  });

  it('carries no null anywhere in the whole declaration', () => {
    for (const entry of entries) {
      for (const value of Object.values(entry.attributes)) {
        expect(typeof value).toBe('string');
        if (value.startsWith('[') || value.startsWith('{')) {
          walk(JSON.parse(value), (leaf) => expect(leaf).not.toBeNull());
        }
      }
    }
  });
});

describe('the lock bites (go-red self-test)', () => {
  /** Apply a mutation to the real document and expect certification to fail. */
  const perturbed = (mutate: (entries: Entry[]) => void): (() => void) => {
    const entries = JSON.parse(annotationsJson) as Entry[];
    mutate(entries);
    return () => certify(JSON.stringify(entries));
  };

  it('rejects a shape outside the closed set', () => {
    expect(perturbed((es) => (es[0].attributes['data-fuaran-shape'] = 'freetext'))).toThrow(
      /outside the closed set/,
    );
  });

  it('rejects an effect outside the closed set', () => {
    expect(
      perturbed(
        (es) =>
          (es[0].attributes['data-fuaran-commands'] = JSON.stringify([
            { phrase: 'do the thing', effect: 'mutate' },
          ])),
      ),
    ).toThrow(/outside the closed set/);
  });

  it('rejects a nulled open bound', () => {
    expect(
      perturbed(
        (es) =>
          (es[0].attributes['data-fuaran-values'] = JSON.stringify({
            kind: 'textLength',
            minLength: 2,
            maxLength: null,
          })),
      ),
    ).toThrow(/open bound must be omitted/);
  });

  it('rejects a marker that disagrees with the field id', () => {
    expect(perturbed((es) => (es[0].attributes['data-fuaran-field'] = 'somewhere-else'))).toThrow(
      /disagrees with the field id/,
    );
  });

  it('rejects an aria-description with an unsubstituted slot', () => {
    expect(
      perturbed(
        (es) => (es[0].attributes['aria-description'] = 'You can say: “set it to {value}”.'),
      ),
    ).toThrow(/unsubstituted \{value\} slot/);
  });
});

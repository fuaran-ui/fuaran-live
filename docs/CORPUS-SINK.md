# The opt-in anonymous session-corpus sink

This page describes a feature that is **switched off in the public build**, and explains what it
would do if an operator switched it on. Both halves matter: if you are reading this because you
want to know what the deployed playground sends anywhere, the answer is the first section, and it
is short.

## In the public build, nothing is sent

The collection endpoint is build configuration (`VITE_CORPUS_SINK`). The public build sets none.
With no endpoint:

- no contribution control is rendered anywhere in the page — not a disabled one, none at all;
- nothing in the app can reach a POST, because there is no destination to reach;
- the shipped Content-Security-Policy is **byte-for-byte** what it was before this feature
  existed. Its `connect-src` still names the app's own origin and the five BYOK provider origins
  and nothing else, so the browser would refuse a connection to a collector even if one were
  somehow attempted.

That last point is the one worth stating plainly, because it is the one a reader would otherwise
have to take on trust: a corpus feature that widened the shipped policy by a single character
would have widened every visitor's egress surface, whether or not anyone ever contributed. It
does not, and `test/corpusSink.test.ts` pins the unset case against the provider list.

## What a contribution is, when one is configured

A session in this playground is a small, complete artefact: a starting tree, the ops that carried
it forward, and which model produced them. That is the material a corpus of "what a model emits
when asked for a UI" is made of, and this page is the only place it exists. So with a collector
configured, the page can **offer** to send one.

### What is sent

A single JSON document, `session.fuaran.json`:

| Member              | What it is                                                                                                                                     |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- |
| `kind`, `version`   | `"fuaran-live/session-corpus"` and the format version, so a collector knows what it received.                                                  |
| `capturedAt`        | ISO-8601 UTC, whole seconds.                                                                                                                   |
| `provider`, `model` | Which provider and model id produced the emissions.                                                                                            |
| `promptCount`       | **How many** prompts you sent. Not the prompts.                                                                                                |
| `baseTree`          | The tree the op sequence replays from, as canonical wire JSON.                                                                                 |
| `tree`              | The tree it arrives at.                                                                                                                        |
| `ops`               | Each applied op: its sequence number, its kind, the surface or model it came from, its hash-chain links, and the canonical op document itself. |

Both trees are present because either alone makes the op stream uncheckable — without the base
there is nothing to replay against, without the result there is nothing to check the replay
produced. The ops carry their hash chain, so a recipient can verify the sequence rather than
trusting it.

### What is NOT sent

- **Your API key.** See the next section — this is structural, not a filter.
- **Your prompts, and the model's replies.** Free text a person typed is precisely the category
  that "nothing is stored about you" has to exclude for the sentence to be true. The corpus this
  feeds is a corpus of UI-as-data; the trees and ops are the whole of it, and the prompt _count_
  is what the metadata needs and all it gets.
- **Anything identifying.** There is no account, no cookie, no visitor id, and no session id that
  outlives the tab. The POST sends `credentials: 'omit'`, so no stored credential is attached, and
  carries no header other than `content-type`.

Anonymity here is not a policy applied to identified data. There is no identity to withhold: the
app has never had one to collect. That is the same property that makes this playground keyless and
account-free, read from the other direction — and it is why contributions cannot be de-duplicated
or stitched across visits, which is the named trade-off of the design rather than an oversight.

## Consent

Consent is **per contribution**, and off by default:

- the section is collapsed under "More tools" and does nothing until opened;
- the send button is disabled until you tick the consent box, which starts unticked;
- after every attempt — sent, refused or failed — the tick is cleared, so a second contribution is
  a second decision;
- there is no automatic, background, or "remember this choice" path. There is no code for one.

## The key guard

`SECURITY.md`'s key-handling guarantee 5 states the condition any corpus feature in this
repository must meet: it must be **key-blind by construction** — it must never be able to observe
the key value. That is built rather than promised:

- the module that builds a contribution (`app/Contribute.fs`) opens no key store, and the module
  that holds the key stores (`app/Byok.fs`) knows nothing of the sink. Neither is in the other's
  scope, and the test checks both against the source;
- the seam the sink accepts takes a single-case type that only the guard constructs, so a key
  cannot cross it — the same construction the live-drive channel uses, for the same reason.

On top of that, the built bytes are **scanned before anything is sent**, and a sighting **refuses
the contribution outright**:

- a token shaped like any supported provider's API key (`sk-ant-`, `sk-proj-`, `sk-`, `AIza`,
  `xai-`, each followed by a credential-length run of key characters);
- a provider endpoint URL.

Refusing rather than quietly stripping is deliberate. A bundle that is key-blind by construction
has no legitimate reason to contain key-shaped material, so a sighting means something is wrong —
and silently cleaning one up is indistinguishable from having missed one. You are told which class
was found (never the value), and nothing is uploaded.

`test/corpusSink.test.ts` plants every key format and every provider origin, in the tree, in an
op, and in the metadata, and requires each to produce a refusal **and zero network calls**. It
also requires the clean path to genuinely POST — otherwise "no leak found" could be the vacuous
result of a probe that never sent anything — and it requires the guard _not_ to fire on ordinary
prose that happens to contain a prefix (`risk-averse`, `task-oriented`), because a guard that
fires on English is a guard someone turns off.

## Wiring a collector

The collector itself is **not in this repository**, and is not part of this project's public
surface: it is a separate deliverable, maintained elsewhere, deliberately out of scope here so
that the public artefact contains no collection logic at all. What follows is the contract it has
to meet.

1. **Stand up an endpoint** that accepts `POST` with `content-type: application/json` and answers
   `2xx` on success. It must send permissive CORS headers for the playground's origin, since the
   request is a cross-origin `fetch` from a static page. It receives no credentials and no custom
   header, so it cannot authenticate the sender — treat every submission as untrusted input, and
   rate-limit it.
2. **Build with the endpoint configured:**

   ```powershell
   $env:VITE_CORPUS_SINK = "https://collector.example/ingest"
   pnpm run build
   ```

   The URL must be `https:` — or `http:` on `localhost` / `127.0.0.1` for a collector on your own
   machine. Any other scheme, and any remote `http:`, is ignored: the value fails closed, so a
   typo ships the feature off rather than opening a cleartext channel.

3. **Check the built policy.** The endpoint's origin — only its origin, not its path — is added to
   the built `index.html`'s `connect-src`. If it is not there, the value was not admissible, and
   the app will show no contribution control either.
4. **Tell your visitors.** This page describes what the build sends. If you deploy a build with a
   collector configured, you are the operator of that collection, and the disclosure is yours to
   make.

## See also

- [`SECURITY.md`](../SECURITY.md) — the threat model, and the key-handling guarantees this feature
  is held to.
- [`test/corpusSink.test.ts`](../test/corpusSink.test.ts) — the guard.
- [`src/corpus/sink.ts`](../src/corpus/sink.ts) — the endpoint's admissibility rule, shared by the
  build and the app so the policy and the code cannot drift apart.

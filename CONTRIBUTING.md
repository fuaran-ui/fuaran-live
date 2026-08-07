# Contributing to `fuaran-live`

Thanks for your interest. This one is a little different from most Apache-2.0 projects, so
the short version up front:

> **`fuaran-live` does not accept external code contributions.** Bug reports are very
> welcome. Pull requests will be closed unsolicited — please don't spend your time on one.

The code is Apache-2.0 and you are free to fork it, run it, modify it, and build on it. See
[Forking](#forking) below, and [`TRADEMARK.md`](TRADEMARK.md) for the one thing the licence
does not grant you.

## Why no pull requests

Not because contributions aren't valued — because accepting them honestly costs more than
this particular project can carry.

`fuaran-live` is a **reference playground**, not a general-purpose application. Its job is to
demonstrate one thing precisely: that a model can emit canonical Fuaran wire-format JSON and a
conformant host renders it live, with no account and no server. That mission is narrow on
purpose, and most of what a healthy contribution process produces — features, options,
integrations — makes the demonstration worse rather than better.

It is also maintained by one person. A project that accepts PRs owes contributors timely
review, a stable contribution API, and a clear yes/no on scope. Pretending to offer that and
then leaving PRs to rot for months is worse for you than saying no now.

So: the door is shut deliberately and stated plainly, rather than left ajar and unattended.

## What is genuinely useful

- **Bug reports.** Open an issue. The most valuable ones concern the key-handling guarantees
  in [`SECURITY.md`](SECURITY.md) — anything that could route your API key to storage, a log,
  or a non-provider origin. Please read the threat model first; it documents the known
  residual risks (malicious extension, XSS, supply chain) so you can tell a gap from a
  documented limitation.
- **Security issues.** Follow [`SECURITY.md`](SECURITY.md) — use the repository's private
  security-advisory channel for anything sensitive rather than a public issue.
- **Wire-format problems.** If a model emits JSON that _should_ be valid and the playground
  rejects it — or one that should be rejected and it renders — that's a finding about the
  language, not about this app. Report it against the language repositories in the
  [`fuaran-ui`](https://github.com/fuaran-ui) organisation, where the spec and the conformance
  corpus live. Those are the projects where contributions shape the standard.
- **Telling us it's confusing.** If the thirty-second first run doesn't land, that's a real
  bug in a demo. Say so.

## One rule, if you are working in a fork

Worth stating even though PRs are closed, because it is the rule this project
learned the hard way and a fork inherits the same documents:

> **A claim that names a test cites a test that runs in CI. A claim that names a
> mitigation cites code that exists.** Otherwise the claim goes.

[`SECURITY.md`](SECURITY.md) once described an egress test that did not exist and
a scrub that had never been written. Prose has no build step, so nothing failed
when those stopped being true — and a security document is the one place where an
unbacked sentence actively does harm, because it is what a reviewer checks
_instead of_ checking the code. The full statement of the rule is in
[`SECURITY.md`](SECURITY.md#the-rule-this-document-is-held-to).

## Forking

Fork it. Apache-2.0 means you may use, modify, and redistribute the source, including
commercially, provided you keep the licence and attribution notices intact — including those
for the vendored third-party code noted in [`NOTICE`](NOTICE).

Two practical notes:

1. **Rebrand your fork.** The licence covers the code, not the name. See
   [`TRADEMARK.md`](TRADEMARK.md) — it's short and it is not trying to catch you out.
2. **Nothing here is a stable API.** The playground pins the public `@fuaran-ui/*` packages
   and tracks them closely; internals change without notice or deprecation. If you want a
   stable surface to build against, depend on the published packages directly rather than on
   this app's internals.

## If you were hoping to contribute

The parts of this project that _do_ take contributions are the language and host
implementations in the [`fuaran-ui`](https://github.com/fuaran-ui) organisation — the wire
format, the conformance corpus, and the host tiers. They use Developer Certificate of Origin
sign-off and they welcome PRs. That's where contributions have leverage; this playground is
downstream of all of it.

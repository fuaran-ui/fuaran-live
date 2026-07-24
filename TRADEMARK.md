# Trademark and brand

**Short version: the code is yours to use. The name isn't.**

`fuaran-live` is licensed under [Apache-2.0](LICENSE), which grants you broad rights to the
**code** — use, modify, redistribute, commercially or otherwise. Like every Apache-2.0
licence, it does **not** grant rights to trade marks, names, or logos (see section 6 of the
licence text).

So this note isn't an extra restriction bolted on top. It's a plain-English statement of
what the licence already does and doesn't cover, because "can I call my fork this?" is a
fair question and the licence text answers it in legalese.

"Fuaran", the `fuaran-*` project names, and the associated logos and visual identity are
marks of Diametrical Ltd.

## Yes, without asking

- **Say what your thing is.** "Built with Fuaran", "renders the Fuaran wire format",
  "compatible with `@fuaran-ui/renderer`", "a Fuaran playground". Accurate, factual
  references to the project are exactly what the name is for.
- **Fork it and run it** — privately, internally, at your company, on your own domain.
- **Write about it.** Tutorials, videos, courses, conference talks, criticism, comparisons
  against alternatives. You don't need permission, and you don't need to be nice about it.
- **Use the name in package metadata** where it's factually describing a dependency
  relationship.

## Please don't, without asking

- **Name your fork or product "Fuaran"** (or a confusable variant) in a way that suggests it
  _is_ this project — package names, repository names, domain names, app-store listings.
- **Imply it's official** — "the official Fuaran playground", Diametrical branding, or
  presenting a modified build as the canonical one.
- **Use the logo or visual identity** as your own product's branding.

The line is **passing off**, not use. If a reasonable person could come away thinking your
thing is our thing — or that we made it, endorsed it, or support it — that's the case we'd
ask you to change. Everything else is fine.

## Why this exists

A hosted playground is unusually easy to clone and re-brand: it's a static site, and the
whole point is that anyone can run it. That's a feature, and copies genuinely help — they
spread the wire format, which is the standard we want adopted.

What copies shouldn't do is create confusion about **which build is canonical** and who is
answerable when a key-handling guarantee is broken. `fuaran-live` makes specific, testable
promises about how your API key is handled ([`SECURITY.md`](SECURITY.md)). A modified fork
under the same name inherits the trust attached to those promises without inheriting the
code that keeps them. Keeping the name distinct is how a user can tell whose promise they're
relying on.

That's the entire concern. It isn't a lever for controlling who uses the software or what
they use it for — the Apache licence settles that, and we're not walking it back.

## Asking

If you want to do something the middle section rules out, just ask —
**andrew@fuaran.com**. Reasonable requests get a yes; it's a short conversation, not a
legal process.

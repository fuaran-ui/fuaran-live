# Promotion captures

`scripts/capture-exhibits.mjs` drives every showcase exhibit in a real browser and writes one
full-page PNG each. This file records what those captures **are** — because the question had two
answers on record and a session that took the wrong one would have committed eleven large binaries
into a public repo — and how to produce a set somebody can review.

## The decision: a capture is a build product (Phase 1702)

The residue that first asked for these images said "commit them where the script says". The script's
own header says the opposite: output goes to `dist-showcase/captures/`, which is gitignored, because
a capture is a build product and not a source.

**Build product wins, and the script's reason is the reason.** A screenshot in git is wrong the
first moment anybody edits the page it depicts, and nothing says so — it just quietly starts
advertising a version of the site that no longer exists. Every other way of being wrong announces
itself; this one does not. Three further things follow from the same fact and are worth stating
because each is sometimes offered as a counter-argument:

- **Regeneration is cheap and total.** The whole set is one command against a dev server, about
  ninety seconds. There is no partial-update problem, so there is nothing for a committed copy to
  buy.
- **The repo is public and the images are large.** Eleven full-page PNGs at 2x on a 1440px viewport
  are megabytes that every clone carries forever and that a promotion re-adds each time. Git keeps
  the superseded ones too.
- **A stale image is worse than an absent one.** An absent capture sends you to run the script. A
  stale capture is used.

## What the "commit them" ask was actually after, and what serves it

Reviewability. Someone approving a promotion needs to know what was captured, from what, and that it
matched the page. Images do not answer that — two PNGs of a page look alike whether or not the page
changed. **A manifest does**, and it is text, so it diffs:

```
PLAYWRIGHT_CHANNEL=msedge node scripts/capture-exhibits.mjs \
    --base http://localhost:24071 \
    --manifest dist-showcase/captures/manifest.json
```

The manifest names the base URL, the viewport and device scale, the colour scheme, **which browser
build produced the run**, and per exhibit its file name, byte length, full-page height and a sha256.
A reviewer holding a capture can check it against the run that produced it; a reviewer holding two
manifests can see which exhibits actually changed and which merely got recaptured.

**The manifest is written beside the images and is a build product too.** Committing one would
reintroduce exactly the staleness this decision rejects, one indirection further back: a manifest in
git describes the run that happened to be current when it landed. What is committed is the _rule_
— this file — and what travels with a promotion is the run: the images and the manifest together.

## Which browser, and why the default is what it is

The script launches Playwright's own bundled Chromium by default. That build is pinned by the
lockfile, so two people capturing on two machines get the same renderer, which is the property a
promotion asset wants.

`--channel` / `PLAYWRIGHT_CHANNEL` drives a Chromium-family browser the machine already has
(`chrome`, `msedge`, and the beta/dev variants) instead. That trades the pinning for being able to
run at all — the right trade on a machine that cannot download the binaries, the wrong one on a
machine that can. It is opt-in for that reason, and the channel used is recorded in the manifest
rather than left to be inferred from who ran it.

## Producing a set

```
pnpm install --frozen-lockfile
pnpm run fable:app
pnpm exec vite --port 24071 --strictPort          # one terminal

node scripts/capture-exhibits.mjs --base http://localhost:24071 \
     --manifest dist-showcase/captures/manifest.json   # another
node scripts/capture-exhibits.mjs --mobile --base http://localhost:24071   # 390x844, touch
node scripts/capture-exhibits.mjs --dark   --base http://localhost:24071
```

## Verify before you promote

A capture is only worth as much as the page under it, so the exhibits carry DOM claims — Phase 1129
recorded one per page, read out of the live DOM rather than eyeballed, and they are the thing to
re-check when a capture set is taken:

| Exhibit          | The claim the capture is worth nothing without                                                                           |
| ---------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `briefing`       | three `<track>` elements on a real `<audio>`, with `aria-label` and `controls`                                           |
| `embedded`       | three iframes: `sandbox=""`, `allow-scripts`, `allow-scripts allow-forms`                                                |
| `situation-room` | eleven elements carrying `aria-describedby`; an icon-only button with `aria-label` beside a _different_ `title` hint     |
| `intake`         | three `combobox` roles and one `input[type=color]`                                                                       |
| `bidi`           | exactly ONE `dir="ltr"` in the whole page                                                                                |
| `invoice`        | `fuaran-break-inside-avoid` x2, `fuaran-break-before-page` x1, `fuaran-grid-rows-together` + `fuaran-grid-repeat-header` |
| `roster`         | the 6 / 2 / 8 counts resolved from the grids' own State keys                                                             |
| `catalog`        | the carousel advances through four cases unaided and stays `running`; one interaction latches it `stopped`, one-way      |
| `outline`        | `role=tree`, ten `treeitem`s, `aria-level` 1/2/3/3/3/2, exactly ONE tabbable                                             |
| `handover`       | writing the state key moves the bound read-out to `INC-9999` while the literal stays `INC-4471`                          |
| `attach`         | five file inputs, one `capture="environment"`, one `capture="user"`, a shell naming `claims-intake`                      |

All eleven were re-verified in Edge on 2026-09-12 (Phase 1702) and all eleven hold.

**Three of those probes were written wrong the first time and passed a false failure**, which is
worth more to the next person than the green result: the `situation-room` hint reaches its button as
`title`, not as an `aria-describedby` tooltip element, so demanding both attributes on one element
fails a page that is correct; `data-fuaran-switch-state` on `catalog` is the run/stop LATCH and not
the selected case, and **hovering the stage suspends the timer**, so a probe that leaves the pointer
where it landed measures a carousel it has itself paused; and `handover`'s read-out only moves once
the state key is written, so reading the page at rest measures the default. Check what a probe
measured before believing it, in both directions.

## The 375px sweep

The same run sweeps all eleven exhibits at 375px for horizontal overflow. On 2026-09-12 exactly one
overflowed — `situation-room`, by 7px, from a hidden `.fuaran-tooltip` — which is the defect Phase
1129 filed and Phase 1702 fixed in the reference stylesheet.

**That page will keep overflowing here until a release carries the fix.** This app renders through
the published `@fuaran-ui/renderer` stylesheet, not the reference sheet in the sibling checkout, so
the fix reaches this surface when that package publishes a version carrying it and this repo raises
its pin — not when the sheet changes. Proven rather than assumed: injecting the fixed rules into the
live page takes `documentElement.scrollWidth` from 382 to 375 with the hint still revealing at
320x88.

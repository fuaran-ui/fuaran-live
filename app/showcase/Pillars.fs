module Fuaran.Showcase.Pillars

// ============================================================================
//  The four pillars the site groups its demos by, and the demo registry.
//
//  The site's accumulating argument is that these are not N features but one
//  design decision – UI as typed data – paying out N times. The navigation
//  groups by pillar so that argument is legible. Adding a demo page is one entry
//  in `demos` below + its page function in Pages.fs.
// ============================================================================

open Fable.Core.JsInterop

/// The playground lives on its own origin (D8/D10 – one repo, two origins).
/// Prefer the build-time setting; fall back to the canonical permalink domain
/// when unset (dev: both entries on one Vite server, so the domain is the honest
/// target for a real cross-door hop). Defined here rather than in `Pages` because
/// pages compiled ahead of `Pages` link across too — the Navigator page's handoff.
let playgroundOrigin: string =
  let configured: string =
    emitJsExpr () "((import.meta.env && import.meta.env.VITE_PLAYGROUND_ORIGIN) || '')"

  if configured = "" then
    "https://fuaran-ui.live"
  else
    configured

[<RequireQualifiedAccess>]
type Pillar =
  | Value
  | Wire
  | Machine
  | Intent

let allPillars = [ Pillar.Value; Pillar.Wire; Pillar.Machine; Pillar.Intent ]

let pillarSlug (p: Pillar) : string =
  match p with
  | Pillar.Value -> "value"
  | Pillar.Wire -> "wire"
  | Pillar.Machine -> "machine"
  | Pillar.Intent -> "intent"

let pillarBySlug (slug: string) : Pillar option =
  allPillars |> List.tryFind (fun p -> pillarSlug p = slug)

let pillarTitle (p: Pillar) : string =
  match p with
  | Pillar.Value -> "The app is a value"
  | Pillar.Wire -> "One wire, many worlds"
  | Pillar.Machine -> "The machine can see the UI"
  | Pillar.Intent -> "Intent, not implementation"

let pillarBlurb (p: Pillar) : string =
  match p with
  | Pillar.Value ->
    "History, branching, provenance, portability of the artefact – an app that is data can be scrubbed, forked, notarised, and teleported."
  | Pillar.Wire ->
    "The same bytes across languages, runtimes, and render targets – a single wire format, many conformant hosts."
  | Pillar.Machine ->
    "Introspection, assertions, self-repair, default-deny safety – the interface is structured data a machine can read, not pixels it must guess at."
  | Pillar.Intent ->
    "Semantic styling, observable accessibility, grammar-constrained emission – you declare intent; the substrate resolves the implementation."

/// One demo page's registry entry. `ReplayId` names the recorded artefact the
/// page's keyless mode loads through the shared replay-loader seam (None until a
/// recording exists); `Live` marks whether the full page has shipped versus a
/// coming-soon stub in the shell.
type Demo =
  { Id: string
    Title: string
    Pillar: Pillar
    Wow: string
    Route: string
    ReplayId: string option
    Live: bool }

/// The demo registry. The shell ships one representative stub per pillar so the
/// site launches with its full argument shape; the pages themselves land
/// page-by-page.
let demos: Demo list =
  [ { Id = "teleport"
      Title = "Teleport"
      Pillar = Pillar.Value
      Wow = "Fill in an app on your laptop, scan a QR, keep going on your phone – mid-interaction."
      Route = "teleport"
      ReplayId = None
      Live = true }
    { Id = "time-machine"
      Title = "The Time Machine"
      Pillar = Pillar.Value
      Wow = "Scrub the app's life like video, and fork any frame."
      Route = "time-machine"
      ReplayId = Some "time-machine"
      Live = true }
    { Id = "rosetta"
      Title = "Rosetta"
      Pillar = Pillar.Wire
      Wow =
        "Nine host languages side by side – F#, C#, Visual Basic, TypeScript, Python, Go, Rust, Swift and Kotlin – the same bytes on the wire. See also: Attesor, the reverse direction."
      Route = "rosetta"
      ReplayId = None
      Live = true }
    { Id = "attesor"
      Title = "Attesor"
      Pillar = Pillar.Wire
      Wow =
        "The reverse of Rosetta: paste one wire and read it back as idiomatic source in all nine host languages – plus the app it renders. See also: Rosetta, the forward direction."
      Route = "attesor"
      ReplayId = None
      Live = true }
    { Id = "versioning"
      Title = "The Versioning Envelope"
      Pillar = Pillar.Wire
      Wow =
        "One artefact, three schema versions – the old host degrades and preserves; a breaking version is refused, never mis-read."
      Route = "versioning"
      ReplayId = None
      Live = true }
    { Id = "kintsugi"
      Title = "Kintsugi"
      Pillar = Pillar.Machine
      Wow = "Sabotage the interface; watch it get healed – from structure, not screenshots."
      Route = "kintsugi"
      ReplayId = None
      Live = true }
    { Id = "infinite-skins"
      Title = "Infinite Skins"
      Pillar = Pillar.Intent
      Wow = "One tree across five design systems, with a live contrast auditor."
      Route = "infinite-skins"
      ReplayId = None
      Live = true }
    { Id = "every-screen"
      Title = "Every Screen"
      Pillar = Pillar.Intent
      Wow = "One tree at phone, tablet, and desktop – reflowing itself, with zero media queries written."
      Route = "every-screen"
      ReplayId = None
      Live = true }
    { Id = "blind-surveyor"
      Title = "The Blind Surveyor"
      Pillar = Pillar.Machine
      Wow = "Black out the screen; the machine still knows what overflows on a phone – layout is read, not looked at."
      Route = "blind-surveyor"
      ReplayId = None
      Live = true }
    { Id = "notarised"
      Title = "The Notarised Dashboard"
      Pillar = Pillar.Value
      Wow = "Click any element for its provenance; try to tamper with history and watch the hash chain catch you."
      Route = "notarised"
      ReplayId = None
      Live = true }
    { Id = "unit-test"
      Title = "Unit-Test Your UI"
      Pillar = Pillar.Machine
      Wow =
        "Assertions run against the living UI in microseconds; restyle the whole app and they stay green – they test structure, not pixels."
      Route = "unit-test"
      ReplayId = None
      Live = true }
    { Id = "git-for-interfaces"
      Title = "Git for Interfaces"
      Pillar = Pillar.Value
      Wow =
        "Two assistants edit one app on separate branches; a real three-way merge lands both – and you win the conflict."
      Route = "git-for-interfaces"
      ReplayId = None
      Live = true }
    { Id = "bouncer"
      Title = "The Bouncer"
      Pillar = Pillar.Machine
      Wow =
        "Try to make the interface do something malicious; watch every attempt bounce off the structural gate, with the reason shown."
      Route = "bouncer"
      ReplayId = None
      Live = true }
    { Id = "degradation"
      Title = "The Degradation Ladder"
      Pillar = Pillar.Wire
      Wow =
        "Turn JavaScript off – nothing white-screens; the same tree degrades tier by tier, deterministic on the wire."
      Route = "degradation"
      ReplayId = None
      Live = true }
    { Id = "pandas"
      Title = "The Pandas Dashboard"
      Pillar = Pillar.Wire
      Wow =
        "Four lines of Python in the browser – real pandas – and a serverless interactive dashboard appears, patched by ops on re-run."
      Route = "pandas"
      ReplayId = None
      Live = true }
    { Id = "send-me"
      Title = "Send Me That App"
      Pillar = Pillar.Wire
      Wow = "One artefact, three ways: a crawlable document, an email-safe digest, and the live app – zero forks."
      Route = "send-me"
      ReplayId = None
      Live = true }
    { Id = "grep-apps"
      Title = "Grep Your Apps"
      Pillar = Pillar.Value
      Wow = "SQL a database of applications – the search results are the running apps, matched nodes glowing."
      Route = "grep-apps"
      ReplayId = None
      Live = true }
    { Id = "what-if"
      Title = "The What-If Machine"
      Pillar = Pillar.Value
      Wow =
        "Ask a what-if and parallel universes of your plan open side by side – each a live branch; adopt the one you like."
      Route = "what-if"
      ReplayId = None
      Live = true }
    { Id = "bazaar"
      Title = "The Bazaar"
      Pillar = Pillar.Value
      Wow =
        "Mount apps into a workspace out of a marketplace – each sandboxed, capability-gated, live; the composition is itself an app."
      Route = "bazaar"
      ReplayId = None
      Live = true }
    { Id = "relay"
      Title = "The Relay"
      Pillar = Pillar.Wire
      Wow =
        "One app crosses four runtimes – Python, TypeScript, .NET, server – and arrives with a verified, unbroken hash chain."
      Route = "relay"
      ReplayId = None
      Live = true }
    { Id = "living-sheet"
      Title = "The Living Sheet"
      Pillar = Pillar.Wire
      Wow =
        "Every number is computed live by a transform pipeline that is itself data on the wire – edit an input, watch it recompute; open the wire, the formulas are right there."
      Route = "living-sheet"
      ReplayId = None
      Live = true }
    { Id = "counterfactual"
      Title = "The Counterfactual Corner"
      Pillar = Pillar.Value
      Wow =
        "Ask “what if?” and parallel universes of your app open side by side – each a live isolated branch; adopt the ones you like and a real merge folds them together."
      Route = "counterfactual"
      ReplayId = None
      Live = true }
    { Id = "pattern-bank"
      Title = "The Pattern Bank"
      Pillar = Pillar.Machine
      Wow =
        "Describe the shape you want and a known-good app pattern resolves instantly – a real structural search, no model call, no server, zero latency."
      Route = "pattern-bank"
      ReplayId = None
      Live = true }
    { Id = "chart-as-data"
      Title = "Chart-as-data"
      Pillar = Pillar.Value
      Wow =
        "A chart is usually an opaque PNG. Here it is data – rendered as inline SVG with no charting library, and so notarisable, diffable, portable, and interactive."
      Route = "chart-as-data"
      ReplayId = None
      Live = true }
    { Id = "typed-question"
      Title = "The Typed Question"
      Pillar = Pillar.Machine
      Wow =
        "An agent asks you a question as a live form – and gets back a typed, contract-checked answer, never prose. Try to cheat the contract; every trick is refused, with the reason."
      Route = "typed-question"
      ReplayId = None
      Live = true }
    { Id = "hand-on-the-wheel"
      Title = "Hand on the Wheel"
      Pillar = Pillar.Machine
      Wow =
        "A module declares which of its fields an agent may set – by name, each with a typed space – and the agent turns the knobs directly, never guessing at pixels. Out-of-range and undeclared names bounce, with the reason."
      Route = "hand-on-the-wheel"
      ReplayId = None
      Live = true }
    { Id = "go-sessions"
      Title = "Go Sessions – bring your own server"
      Pillar = Pillar.Wire
      Wow =
        "The same page, a new key: BYOK becomes BYOS. Play a recorded Go-server session with zero setup, or run one Go binary and drive the live session from the browser – validator reject and last-good-tree included."
      Route = "go-sessions"
      ReplayId = Some "go-sessions"
      Live = true }
    { Id = "navigator"
      Title = "The Navigator"
      Pillar = Pillar.Machine
      Wow =
        "Edit a running app through its own wire format: walk it with a cursor, retitle a button, resize a heading, undo — and watch each action turn into the operation it actually is, in canonical bytes."
      Route = "navigator"
      ReplayId = None
      Live = true }
    { Id = "agent-readable"
      Title = "The Agent-Readable Page"
      Pillar = Pillar.Machine
      Wow =
        "A page that advertises its own natural-language affordances – the phrases it understands, the synonyms it resolves, the values it accepts – and a live pane showing exactly what a machine reading it gets back."
      Route = "agent-readable"
      ReplayId = None
      Live = true }
    { Id = "locale-lens"
      Title = "The Locale Lens"
      Pillar = Pillar.Intent
      Wow =
        "One instant – a single epoch number on the wire – rendered in New York, Cairo, Tokyo and Bangkok at once: different words, orders, digits, even different years. The data never changes; the renderer owns the locale."
      Route = "locale-lens"
      ReplayId = None
      Live = true }
    // ── The platform-baseline exhibits (Phase 1129) ─────────────────────────
    { Id = "briefing"
      Title = "The Briefing"
      Pillar = Pillar.Intent
      Wow =
        "Play it or read it — it is one node either way. Captions, subtitles, chapter marks and the full transcript all ride the same media element, so the words are on the wire whether or not anyone presses play."
      Route = "briefing"
      ReplayId = None
      Live = true }
    { Id = "invoice"
      Title = "The Invoice"
      Pillar = Pillar.Value
      Wow =
        "A document that says which of its own parts are indivisible — and nothing at all about paper. Press print and four declarations take effect that were invisible a moment before."
      Route = "invoice"
      ReplayId = None
      Live = true }
    { Id = "roster"
      Title = "The Roster Board"
      Pillar = Pillar.Value
      Wow =
        "Two grids exchange rows because they name the same channel; a third, identical and adjacent, cannot — because adjacency is layout and the permission is a name. And the rows are yours to take away."
      Route = "roster"
      ReplayId = None
      Live = true }
    { Id = "catalog"
      Title = "The Catalogue"
      Pillar = Pillar.Intent
      Wow =
        "A carousel from one number on the wire. Swipe, arrow keys, pause on hover and a one-way stop the moment you take control — none of which the document says, and all of which it gets."
      Route = "catalog"
      ReplayId = None
      Live = true }
    { Id = "outline"
      Title = "The Outline"
      Pillar = Pillar.Machine
      Wow =
        "A hierarchy that is ONE tab stop and six keys — beside the disclosure composition it is not, so you can feel the difference that made it a kind rather than read about it."
      Route = "outline"
      ReplayId = None
      Live = true }
    { Id = "handover"
      Title = "The Handover"
      Pillar = Pillar.Intent
      Wow =
        "Edit a field, press copy, and get what you are LOOKING at — because the payload is a binding that resolves when you press it. Beside it, the literal that hands you yesterday's value."
      Route = "handover"
      ReplayId = None
      Live = true }
    { Id = "attach"
      Title = "The Attachment"
      Pillar = Pillar.Intent
      Wow =
        "One file control four ways — drop, paste, camera, microphone — each an independent declaration, all off by default. Plus one that names a destination this host cannot serve, and refuses rather than pretends."
      Route = "attach"
      ReplayId = None
      Live = true }
    { Id = "situation-room"
      Title = "The Situation Room"
      Pillar = Pillar.Machine
      Wow =
        "Eleven payment numbers, eleven definitions two teams have argued about, none of which fits in a label — so each one carries its own hint, on the node, on the wire, and in the accessibility tree."
      Route = "situation-room"
      ReplayId = None
      Live = true }
    { Id = "intake"
      Title = "The Intake Form"
      Pillar = Pillar.Intent
      Wow =
        "A closed list you can type into, an open one you can add to, several values in one control, a bounded rating and a colour — the ordinary form a model reaches for, which until this release it had to fake four times over."
      Route = "intake"
      ReplayId = None
      Live = true }
    { Id = "bidi"
      Title = "Right to Left"
      Pillar = Pillar.Intent
      Wow =
        "An Arabic invoice with an English reference on it. The bidirectional algorithm gets almost all of it right unaided — and exactly one thing wrong, every time, which one slot on one node fixes."
      Route = "bidi"
      ReplayId = None
      Live = true }
    { Id = "embedded"
      Title = "Embedded"
      Pillar = Pillar.Machine
      Wow =
        "One guest document framed three times with three different relaxations, reporting on itself — watch the sandbox bite. An embed that asks for nothing gets nothing: the wire-cheapest document is the safest one."
      Route = "embedded"
      ReplayId = None
      Live = true } ]

let demosInPillar (p: Pillar) : Demo list =
  demos |> List.filter (fun d -> d.Pillar = p)

let demoByRoute (route: string) : Demo option =
  demos |> List.tryFind (fun d -> d.Route = route)

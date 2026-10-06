module Fuaran.Showcase.Registry

// ============================================================================
//  The page registry: one entry per showcase page, carrying the page itself.
//
//  Everything the shell knows about a page derives from its entry here — the
//  route (`#/demo/<Id>`), the pillar navigation, the index cards on each pillar
//  page, and the page body. Adding a showcase page is one entry below and one
//  page file; the project file compiles every page file it does not name
//  explicitly, so there is no third place to edit.
//
//  The registry sits AFTER the page modules because it references each page's
//  value, and F# compile order requires a definition before its use. It does
//  not import them statically: `Load` is a dynamic import, so each page is its
//  own chunk, fetched the first time a visitor opens it. The landing page pays
//  for none of them.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Showcase.Pillars

/// One page's entry. `Wow` is the one-line promise its index card makes;
/// `Load` fetches the page's chunk and yields the page.
type Page =
  { Id: string
    Title: string
    Pillar: Pillar
    Wow: string
    Load: unit -> JS.Promise<ReactElement> }

/// The registry, in index order within each pillar.
let pages: Page list =
  [ { Id = "teleport"
      Title = "Teleport"
      Pillar = Pillar.Value
      Wow = "Fill in an app on your laptop, scan a QR, keep going on your phone – mid-interaction."
      Load = fun () -> importValueDynamic Teleport.page }
    { Id = "time-machine"
      Title = "The Time Machine"
      Pillar = Pillar.Value
      Wow = "Scrub the app's life like video, and fork any frame."
      Load = fun () -> importValueDynamic TimeMachine.page }
    { Id = "rosetta"
      Title = "Rosetta"
      Pillar = Pillar.Wire
      Wow =
        "Nine host languages side by side – F#, C#, Visual Basic, TypeScript, Python, Go, Rust, Swift and Kotlin – the same bytes on the wire. See also: Attesor, the reverse direction."
      Load = fun () -> importValueDynamic Rosetta.page }
    { Id = "attesor"
      Title = "Attesor"
      Pillar = Pillar.Wire
      Wow =
        "The reverse of Rosetta: paste one wire and read it back as idiomatic source in all nine host languages – plus the app it renders. See also: Rosetta, the forward direction."
      Load = fun () -> importValueDynamic Attesor.page }
    { Id = "versioning"
      Title = "The Versioning Envelope"
      Pillar = Pillar.Wire
      Wow =
        "One artefact, three schema versions – the old host degrades and preserves; a breaking version is refused, never mis-read."
      Load = fun () -> importValueDynamic WireVersioning.page }
    { Id = "kintsugi"
      Title = "Kintsugi"
      Pillar = Pillar.Machine
      Wow = "Sabotage the interface; watch it get healed – from structure, not screenshots."
      Load = fun () -> importValueDynamic Kintsugi.page }
    { Id = "infinite-skins"
      Title = "Infinite Skins"
      Pillar = Pillar.Intent
      Wow = "One tree across five design systems, with a live contrast auditor."
      Load = fun () -> importValueDynamic Skins.page }
    { Id = "every-screen"
      Title = "Every Screen"
      Pillar = Pillar.Intent
      Wow = "One tree at phone, tablet, and desktop – reflowing itself, with zero media queries written."
      Load = fun () -> importValueDynamic Responsive.page }
    { Id = "blind-surveyor"
      Title = "The Blind Surveyor"
      Pillar = Pillar.Machine
      Wow = "Black out the screen; the machine still knows what overflows on a phone – layout is read, not looked at."
      Load = fun () -> importValueDynamic BlindSurveyor.page }
    { Id = "notarised"
      Title = "The Notarised Dashboard"
      Pillar = Pillar.Value
      Wow = "Click any element for its provenance; try to tamper with history and watch the hash chain catch you."
      Load = fun () -> importValueDynamic Notarised.page }
    { Id = "unit-test"
      Title = "Unit-Test Your UI"
      Pillar = Pillar.Machine
      Wow =
        "Assertions run against the living UI in microseconds; restyle the whole app and they stay green – they test structure, not pixels."
      Load = fun () -> importValueDynamic UnitTest.page }
    { Id = "git-for-interfaces"
      Title = "Git for Interfaces"
      Pillar = Pillar.Value
      Wow =
        "Two assistants edit one app on separate branches; a real three-way merge lands both – and you win the conflict."
      Load = fun () -> importValueDynamic GitForInterfaces.page }
    { Id = "bouncer"
      Title = "The Bouncer"
      Pillar = Pillar.Machine
      Wow =
        "Try to make the interface do something malicious; watch every attempt bounce off the structural gate, with the reason shown."
      Load = fun () -> importValueDynamic Bouncer.page }
    { Id = "degradation"
      Title = "The Degradation Ladder"
      Pillar = Pillar.Wire
      Wow =
        "Turn JavaScript off – nothing white-screens; the same tree degrades tier by tier, deterministic on the wire."
      Load = fun () -> importValueDynamic Degradation.page }
    { Id = "pandas"
      Title = "The Pandas Dashboard"
      Pillar = Pillar.Wire
      Wow =
        "Four lines of Python in the browser – real pandas – and a serverless interactive dashboard appears, patched by ops on re-run."
      Load = fun () -> importValueDynamic Pandas.page }
    { Id = "send-me"
      Title = "Send Me That App"
      Pillar = Pillar.Wire
      Wow = "One artefact, three ways: a crawlable document, an email-safe digest, and the live app – zero forks."
      Load = fun () -> importValueDynamic Send.page }
    { Id = "grep-apps"
      Title = "Grep Your Apps"
      Pillar = Pillar.Value
      Wow = "SQL a database of applications – the search results are the running apps, matched nodes glowing."
      Load = fun () -> importValueDynamic GrepApps.page }
    { Id = "what-if"
      Title = "The What-If Machine"
      Pillar = Pillar.Value
      Wow =
        "Ask a what-if and parallel universes of your plan open side by side – each a live branch; adopt the one you like."
      Load = fun () -> importValueDynamic WhatIf.page }
    { Id = "bazaar"
      Title = "The Bazaar"
      Pillar = Pillar.Value
      Wow =
        "Mount apps into a workspace out of a marketplace – each sandboxed, capability-gated, live; the composition is itself an app."
      Load = fun () -> importValueDynamic Bazaar.page }
    { Id = "relay"
      Title = "The Relay"
      Pillar = Pillar.Wire
      Wow =
        "One app crosses four runtimes – Python, TypeScript, .NET, server – and arrives with a verified, unbroken hash chain."
      Load = fun () -> importValueDynamic Relay.page }
    { Id = "living-sheet"
      Title = "The Living Sheet"
      Pillar = Pillar.Wire
      Wow =
        "Every number is computed live by a transform pipeline that is itself data on the wire – edit an input, watch it recompute; open the wire, the formulas are right there."
      Load = fun () -> importValueDynamic LivingSheet.page }
    { Id = "counterfactual"
      Title = "The Counterfactual Corner"
      Pillar = Pillar.Value
      Wow =
        "Ask “what if?” and parallel universes of your app open side by side – each a live isolated branch; adopt the ones you like and a real merge folds them together."
      Load = fun () -> importValueDynamic Counterfactual.page }
    { Id = "pattern-bank"
      Title = "The Pattern Bank"
      Pillar = Pillar.Machine
      Wow =
        "Describe the shape you want and a known-good app pattern resolves instantly – a real structural search, no model call, no server, zero latency."
      Load = fun () -> importValueDynamic PatternBank.page }
    { Id = "chart-as-data"
      Title = "Chart-as-data"
      Pillar = Pillar.Value
      Wow =
        "A chart is usually an opaque PNG. Here it is data – rendered as inline SVG with no charting library, and so notarisable, diffable, portable, and interactive."
      Load = fun () -> importValueDynamic Charts.page }
    { Id = "typed-question"
      Title = "The Typed Question"
      Pillar = Pillar.Machine
      Wow =
        "An agent asks you a question as a live form – and gets back a typed, contract-checked answer, never prose. Try to cheat the contract; every trick is refused, with the reason."
      Load = fun () -> importValueDynamic TypedQuestion.page }
    { Id = "hand-on-the-wheel"
      Title = "Hand on the Wheel"
      Pillar = Pillar.Machine
      Wow =
        "A module declares which of its fields an agent may set – by name, each with a typed space – and the agent turns the knobs directly, never guessing at pixels. Out-of-range and undeclared names bounce, with the reason."
      Load = fun () -> importValueDynamic HandOnTheWheel.page }
    { Id = "go-sessions"
      Title = "Go Sessions – bring your own server"
      Pillar = Pillar.Wire
      Wow =
        "The same page, a new key: BYOK becomes BYOS. Play a recorded Go-server session with zero setup, or run one Go binary and drive the live session from the browser – validator reject and last-good-tree included."
      Load = fun () -> importValueDynamic GoSessions.page }
    { Id = "navigator"
      Title = "The Navigator"
      Pillar = Pillar.Machine
      Wow =
        "Edit a running app through its own wire format: walk it with a cursor, retitle a button, resize a heading, undo — and watch each action turn into the operation it actually is, in canonical bytes."
      Load = fun () -> importValueDynamic Navigator.page }
    { Id = "agent-readable"
      Title = "The Agent-Readable Page"
      Pillar = Pillar.Machine
      Wow =
        "A page that advertises its own natural-language affordances – the phrases it understands, the synonyms it resolves, the values it accepts – and a live pane showing exactly what a machine reading it gets back."
      Load = fun () -> importValueDynamic AgentReadable.page }
    { Id = "locale-lens"
      Title = "The Locale Lens"
      Pillar = Pillar.Intent
      Wow =
        "One instant – a single epoch number on the wire – rendered in New York, Cairo, Tokyo and Bangkok at once: different words, orders, digits, even different years. The data never changes; the renderer owns the locale."
      Load = fun () -> importValueDynamic LocaleLens.page }
    // ── The platform-baseline exhibits (Phase 1129) ─────────────────────────
    { Id = "briefing"
      Title = "The Briefing"
      Pillar = Pillar.Intent
      Wow =
        "Play it or read it — it is one node either way. Captions, subtitles, chapter marks and the full transcript all ride the same media element, so the words are on the wire whether or not anyone presses play."
      Load = fun () -> importValueDynamic Briefing.page }
    { Id = "invoice"
      Title = "The Invoice"
      Pillar = Pillar.Value
      Wow =
        "A document that says which of its own parts are indivisible — and nothing at all about paper. Press print and four declarations take effect that were invisible a moment before."
      Load = fun () -> importValueDynamic Invoice.page }
    { Id = "roster"
      Title = "The Roster Board"
      Pillar = Pillar.Value
      Wow =
        "Two grids exchange rows because they name the same channel; a third, identical and adjacent, cannot — because adjacency is layout and the permission is a name. And the rows are yours to take away."
      Load = fun () -> importValueDynamic Roster.page }
    { Id = "catalog"
      Title = "The Catalogue"
      Pillar = Pillar.Intent
      Wow =
        "A carousel from one number on the wire. Swipe, arrow keys, pause on hover and a one-way stop the moment you take control — none of which the document says, and all of which it gets."
      Load = fun () -> importValueDynamic Catalog.page }
    { Id = "outline"
      Title = "The Outline"
      Pillar = Pillar.Machine
      Wow =
        "A hierarchy that is ONE tab stop and six keys — beside the disclosure composition it is not, so you can feel the difference that made it a kind rather than read about it."
      Load = fun () -> importValueDynamic Outline.page }
    { Id = "handover"
      Title = "The Handover"
      Pillar = Pillar.Intent
      Wow =
        "Edit a field, press copy, and get what you are LOOKING at — because the payload is a binding that resolves when you press it. Beside it, the literal that hands you yesterday's value."
      Load = fun () -> importValueDynamic Handover.page }
    { Id = "attach"
      Title = "The Attachment"
      Pillar = Pillar.Intent
      Wow =
        "One file control four ways — drop, paste, camera, microphone — each an independent declaration, all off by default. Plus one that names a destination this host cannot serve, and refuses rather than pretends."
      Load = fun () -> importValueDynamic Attach.page }
    { Id = "situation-room"
      Title = "The Situation Room"
      Pillar = Pillar.Machine
      Wow =
        "Eleven payment numbers, eleven definitions two teams have argued about, none of which fits in a label — so each one carries its own hint, on the node, on the wire, and in the accessibility tree."
      Load = fun () -> importValueDynamic Situation.page }
    { Id = "intake"
      Title = "The Intake Form"
      Pillar = Pillar.Intent
      Wow =
        "A closed list you can type into, an open one you can add to, several values in one control, a bounded rating and a colour — the ordinary form a model reaches for, which until this release it had to fake four times over."
      Load = fun () -> importValueDynamic Intake.page }
    { Id = "bidi"
      Title = "Right to Left"
      Pillar = Pillar.Intent
      Wow =
        "An Arabic invoice with an English reference on it. The bidirectional algorithm gets almost all of it right unaided — and exactly one thing wrong, every time, which one slot on one node fixes."
      Load = fun () -> importValueDynamic Bidi.page }
    { Id = "embedded"
      Title = "Embedded"
      Pillar = Pillar.Machine
      Wow =
        "One guest document framed three times with three different relaxations, reporting on itself — watch the sandbox bite. An embed that asks for nothing gets nothing: the wire-cheapest document is the safest one."
      Load = fun () -> importValueDynamic Embedded.page } ]

let pagesInPillar (p: Pillar) : Page list =
  pages |> List.filter (fun d -> d.Pillar = p)

let pageById (id: string) : Page option =
  pages |> List.tryFind (fun d -> d.Id = id)

/// The two destinations outside the pillar index, loaded the same way.
let loadEvaluation () : JS.Promise<ReactElement> = importValueDynamic Evaluation.page

let loadContact () : JS.Promise<ReactElement> = importValueDynamic Contact.page

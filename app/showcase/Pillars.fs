module Fuaran.Showcase.Pillars

// ============================================================================
//  The four pillars the site groups its demos by, and the demo registry.
//
//  The site's accumulating argument is that these are not N features but one
//  design decision – UI as typed data – paying out N times. The navigation
//  groups by pillar so that argument is legible. Adding a demo page is one entry
//  in `demos` below + its page function in Pages.fs.
// ============================================================================

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
    { Id = "locale-lens"
      Title = "The Locale Lens"
      Pillar = Pillar.Intent
      Wow =
        "One instant – a single epoch number on the wire – rendered in New York, Cairo, Tokyo and Bangkok at once: different words, orders, digits, even different years. The data never changes; the renderer owns the locale."
      Route = "locale-lens"
      ReplayId = None
      Live = true } ]

let demosInPillar (p: Pillar) : Demo list =
  demos |> List.filter (fun d -> d.Pillar = p)

let demoByRoute (route: string) : Demo option =
  demos |> List.tryFind (fun d -> d.Route = route)

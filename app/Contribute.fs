module Fuaran.Live.Contribute

// ============================================================================
//  CONTRIBUTE THIS SESSION — the opt-in, anonymous session-corpus sink.
//
//  A session in this playground is a small, complete artefact: a base tree, the
//  ops that carried it forward, and which model produced them. That is exactly
//  the material a corpus of "what a model emits when asked for a UI" is made of,
//  and this page is the only place it exists. So the page can OFFER to send one
//  — once, explicitly, per contribution — to a collection endpoint an operator
//  has configured.
//
//  ── The public build sends nothing, and cannot ──────────────────────────────
//
//  The endpoint is build configuration (`VITE_CORPUS_SINK`, see
//  src/corpus/sink.ts). The public build sets none, so `sinkUrl` is empty,
//  `configured` is false, App.fs renders no contribution affordance at all, and
//  the CSP gains no `connect-src` origin. There is no interaction in the shipped
//  artefact that reaches a POST. That is not a runtime check to be trusted — it
//  is the absence of a destination, visible in the shipped files.
//
//  ── Key-blind by construction ───────────────────────────────────────────────
//
//  SECURITY.md's key-handling guarantee 5 states the condition any corpus
//  feature in this repo must meet: it must be *key-blind by construction* — it
//  must never be able to observe the key value. This module is that sentence
//  built rather than promised:
//
//   • It builds from `Session.SessionState` alone, which holds a tree, an op
//     list, snapshots, the attributed log and the conversation transcript. No
//     key store is in scope here, and this module opens none. `Byok.fs` — the
//     one module holding the key stores — knows nothing of this one.
//   • The seam takes a `ContributionBundle`, a single-case type nothing but
//     `prepare` constructs (Ports.fs). The key cannot cross it because the type
//     has no case that could hold one, which is the same construction the
//     live-drive channel already uses for the same reason.
//   • `corpusSink.test.ts` asserts both structurally, against this file's own
//     source as well as against the built payload.
//
//  ── What is captured, and what is deliberately NOT ──────────────────────────
//
//  Captured: the base tree, the current folded tree, every applied op with its
//  actor and its hash-chain link, and four metadata fields — provider id, model
//  id, a capture timestamp, and the NUMBER of prompts.
//
//  Not captured: the prompts themselves, the model's prose, the panel
//  transcript. Those are free text a visitor typed, which is precisely the
//  category "nothing is stored about you" has to exclude for the sentence to be
//  true. The corpus this feeds is a corpus of UI-as-data, and the trees and ops
//  are the whole of it; the prompt COUNT is what the metadata needs and all it
//  gets. Excluding the transcript is a smaller bundle AND a smaller claim, and
//  the claim is the part that has to hold.
//
//  Also not captured: anything identifying. There is no account, no cookie, no
//  visitor id, no session id that outlives the tab, and the POST sends no
//  credentials (`credentials: 'omit'`) and no custom header. Anonymity here is
//  not a policy applied to identified data — there is no identity to withhold.
//
//  ── The guard REFUSES; it does not quietly clean ────────────────────────────
//
//  `prepare` scans the built bytes for key-shaped tokens and provider origins,
//  and REFUSES the contribution when it finds either, naming the class it found
//  (never the value). The phase this implements asked for a strip-then-assert
//  guard; refusing is the stronger reading and the one that ships, because a
//  bundle that is key-blind by construction has no legitimate reason to contain
//  key-shaped material — so a sighting is a contract violation, and silently
//  stripping one is indistinguishable from having missed one. The visitor is
//  told what was found and nothing is sent.
//
//  ── Not a wire emitter ──────────────────────────────────────────────────────
//
//  Per CLAUDE.md's emitter-lock convention: this module hand-authors no wire.
//  The trees are encoded by the real `CanonicalJson` encoder and the ops are the
//  canonical documents the session already recorded; the envelope around them is
//  a sidecar, not a wire-format document. See the exclusion entry in
//  test/emitterLocks.test.ts.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.UI.OpStream.Abstractions
open Fuaran.Live.Ports

module Canon = Fuaran.UI.OpStream.Abstractions.CanonicalJson

// ─── build configuration ─────────────────────────────────────────────────────

/// The configured collection endpoint, or `""`.
///
/// Read from the build environment, exactly as `VITE_DUAL_HOST` and
/// `VITE_SHOWCASE_ORIGIN` are. Empty in the public build — see this module's
/// header and src/corpus/sink.ts, which applies the same admissibility rule to
/// the same string when it builds the CSP, so "the CSP allows it" and "the app
/// would post to it" cannot disagree.
let sinkUrl: string =
  let raw: string =
    emitJsExpr () "((import.meta.env && import.meta.env.VITE_CORPUS_SINK) || '')"

  raw.Trim()

/// Whether an endpoint is configured at all. False in the public build, where it
/// is the reason no contribution affordance is rendered.
let configured: bool = sinkUrl <> ""

// ─── the leak guard ──────────────────────────────────────────────────────────

/// The provider origins a bundle must not contain. Mirrors `Byok.providers`'
/// `Origin` set and src/byok/origins.ts; the test asserts the three agree, so a
/// sixth provider cannot be added past this guard unnoticed.
let providerOrigins =
  [ "https://api.anthropic.com"
    "https://api.openai.com"
    "https://generativelanguage.googleapis.com"
    "https://api.moonshot.ai"
    "https://api.x.ai" ]

/// Whether the text carries a key-shaped token.
///
/// The prefixes are the five supported providers' own key formats: `sk-ant-`
/// (Anthropic), `sk-proj-` and `sk-` (OpenAI, Moonshot), `AIza` (Google),
/// `xai-` (xAI). Two details are load-bearing and were both learned by writing
/// the naive version first:
///
///  • The prefix must start a TOKEN — `(?<![A-Za-z0-9_])` — or `sk-` matches
///    inside the ordinary English word "risk-averse", and a guard that fires on
///    prose is a guard that gets switched off.
///  • It must be followed by at least 16 key characters. Every one of these
///    formats is far longer than that, and the length is what separates a
///    credential from a hyphenated word that happens to start the same way.
///
/// Regex rather than a hand-rolled scanner because the host engine's is the one
/// the rest of this repo already relies on, and this is exactly its job.
[<Emit("/(?<![A-Za-z0-9_])(sk-ant-|sk-proj-|sk-|xai-|AIza)[A-Za-z0-9_-]{16,}/.test($0)")>]
let private hasKeyShapedToken (text: string) : bool = jsNative

/// Every reason this payload must not be sent — one line per class found, in a
/// fixed order, naming the class and never the value. Empty means clean.
///
/// A finding is a refusal, not a warning: see this module's header.
let findings (json: string) : string list =
  [ if hasKeyShapedToken json then
      "a provider-API-key-shaped token"
    for origin in providerOrigins do
      if json.Contains origin then
        "a provider endpoint URL (" + origin + ")" ]

// ─── the bundle ──────────────────────────────────────────────────────────────

/// The session metadata a contribution carries. Supplied by the caller rather
/// than read here, for the reason `Session.chainTimestamp` gives about its own
/// stamp: a value this module read from the wall clock would make every test
/// disagree with every other run, and the timestamp is data about the session,
/// not about this module.
type Meta =
  {
    ProviderId: string
    ModelId: string
    /// ISO-8601 UTC, seconds precision. Coarse enough to say when the corpus
    /// entry was produced; it identifies nobody on its own, and there is no
    /// second field to join it against.
    CapturedAt: string
  }

/// The capture stamp, ISO-8601 UTC at seconds precision — the whole-second
/// truncation is deliberate: sub-second precision would be a finer join key than
/// anything in this bundle needs, and a corpus entry's usefulness does not
/// change with it.
[<Emit("new Date().toISOString().replace(/\\.\\d{3}Z$/, 'Z')")>]
let isoNowUtc () : string = jsNative

/// The bundle format's version. A collector reads this first and can then know
/// what the rest of the document means; bumping it is how the shape changes
/// without a receiver having to guess.
[<Literal>]
let formatVersion = 1

/// The document kind, so a collector can tell one of these from anything else
/// that lands on the same endpoint.
[<Literal>]
let formatKind = "fuaran-live/session-corpus"

/// One log entry's actor, flattened for the document: `"human:<surface>"` or
/// `"agent:<model>"`. The playground has no accounts, so a `Human` actor names
/// the SURFACE the edit came from (navigator / console) and never a person —
/// which is both the honest thing to record and the only thing there is.
let private actorText (actor: Actor) : string =
  match actor with
  | Actor.Human surface -> "human:" + surface
  | Actor.Agent(model, _, _) -> "agent:" + model

/// Parse a canonical JSON document into a plain JS value for embedding, so the
/// bundle is one readable document rather than a document of strings. The host
/// engine preserves member order through parse + stringify, so the canonical
/// bytes are recoverable by re-stringifying. A document that somehow will not
/// parse is embedded as its string, which is lossless and visibly odd, rather
/// than dropped.
[<Emit("(function(j){ try { return JSON.parse(j); } catch(e){ return j; } })($0)")>]
let private embed (canonJson: string) : obj = jsNative

[<Emit("JSON.stringify($0)")>]
let private stringify (value: obj) : string = jsNative

/// How many prompts the visitor sent this session — the metadata field, and the
/// only thing the transcript contributes to the bundle.
let promptCount (session: Session.SessionState) : int =
  session.History
  |> List.filter (fun turn ->
    match turn.Role with
    | User -> true
    | Assistant -> false)
  |> List.length

/// The `session.fuaran.json` document for this session, as canonical bytes.
///
/// Pure, and total: a session with no tree yields a well-formed document whose
/// trees are `null` and whose op list is empty. Whether such a session is worth
/// contributing is `prepare`'s question, not this one's.
let build (meta: Meta) (session: Session.SessionState) : string =
  let treeJson (tree: Fuaran.UI.Types.Node<obj> option) : obj =
    match tree with
    | Some t -> embed (Canon.encodeNode t)
    | None -> box null

  // The applied prefix only. An undone op is in `Log` so it can be redone in
  // the tab; it did not happen to the tree being contributed, and a corpus
  // entry that claimed it did would not replay.
  let applied = session.Log |> List.truncate (List.length session.Ops)

  let ops =
    applied
    |> List.map (fun entry ->
      createObj
        [ "seq" ==> entry.Seq
          "kind" ==> entry.OpKind
          "actor" ==> actorText entry.Actor
          "hash" ==> entry.Hash
          "prev" ==> entry.Prev
          "op" ==> embed entry.OpJson ])
    |> List.toArray

  createObj
    [ "kind" ==> formatKind
      "version" ==> formatVersion
      "capturedAt" ==> meta.CapturedAt
      "provider" ==> meta.ProviderId
      "model" ==> meta.ModelId
      "promptCount" ==> promptCount session
      // The tree the op sequence replays FROM, and the tree it arrives at. Both,
      // because either alone makes the ops uncheckable: without the base there
      // is nothing to replay against, and without the result there is nothing to
      // check the replay produced.
      "baseTree" ==> treeJson (Session.baseTree session)
      "tree" ==> treeJson session.Tree
      "ops" ==> ops ]
  |> stringify

/// Build, then guard. `Ok` carries the permission slip the sink seam demands;
/// `Error` carries the human-readable reason, which names the class found and
/// never the value.
///
/// A session with no tree is refused too — there is nothing in it to contribute,
/// and an empty corpus entry is noise a collector then has to filter.
let prepare (meta: Meta) (session: Session.SessionState) : Result<ContributionBundle, string> =
  if Option.isNone session.Tree then
    Error "There is no tree yet — generate or load one first."
  else
    let json = build meta session

    match findings json with
    | [] -> Ok(ContributionBundle.Verified json)
    | reasons ->
      Error(
        "Not sent. The session contains "
        + String.concat " and " reasons
        + ", which must never leave this page. Nothing was uploaded."
      )

// ─── the browser sink ────────────────────────────────────────────────────────

[<Emit("fetch($0, $1)")>]
let private fetchApi (url: string) (init: obj) : JS.Promise<obj> = jsNative

/// A sink posting to `endpoint`. An empty endpoint yields a sink that posts
/// nothing and says so — the public build's sink, and the reason a
/// misconfiguration fails closed instead of throwing.
///
/// The request is deliberately plain: one POST, one JSON body, no custom header,
/// `credentials: 'omit'` so no cookie or stored credential is attached (an
/// anonymous contribution that carried a cookie would not be anonymous), and
/// `redirect: 'error'` for the reason the provider transport gives — a redirect
/// would move the body to an origin the operator did not configure and the CSP
/// might still allow.
let sinkTo (endpoint: string) : IContributionSink =
  { new IContributionSink with
      member _.Endpoint = endpoint

      member _.Post(bundle) =
        async {
          match endpoint, bundle with
          | "", _ -> return ContributionOutcome.Refused "No collection endpoint is configured."
          | url, ContributionBundle.Verified json ->
            try
              let init =
                createObj
                  [ "method" ==> "POST"
                    "headers" ==> createObj [ "content-type" ==> "application/json" ]
                    "body" ==> json
                    "credentials" ==> "omit"
                    "redirect" ==> "error" ]

              let! response = fetchApi url init |> Async.AwaitPromise
              let status: int = response?status

              if status >= 200 && status < 300 then
                return ContributionOutcome.Sent
              else
                return ContributionOutcome.Failed("The collector answered " + string status + ".")
            with _ ->
              // The transport's own text. It is a string this app did not author,
              // so it is reported as a class rather than echoed — the same
              // posture the provider error path takes, and for the same reason.
              return ContributionOutcome.Failed "The collector could not be reached."
        } }

/// The configured sink. Empty-endpoint in the public build.
let browserSink: IContributionSink = sinkTo sinkUrl

// ─── the pane ────────────────────────────────────────────────────────────────

/// What the pane is showing. `Consented` is the per-contribution consent, and it
/// is reset to false after every attempt — consent is given for ONE
/// contribution, so a second one is a second decision.
type State =
  {
    Consented: bool
    /// The outcome of the most recent attempt, if any.
    Status: string
    Sending: bool
  }

let empty: State =
  { Consented = false
    Status = ""
    Sending = false }

/// Fold an outcome into the pane state, clearing consent either way.
let recorded (state: State) (outcome: ContributionOutcome) : State =
  let status =
    match outcome with
    | ContributionOutcome.Sent -> "Thank you — the session was contributed anonymously."
    | ContributionOutcome.Refused reason -> reason
    | ContributionOutcome.Failed reason -> reason

  { state with
      Consented = false
      Sending = false
      Status = status }

/// The honest copy. Written out here rather than inline in the view so the test
/// can assert the claims it makes are the claims the code keeps — in particular
/// that the transcript is named as excluded, which is the sentence a visitor
/// would most reasonably want checked.
let intro: ReactElement =
  Html.div
    [ prop.className "fl-contribute-intro"
      prop.children
        [ Html.p
            [ prop.text (
                "You can contribute this session to an anonymous corpus of Fuaran UI emissions. "
                + "It is off unless you tick the box, and each contribution is a separate decision."
              ) ]
          Html.p
            [ prop.className "fl-contribute-what"
              prop.text (
                "What is sent: the tree the model produced, the ops that edited it, and four "
                + "fields — which provider, which model, when, and how many prompts you sent."
              ) ]
          Html.p
            [ prop.className "fl-contribute-what"
              prop.text (
                "What is NOT sent: your API key, your prompts, the model's replies, and anything "
                + "identifying. There is no account, no cookie and no visitor id — there is no "
                + "identity to send. If a key-shaped value is found anywhere in the bundle, "
                + "nothing is uploaded at all."
              ) ] ] ]

/// The consent + send control. `canSend` is the caller's judgement about whether
/// there is anything to contribute (a tree exists, nothing is in flight).
let controls (state: State) (hasTree: bool) (onConsent: bool -> unit) (onSend: unit -> unit) : ReactElement =
  Html.div
    [ prop.className "fl-contribute-controls"
      prop.children
        [ Html.label
            [ prop.className "fl-contribute-consent"
              prop.children
                [ Html.input
                    [ prop.type' "checkbox"
                      prop.isChecked state.Consented
                      prop.onChange (fun (v: bool) -> onConsent v) ]
                  Html.span [ prop.text "I agree to contribute this session anonymously." ] ] ]
          Html.button
            [ prop.className "fl-btn"
              prop.text (
                if state.Sending then
                  "Contributing…"
                else
                  "Contribute this session"
              )
              prop.disabled (not state.Consented || not hasTree || state.Sending)
              prop.onClick (fun _ -> onSend ()) ]
          (if state.Status = "" then
             Html.none
           else
             Html.p [ prop.className "fl-contribute-status"; prop.text state.Status ]) ] ]

// ─── flat surfaces for the headless suite ────────────────────────────────────
//
// F# `Result`s, DUs and lists are awkward across the Fable boundary, so — as
// `Session.ingestResult` and `Console.runLine` already do — the guard and the
// prepare path project to plain values.

/// `findings` as a plain array.
let findingsFlat (json: string) : string array = findings json |> Array.ofList

/// The provider origins as a plain array, so the test can assert this list, the
/// adapter registry's and the CSP module's are one set.
let providerOriginsFlat () : string array = providerOrigins |> Array.ofList

/// `build` from flat arguments.
let buildFlat (providerId: string) (modelId: string) (capturedAt: string) (session: Session.SessionState) : string =
  build
    { ProviderId = providerId
      ModelId = modelId
      CapturedAt = capturedAt }
    session

/// `prepare`, flattened: `Ok` plus the payload, or the refusal reason.
let prepareFlat
  (providerId: string)
  (modelId: string)
  (capturedAt: string)
  (session: Session.SessionState)
  : {| Ok: bool
       Reason: string
       Json: string |}
  =
  match
    prepare
      { ProviderId = providerId
        ModelId = modelId
        CapturedAt = capturedAt }
      session
  with
  | Ok(ContributionBundle.Verified json) ->
    {| Ok = true
       Reason = ""
       Json = json |}
  | Error reason ->
    {| Ok = false
       Reason = reason
       Json = "" |}

/// The WHOLE path a click takes — prepare, then post through a sink built for
/// `endpoint` — projected flat. This is what the guard test drives, so what it
/// exercises is the shipped sequence rather than a re-assembly of it: an
/// endpoint of `""` is the public build, and a refused prepare must never reach
/// the sink at all.
let contributeProbeFlat
  (endpoint: string)
  (providerId: string)
  (modelId: string)
  (capturedAt: string)
  (session: Session.SessionState)
  : JS.Promise<{| Outcome: string; Reason: string |}> =
  async {
    match
      prepare
        { ProviderId = providerId
          ModelId = modelId
          CapturedAt = capturedAt }
        session
    with
    | Error reason ->
      return
        {| Outcome = "refused"
           Reason = reason |}
    | Ok bundle ->
      let! outcome = (sinkTo endpoint).Post bundle

      return
        match outcome with
        | ContributionOutcome.Sent -> {| Outcome = "sent"; Reason = "" |}
        | ContributionOutcome.Refused reason ->
          {| Outcome = "refused"
             Reason = reason |}
        | ContributionOutcome.Failed reason ->
          {| Outcome = "failed"
             Reason = reason |}
  }
  |> Async.StartAsPromise

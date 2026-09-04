module Fuaran.Showcase.AgentReadable

// ============================================================================
//  The Agent-Readable Page – a page that advertises its own natural-language
//  affordances, so an agent (or a screen reader) can READ what it may say
//  instead of guessing it one failed attempt at a time. Pillar: "the machine
//  can see the UI" (its self-description face – the read-side twin of Hand on
//  the Wheel's actuation).
//
//  The idea in one sentence: an interface that is typed data can also DECLARE,
//  in the page itself, which of its controls a machine may drive, what phrasings
//  it already understands, which synonyms it resolves, and what values each
//  control will accept.
//
//  ── Why this is a DOM story ────────────────────────────────────────────────
//
//  A browser-hosted agent reads the accessibility tree. So the declaration is
//  published where that read already goes: as plain `data-*` attributes on the
//  controls themselves, plus a conservative `aria-description` for the reader
//  that only speaks prose. Nothing here is a private channel or a side-band
//  protocol – view source, and the whole vocabulary is in front of you. That is
//  the point, and it is why the vocabulary can be taught on a public page at
//  all: it ships in the DOM.
//
//  The attribute vocabulary, per control:
//
//    data-fuaran-module         the region this control belongs to
//    data-fuaran-field          the name the host addresses this control by
//    data-fuaran-shape          text | number | boolean | choice | unknown
//    data-fuaran-controllable   "true" – may be set; "false" – readable only
//    data-fuaran-commands       [{"phrase":"…{value}…","effect":"write"}, …]
//    data-fuaran-aliases        [{"alias":"downtown","value":"Central"}, …]
//    data-fuaran-values         {"kind":"oneOf"|"numberRange"|"textLength", …}
//    data-fuaran-description    short human text (render it; never parse it)
//    aria-description           up to three example phrasings, as prose
//
//  Two shapes of ABSENCE carry meaning and neither is written as a null. An
//  open end of a range is simply omitted – a half-open bound is a real
//  declaration and a sentinel would be a lie. And a control the page does not
//  publish at all is not in the enumeration: an agent cannot tell "withheld"
//  from "never existed", which is what makes non-publication a usable deny.
//  `controllable="false"` is the different statement – you may ask, you may not
//  set.
//
//  ── Honest scope ───────────────────────────────────────────────────────────
//
//  The declaration on this page is HAND-AUTHORED: the page plays the host and
//  says what its own controls afford. It is built from the shipped affordance
//  vocabulary in `Fuaran.UI.Renderer.Affordances` (the real `FieldShape` /
//  `CommandEffect` / `ValueHint` types and their wire projections), and the JSON
//  payloads are minted by the same canonical encoder the wire format uses – so
//  the strings in the DOM cannot drift from the vocabulary they claim to speak.
//  The page also registers itself with that module's provider registry, which is
//  the seam an in-page introspection surface serves `getAffordances` from.
//
//  What the "what an agent sees" pane shows is a genuine read: it walks the live
//  DOM with `querySelectorAll("[data-fuaran-field]")` and reports back exactly
//  the attributes it finds, parsed with the browser's own `JSON.parse`. Nothing
//  in that pane is fed from the F# values behind it – if the annotation and the
//  DOM ever disagreed, the pane would show the DOM.
// ============================================================================

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Fuaran.Core
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Affordances

// ─── The toy application: a library-catalogue hold request ───────────────────

let private moduleId = "hold-request"

let private kTitle = "ar-title"
let private kFormat = "ar-format"
let private kBranch = "ar-branch"
let private kCopies = "ar-copies"
let private kNotify = "ar-notify"
let private kCommit = "ar-commit"

let private watchedKeys =
  Set.ofList [ kTitle; kFormat; kBranch; kCopies; kNotify; kCommit ]

/// The element ids the renderer emits for each control – and, deliberately, the
/// same strings the affordance declaration addresses them by. One name, one
/// thing: the id in the DOM IS the field id an agent drives.
let private idTitle = "catalogue-title"
let private idFormat = "catalogue-format"
let private idBranch = "catalogue-branch"
let private idCopies = "catalogue-copies"
let private idNotify = "catalogue-notify"
let private idQueue = "catalogue-queue"

// ─── The declaration (the real shipped vocabulary, hand-authored) ────────────

let private cmd (phrase: string) (effect: CommandEffect) : DeclaredCommand = { Phrase = phrase; Effect = effect }

let private declaredFields: FieldAffordance list =
  [ { Id = idTitle
      Shape = FieldShape.Text
      Controllable = true
      Commands =
        [ cmd "search the catalogue for {value}" CommandEffect.Write
          cmd "what am I searching for?" CommandEffect.Read ]
      Aliases = []
      // A minimum with no maximum: the open end is OMITTED, never sent as null.
      Values = Some(ValueHint.TextLength(Some 2, None))
      Description = Some "The title or author to look for in the catalogue." }
    { Id = idFormat
      Shape = FieldShape.Choice
      Controllable = true
      Commands =
        [ cmd "set the format to {value}" CommandEffect.Write
          cmd "which format is selected?" CommandEffect.Read ]
      Aliases =
        [ "paperback", "Print"
          "hardback", "Print"
          "e-book", "Ebook"
          "audio", "Audiobook" ]
      Values = Some(ValueHint.OneOf [ "Print"; "Ebook"; "Audiobook" ])
      Description = Some "Which edition of the item to reserve." }
    { Id = idBranch
      Shape = FieldShape.Choice
      Controllable = true
      Commands =
        [ cmd "collect it from {value}" CommandEffect.Write
          cmd "where am I collecting it?" CommandEffect.Read ]
      Aliases =
        [ "downtown", "Central"
          "the main library", "Central"
          "the river branch", "Riverside" ]
      Values = Some(ValueHint.OneOf [ "Central"; "Riverside"; "Hillcrest" ])
      Description = Some "The branch the reserved item is sent to." }
    { Id = idCopies
      Shape = FieldShape.Number
      Controllable = true
      Commands =
        [ cmd "set the number of copies to {value}" CommandEffect.Write
          cmd "how many copies am I reserving?" CommandEffect.Read ]
      Aliases = [ "a single copy", "1"; "a couple", "2" ]
      Values = Some(ValueHint.NumberRange(Some 1.0, Some 4.0, Some 1.0))
      Description = Some "How many copies to reserve. A member may hold at most four at a time." }
    { Id = idNotify
      Shape = FieldShape.Boolean
      Controllable = true
      Commands =
        [ cmd "let me know when it arrives" CommandEffect.Write
          cmd "stop telling me when it arrives" CommandEffect.Write ]
      Aliases = []
      // A flag has no declared bound, so it carries NO hint at all – absence
      // already says "unconstrained", and a marker would invite a client to
      // branch on it.
      Values = None
      Description = Some "Whether the branch emails you when the item is ready." }
    // Readable, not settable. Published deliberately: an agent may ASK about the
    // queue, and will be refused if it tries to set it. A control the page did
    // not want touched at all would simply be absent from this list.
    { Id = idQueue
      Shape = FieldShape.Number
      Controllable = false
      Commands = [ cmd "how many people are ahead of me?" CommandEffect.Read ]
      Aliases = []
      Values = None
      Description = Some "How many members are ahead of you in the queue. The branch reports it; nobody sets it." } ]

/// Phrases that address the region itself rather than one of its controls.
let private declaredModuleCommands: DeclaredCommand list =
  [ cmd "place the hold" CommandEffect.Invoke
    cmd "take me to my holds" CommandEffect.Navigate ]

let private declaredModule: ModuleAffordance =
  { Id = moduleId
    Active = true
    Fields = declaredFields
    Commands = declaredModuleCommands }

// ─── Projection: the declaration as DOM attributes ───────────────────────────
//
// Every payload below is rendered by `Fuaran.Core`'s canonical JSON encoder over
// a typed `JVal`, and every closed-set token comes from the vocabulary's own
// `toWire` function. So the bytes in the DOM are canon-by-construction: there is
// no hand-typed JSON here that could drift from the type it claims to encode.

let private commandsJson (commands: DeclaredCommand list) : string =
  Canon.render (
    JArr [ for c in commands -> JObj [ "phrase", JStr c.Phrase; "effect", JStr(CommandEffect.toWire c.Effect) ] ]
  )

let private aliasesJson (aliases: (string * string) list) : string =
  Canon.render (JArr [ for alias, canonical in aliases -> JObj [ "alias", JStr alias; "value", JStr canonical ] ])

/// The three bound shapes, each with its open ends OMITTED rather than nulled.
let private valuesJson (hint: ValueHint) : string =
  let payload =
    match hint with
    | ValueHint.OneOf values -> JObj [ "kind", JStr "oneOf"; "values", JArr [ for v in values -> JStr v ] ]
    | ValueHint.NumberRange(min, max, step) ->
      JObj(
        [ Some("kind", JStr "numberRange")
          min |> Option.map (fun v -> "min", JFloat v)
          max |> Option.map (fun v -> "max", JFloat v)
          step |> Option.map (fun v -> "step", JFloat v) ]
        |> List.choose id
      )
    | ValueHint.TextLength(minLength, maxLength) ->
      JObj(
        [ Some("kind", JStr "textLength")
          minLength |> Option.map (fun v -> "minLength", JInt v)
          maxLength |> Option.map (fun v -> "maxLength", JInt v) ]
        |> List.choose id
      )

  Canon.render payload

/// A representative value to stand in for a `{value}` slot in the prose
/// examples. Derived from the declared bound where there is one – so the
/// example is always something the control would actually accept.
let private exampleValue (field: FieldAffordance) : string =
  match field.Values with
  | Some(ValueHint.OneOf(first :: _)) -> first
  | Some(ValueHint.NumberRange(Some low, _, _)) -> Canon.render (JFloat low)
  | Some(ValueHint.TextLength _) -> "The Dispossessed"
  | _ -> "…"

/// The conservative half: up to three example phrasings, as prose, for a reader
/// that speaks sentences rather than JSON. Everything machine-actionable is in
/// the typed attributes; this is a courtesy, not a second channel.
let private ariaDescription (field: FieldAffordance) : string =
  let sample = exampleValue field

  let phrasings =
    field.Commands
    |> List.truncate 3
    |> List.map (fun c -> "“" + c.Phrase.Replace("{value}", sample) + "”")

  match phrasings with
  | [] -> ""
  | xs -> "You can say: " + String.concat "; " xs + "."

type private Annotation =
  { FieldId: string
    Attributes: (string * string) list }

let private annotationFor (field: FieldAffordance) : Annotation =
  let attributes =
    [ "data-fuaran-module", moduleId
      "data-fuaran-field", field.Id
      "data-fuaran-shape", FieldShape.toWire field.Shape
      "data-fuaran-controllable", (if field.Controllable then "true" else "false")
      "data-fuaran-commands", commandsJson field.Commands
      if not (List.isEmpty field.Aliases) then
        "data-fuaran-aliases", aliasesJson field.Aliases
      match field.Values with
      | Some hint -> "data-fuaran-values", valuesJson hint
      | None -> ()
      match field.Description with
      | Some text -> "data-fuaran-description", text
      | None -> ()
      let aria = ariaDescription field

      if aria <> "" then
        "aria-description", aria ]

  { FieldId = field.Id
    Attributes = attributes }

let private annotations: Annotation list = declaredFields |> List.map annotationFor

/// The whole annotation set as one canonical JSON document. Exported so the
/// repository's own test suite can certify the payloads this page hangs on its
/// controls – the shapes are a contract a reader relies on, so they are pinned
/// rather than trusted.
let annotationsJson: string =
  Canon.render (
    JArr
      [ for a in annotations ->
          JObj
            [ "field", JStr a.FieldId
              "attributes", JObj [ for name, value in a.Attributes -> name, JStr value ] ] ]
  )

// ─── Publishing: onto the DOM, and into the affordance registry ──────────────

let private findElement (elementId: string) : Browser.Types.Element option =
  let byId = Browser.Dom.document.getElementById elementId

  if not (isNull (box byId)) then
    Some(unbox<Browser.Types.Element> byId)
  else
    None

/// Hang the declaration on the live controls. Idempotent, and cheap enough to
/// re-run after every render – React owns the props it set, so an attribute the
/// host added itself has to be re-applied whenever an element is replaced.
let private applyAnnotations () : unit =
  for annotation in annotations do
    match findElement annotation.FieldId with
    | Some el ->
      for name, value in annotation.Attributes do
        el.setAttribute (name, value)
    | None -> ()

// ─── The read: what a machine walking this page actually gets back ───────────

[<Emit("(function(){ try { return JSON.parse($0); } catch (e) { return null; } })()")>]
let private tryParseJson (raw: string) : obj = jsNative

/// What a reader gets for "the value of this thing", in the order it would try:
/// a form control's own value (a checkbox reports its checked state), then the
/// rendered value part of a display node — the renderer's class vocabulary is
/// parity-locked across hosts, so it is a legitimate thing to read — and finally
/// the element's text.
[<Emit("(function(el){ if (el.type === 'checkbox') return String(el.checked); if (el.value !== undefined && el.value !== null) return String(el.value); var part = el.querySelector && el.querySelector('.fuaran-metric-value'); return ((part || el).textContent || '').replace(/\\s+/g, ' ').trim(); })($0)")>]
let private readControlValue (el: obj) : string = jsNative

let private parsedCommands (raw: string) : (string * string) list =
  let parsed = tryParseJson raw

  if isNull (box parsed) then
    []
  else
    [ for item in unbox<obj array> parsed -> unbox<string> item?phrase, unbox<string> item?effect ]

let private parsedAliases (raw: string) : (string * string) list =
  let parsed = tryParseJson raw

  if isNull (box parsed) then
    []
  else
    [ for item in unbox<obj array> parsed -> unbox<string> item?alias, unbox<string> item?value ]

/// One control as READ BACK from the DOM. Every field here came out of an
/// attribute; none of it is fed from the F# declaration above.
type private SeenControl =
  { Tag: string
    Module: string
    Field: string
    Shape: string
    Controllable: string
    Commands: (string * string) list
    Aliases: (string * string) list
    Values: string option
    Description: string option
    Aria: string option
    ValueNow: string }

let private attribute (el: Browser.Types.Element) (name: string) : string option =
  let raw = el.getAttribute name
  if isNull (box raw) then None else Some raw

let private orEmpty (value: string option) : string = defaultArg value ""

/// The whole read: one `querySelectorAll` over the marker attribute, then the
/// attributes of whatever it found. This is the accessibility-tree read a
/// browser-hosted agent performs, done in the page so you can watch it.
let private readPage () : SeenControl list =
  let found = Browser.Dom.document.querySelectorAll "[data-fuaran-field]"

  [ for i in 0 .. found.length - 1 do
      let el: Browser.Types.Element = unbox found[i]

      { Tag = el.tagName.ToLower()
        Module = orEmpty (attribute el "data-fuaran-module")
        Field = orEmpty (attribute el "data-fuaran-field")
        Shape = orEmpty (attribute el "data-fuaran-shape")
        Controllable = orEmpty (attribute el "data-fuaran-controllable")
        Commands =
          attribute el "data-fuaran-commands"
          |> Option.map parsedCommands
          |> Option.defaultValue []
        Aliases =
          attribute el "data-fuaran-aliases"
          |> Option.map parsedAliases
          |> Option.defaultValue []
        Values = attribute el "data-fuaran-values"
        Description = attribute el "data-fuaran-description"
        Aria = attribute el "aria-description"
        ValueNow = readControlValue (box el) } ]

/// Whether this build also serves the same declaration through the renderer's
/// in-page introspection surface. That surface is a debug-build opt-in, so a
/// production visit reads the DOM instead – which is exactly why the DOM is
/// where the declaration lives.
[<Emit("(typeof globalThis !== 'undefined' && globalThis.__fuaran && typeof globalThis.__fuaran.getAffordances === 'function') ? true : false")>]
let private hasIntrospectionSurface () : bool = jsNative

// ─── The page's own tree (real Fuaran nodes, drawn by the real renderer) ─────

/// The browser host denies dispatch by default, which is the right posture for a
/// host that cannot vet what it is asked to run. This page's tree declares
/// exactly one action — the submit button's state write, named in the source
/// below — and the page is a zero-egress exhibit with no network, no key and no
/// model behind it, so the named opt-in is stated here rather than inherited.
let private browserRuntime: Runtime.IFuaranRuntime =
  BrowserRuntime.createPermissive ()

let private option (value: string) : SelectOption = { Value = value; Label = value }

let private holdForm: Node<obj> =
  Fuaran.card
    "catalogue-card"
    { Defaults.card with
        Heading = Some(TextSource.Literal "Riverside Library — place a hold")
        Children =
          [ Fuaran.markdown
              "catalogue-intro"
              "An ordinary little form. Every control on it also **says what it affords** — in attributes that ship in the page."
            Fuaran.form
              "catalogue-hold"
              { Defaults.form with
                  Fields =
                    [ { Id = idTitle
                        Label = TextSource.Literal "Title or author"
                        Kind = FormFieldKind.Text(Some(Binding.State(kTitle, Some "The Dispossessed")), None)
                        Required = true
                        Help = Some(TextSource.Literal "At least two characters.")
                        Rule = None }
                      { Id = idFormat
                        Label = TextSource.Literal "Format"
                        Kind =
                          FormFieldKind.Choice(
                            Binding.Static(Some [ option "Print"; option "Ebook"; option "Audiobook" ]),
                            Some(Binding.State(kFormat, Some "Print")),
                            None
                          )
                        Required = true
                        Help = None
                        Rule = None }
                      { Id = idBranch
                        Label = TextSource.Literal "Collect from"
                        Kind =
                          FormFieldKind.Choice(
                            Binding.Static(Some [ option "Central"; option "Riverside"; option "Hillcrest" ]),
                            Some(Binding.State(kBranch, Some "Riverside")),
                            None
                          )
                        Required = true
                        Help = None
                        Rule = None }
                      { Id = idCopies
                        Label = TextSource.Literal "Copies"
                        Kind =
                          FormFieldKind.RangedNumber(
                            Some(Binding.State(kCopies, Some 1.0)),
                            None,
                            Some 1.0,
                            Some 4.0,
                            Some 1.0
                          )
                        Required = true
                        Help = Some(TextSource.Literal "One to four.")
                        Rule = None }
                      { Id = idNotify
                        Label = TextSource.Literal "Email me when it arrives"
                        Kind = FormFieldKind.Checkbox(Some(Binding.State(kNotify, Some true)), None)
                        Required = false
                        Help = None
                        Rule = None } ]
                  OnSubmit = Action.SetState(kCommit, Some(JStr "placed"), None)
                  SubmitLabel = TextSource.Literal "Place the hold" }
            Fuaran.metric
              idQueue
              { Defaults.metric with
                  Label = TextSource.Literal "Members ahead of you"
                  Value = Binding.Static(Some 3.0)
                  Subtext = Some(TextSource.Literal "Reported by the branch — readable, not settable") } ] }

let private renderLive (node: Node<obj>) : ReactElement =
  Render.render
    { Sources =
        { BindingResolver.empty with
            State = StateStore.snapshot () }
      Runtime = browserRuntime
      VisAdapter = VisAdapter.noOp<obj>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Render.collectFragments Map.empty node
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      // Deny non-local egress. Note this page names a permissive DISPATCH
      // runtime a few lines up and still denies egress here: the two seams are
      // declared separately and one does not imply the other. Dispatch was
      // opted out of because the tree's single action is a state write this
      // page authored and can read; egress is a different question, and the
      // honest answer is that the hold form names no destination at all - no
      // link, no image, no markdown anchor - so there is nothing to declare
      // and a denial forbids nothing the exhibit does. The page advertises
      // what it affords; declaring an egress it does not use would be the one
      // affordance claim on it that was not true.
      EgressPolicy = Sanitize.denyNonLocalEgress
      UploadSink = None
      // Client-only page with no durable destination: an unconfigured host
      // records nothing and pays nothing, and `CurrentNodeId` is renderer-owned.
      ActionSink = None
      CurrentNodeId = None }
    node

let private renderStatic (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

let private seedStore () : unit =
  StateStore.set kTitle (box "The Dispossessed")
  StateStore.set kFormat (box "Print")
  StateStore.set kBranch (box "Riverside")
  StateStore.set kCopies (box 1.0)
  StateStore.set kNotify (box true)
  // Cleared by OVERWRITING with the empty sentinel, never removed: string state
  // persists, and a removed key would rise again from persistence as a ghost.
  StateStore.set kCommit (box "")

// ─── The view ────────────────────────────────────────────────────────────────

let private effectChip (effect: string) : ReactElement =
  Html.span [ prop.className ("ar-effect ar-effect-" + effect); prop.text effect ]

let private controlCard (seen: SeenControl) : ReactElement =
  let attributeLines =
    [ "data-fuaran-module", seen.Module
      "data-fuaran-field", seen.Field
      "data-fuaran-shape", seen.Shape
      "data-fuaran-controllable", seen.Controllable ]
    @ (match seen.Values with
       | Some v -> [ "data-fuaran-values", v ]
       | None -> [])
    @ (match seen.Aria with
       | Some v -> [ "aria-description", v ]
       | None -> [])

  Html.div
    [ prop.className "ar-control"
      prop.children
        [ Html.div
            [ prop.className "ar-control-head"
              prop.children
                [ Html.code [ prop.className "ar-control-id"; prop.text seen.Field ]
                  Html.span [ prop.className "ar-shape"; prop.text seen.Shape ]
                  Html.span
                    [ prop.className (
                        if seen.Controllable = "true" then
                          "ar-flag ar-flag-write"
                        else
                          "ar-flag ar-flag-read"
                      )
                      prop.text (
                        if seen.Controllable = "true" then
                          "may be set"
                        else
                          "readable only"
                      ) ]
                  Html.span [ prop.className "ar-tag"; prop.text ("<" + seen.Tag + ">") ] ] ]
          (match seen.Description with
           | Some text -> Html.p [ prop.className "ar-control-desc"; prop.text text ]
           | None -> Html.none)
          Html.div
            [ prop.className "ar-now"
              prop.children
                [ Html.span [ prop.className "ar-now-label"; prop.text "value now" ]
                  Html.code [ prop.className "ar-now-value"; prop.text seen.ValueNow ] ] ]
          Html.div
            [ prop.className "ar-block"
              prop.children
                [ Html.span [ prop.className "ar-block-label"; prop.text "phrases it declares" ]
                  Html.ul
                    [ prop.className "ar-phrases"
                      prop.children
                        [ for phrase, effect in seen.Commands ->
                            Html.li
                              [ prop.children
                                  [ effectChip effect
                                    Html.code [ prop.className "ar-phrase"; prop.text phrase ] ] ] ] ] ] ]
          (if List.isEmpty seen.Aliases then
             Html.none
           else
             Html.div
               [ prop.className "ar-block"
                 prop.children
                   [ Html.span [ prop.className "ar-block-label"; prop.text "synonyms it resolves" ]
                     Html.div
                       [ prop.className "ar-aliases"
                         prop.children
                           [ for alias, canonical in seen.Aliases ->
                               Html.span
                                 [ prop.className "ar-alias"
                                   prop.children
                                     [ Html.code [ prop.text alias ]
                                       Html.span [ prop.className "ar-alias-arrow"; prop.text "→" ]
                                       Html.code [ prop.text canonical ] ] ] ] ] ] ])
          Html.div
            [ prop.className "ar-block"
              prop.children
                [ Html.span [ prop.className "ar-block-label"; prop.text "values it accepts" ]
                  (match seen.Values with
                   | Some raw -> Html.pre [ prop.className "ar-values"; prop.text raw ]
                   | None ->
                     Html.p
                       [ prop.className "ar-none"
                         prop.text "No declared bound — the absence says it; there is no \"unconstrained\" marker." ]) ] ]
          Html.details
            [ prop.className "ar-verbatim"
              prop.children
                [ Html.summary [ prop.text "The attributes, verbatim" ]
                  Html.pre
                    [ prop.className "ar-verbatim-pre"
                      prop.text (
                        attributeLines
                        |> List.map (fun (name, value) -> name + "=\"" + value + "\"")
                        |> String.concat "\n"
                      ) ] ] ] ] ]

[<ReactComponent>]
let private AgentReadableView () : ReactElement =
  // The store subscription is what re-renders the page as the form is edited —
  // which is also what keeps the annotations attached and the read-back live.
  // The value itself is not needed; the subscription is.
  StateStore.useStateKeys watchedKeys |> ignore
  let seen, setSeen = React.useState ([]: SeenControl list)
  let reads, setReads = React.useState 0

  // Seed the page's own store on mount, and publish the declaration into the
  // affordance provider registry for as long as the page is on screen. The
  // registration handle is the teardown – a page that leaves takes its
  // declaration with it.
  React.useEffectOnce (
    (fun () ->
      seedStore ()
      let remove = registerProvider (fun _ -> { Modules = [ declaredModule ] })
      remove)
    : unit -> unit -> unit
  )

  // Re-hang the annotations after every render. Writing attributes React does
  // not manage is cheap and idempotent; skipping it would lose them the first
  // time React replaced a control.
  React.useEffect (fun () -> applyAnnotations ())

  let reread () =
    setSeen (readPage ())
    setReads (reads + 1)

  // The first read happens after the annotations are on the page: effects run
  // in declaration order, so the pass above has already published them.
  React.useEffectOnce (fun () -> setSeen (readPage ()))

  let placed =
    match StateStore.get kCommit with
    | Some v -> unbox<string> v = "placed"
    | None -> false

  let registered = (enumerate (Some moduleId)).Modules

  let registeredFieldCount = registered |> List.sumBy (fun m -> List.length m.Fields)

  let pagePanel =
    Html.div
      [ prop.className "ar-panel"
        prop.children
          [ Html.h3 [ prop.text "The page" ]
            Html.p
              [ prop.className "ar-muted"
                prop.text
                  "An ordinary hold request at a small library. Nothing about it looks unusual, and nothing about it is: it is a typed Fuaran tree drawn by the same renderer as every other page here. Change anything, then re-read it below." ]
            renderLive holdForm
            (if placed then
               renderStatic (
                 Fuaran.callout
                   "ar-placed"
                   { Defaults.callout with
                       Tone = ToneVariant.Success
                       Heading = Some(TextSource.Literal "Hold placed")
                       Body =
                         TextSource.Literal
                           "The submit action is data too — the form carries a state write, not a closure, so the same button survives a trip across the wire." }
               )
             else
               Html.none)
            (if placed then
               Html.button
                 [ prop.className "ar-btn"
                   prop.text "Start again"
                   prop.onClick (fun _ -> StateStore.set kCommit (box "")) ]
             else
               Html.none) ] ]

  let agentPanel =
    Html.div
      [ prop.className "ar-panel"
        prop.children
          [ Html.h3 [ prop.text "What an agent sees" ]
            Html.p
              [ prop.className "ar-muted"
                prop.text
                  "Not a description of the read — the read itself. This pane runs querySelectorAll(\"[data-fuaran-field]\") against the live page above, parses each payload with the browser's own JSON.parse, and prints what came back. Edit the form, press re-read, and watch the values move." ]
            Html.div
              [ prop.className "ar-readbar"
                prop.children
                  [ Html.button
                      [ prop.className "ar-btn ar-btn-primary"
                        prop.text "Re-read the page"
                        prop.onClick (fun _ -> reread ()) ]
                    Html.span
                      [ prop.className "ar-readcount"
                        prop.text (
                          sprintf
                            "%d control%s found · %d read%s so far"
                            (List.length seen)
                            (if List.length seen = 1 then "" else "s")
                            (reads + 1)
                            (if reads = 0 then "" else "s")
                        ) ] ] ]
            Html.div
              [ prop.className "ar-controls"
                prop.children [ for control in seen -> controlCard control ] ] ] ]

  let registryPanel =
    Html.div
      [ prop.className "ar-panel"
        prop.children
          [ Html.h3 [ prop.text "The same declaration, queryable" ]
            Html.p
              [ prop.className "ar-muted"
                prop.text
                  "The DOM is the surface every reader already reaches, so it is where the declaration lives. The renderer also carries a registry for it, and this page registers itself there on arrival — one declaration, two ways to ask." ]
            Html.ul
              [ prop.className "ar-facts"
                prop.children
                  [ Html.li
                      [ prop.text (
                          sprintf
                            "Registered here: %d region, %d declared controls — the same set the attributes carry."
                            (List.length registered)
                            registeredFieldCount
                        ) ]
                    Html.li
                      [ prop.text (
                          if hasIntrospectionSurface () then
                            "This build also publishes the in-page introspection surface, so the same enumeration is available programmatically from the console."
                          else
                            "This build does not publish the in-page introspection surface — it is a debug-build opt-in, and a production page has no obligation to carry one. The attributes above are unaffected: they are in the DOM either way, which is the whole reason to put them there."
                        ) ]
                    Html.li
                      [ prop.text (
                          sprintf
                            "Region-level phrases (they address the region, not a control): %s."
                            (declaredModuleCommands
                             |> List.map (fun c -> "“" + c.Phrase + "” (" + CommandEffect.toWire c.Effect + ")")
                             |> String.concat ", ")
                        ) ] ] ] ] ]

  let honesty =
    Html.div
      [ prop.className "ar-honesty"
        prop.children
          [ Html.h3 [ prop.text "What is real here, and what is staged" ]
            Html.ul
              [ prop.children
                  [ Html.li
                      [ prop.text
                          "The read is real. The pane above walks the live DOM and reports what it finds; nothing in it is fed from the values that wrote the attributes. If the two ever disagreed, you would see the DOM's answer, not the page's intention." ]
                    Html.li
                      [ prop.text
                          "The vocabulary is real. The shapes, the effects, and the three value-hint forms come from the shipped affordance types, and every payload is minted by the same canonical JSON encoder the wire format uses — so these strings cannot drift from the vocabulary they claim to speak." ]
                    Html.li
                      [ prop.text
                          "The declaration is hand-authored. This page plays the host and says what its own controls afford; deriving such a declaration automatically is a host's business, and a host that derives nothing still answers honestly — an empty enumeration is a legitimate answer, never an error." ]
                    Html.li
                      [ prop.text
                          "Absence carries meaning, and never as a null. An open end of a range is omitted, because a half-open bound is a real declaration and a sentinel would be a lie. A control the page chose not to publish is simply not in the list — a reader cannot tell withheld from never-existed, which is what makes silence a usable refusal. Saying \"readable only\" is the different, weaker statement: you may ask, you may not set." ]
                    Html.li
                      [ prop.text
                          "Nothing leaves the tab. There is no key, no network call, and no model — the whole exchange is this page reading itself." ] ] ] ] ]

  Html.div
    [ prop.className "ar-page"
      prop.children
        [ Html.h1 [ prop.className "ar-title"; prop.text "The Agent-Readable Page" ]
          Html.p
            [ prop.className "ar-lede"
              prop.text
                "An assistant driving a web page today is mostly guessing: it looks at a rendering, infers what the controls might be, tries something, and learns from the failure. It does not have to be that way. A page whose interface is typed data can also declare, in the page itself, what it may be asked to do — the phrasings it understands, the synonyms it resolves, and the values each control accepts. Below is such a page, and beside it, exactly what a machine reading it gets back." ]
          pagePanel
          agentPanel
          registryPanel
          honesty ] ]

let page: ReactElement = AgentReadableView()

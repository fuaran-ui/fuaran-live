module Fuaran.Live.Projection

// ============================================================================
//  Multi-language source projection (Phase 329) – restored natively in F#/Fable.
//
//  The Phase 326 rebuild deleted the TypeScript shell and with it the
//  `src/inspector/projections/*` source projectors that Phase 281 shipped. This
//  restores the **Output box**: the current Fuaran tree rendered as JSON plus
//  idiomatic builder source in TypeScript (`@fuaran-ui/ui`), Python
//  (`fuaran_py.ui`), F# (`Fuaran.UI` smart constructors), C# (`Fuaran.UI.CSharp`
//  static-factory + options-object, Phase 362), and VB (`Fuaran.UI.VisualBasic`
//  XML literals, Phase 363). The first four ride one generic per-language
//  `LangSpec` walker; VB has its own walker (its XML-literal shape has no object-
//  literal token).
//
//  Fidelity is per-leg (see docs/PROJECTION_FIDELITY.md). The **TypeScript**
//  leg is a **verified byte-round-trip**: it is emitted per-kind against the
//  real `@fuaran-ui/ui` authoring surface, and `tests/projection-conformance/`
//  executes the generated source and asserts byte-identity with the
//  `wire-format-fixtures/` corpus – restoring the guarantee the pre-rebuild
//  TS-shell projectors carried. The **Python / F# / C# / VB** legs remain
//  illustrative – "how it would look written in" each language, per operator
//  direction – until their conformance arms are re-authored. The projector
//  walks the **canonical wire tree** – the same `Node<obj>` the app already
//  holds, encoded to its canonical JSON – using the vendored `FuaranLive.AiWire`
//  `JsonValue` model the connectors already pulled in (Phase 327). It is pure
//  string generation, never touches the unbuilt `@fuaran-ui/*` runtime, and
//  **never crashes on a tree it does not understand**: any value it does not
//  specifically recognise falls through a generic object / array / literal path
//  (the TS leg falls back to the same sketch for a non-corpus kind), so an
//  uncovered kind still projects rather than throwing.
//
//  There is exactly ONE throwing path, added by Phase 1603, and it is not that
//  case: a wire member that is PRESENT carrying an explicit JSON `null`. A
//  canonical emission never contains one (WIRE_FORMAT §4 rule 4 — absence is
//  structural), and the two positions a decoder accepts one at are normalised
//  rather than refused, so it fires only on input no conformant encoder
//  produces. Both readings available to it are wrong — as an absence it
//  silently drops input, as a value it emits a default the encoder cannot drop
//  — so it names the member instead. See "Wire-field accessors" below.
// ============================================================================

open Fable.Core.JsInterop
open FuaranLive.AiWire

// ─── the target languages ─────────────────────────────────────────────────────

[<RequireQualifiedAccess>]
type Target =
  | Json
  | TypeScript
  | Python
  | FSharp
  | CSharp
  | VisualBasic
  | Go
  | Kotlin
  | Rust
  | Swift

/// The tab order + labels for the Output box – JSON + the nine host languages.
let targets: (Target * string) list =
  [ Target.Json, "JSON"
    Target.TypeScript, "TypeScript"
    Target.Python, "Python"
    Target.FSharp, "F#"
    Target.CSharp, "C#"
    Target.VisualBasic, "VB"
    Target.Go, "Go"
    Target.Kotlin, "Kotlin"
    Target.Rust, "Rust"
    Target.Swift, "Swift" ]

/// The `CodeBlock` language tag for a projection target (the `language-{x}` hint
/// the renderer emits + the client syntax-highlight enhancement reads). Used by
/// the Output box, which dogfoods the `CodeBlock` primitive (Phase 294).
let languageTag (target: Target) : string =
  match target with
  | Target.Json -> "json"
  | Target.TypeScript -> "typescript"
  | Target.Python -> "python"
  | Target.FSharp -> "fsharp"
  | Target.CSharp -> "csharp"
  | Target.VisualBasic -> "vbnet"
  | Target.Go -> "go"
  | Target.Kotlin -> "kotlin"
  | Target.Rust -> "rust"
  | Target.Swift -> "swift"

// ─── node spans: the id → text-range side map (Phase 714) ─────────────────────
//
// The Navigator needs to know WHICH characters of a projection belong to which
// wire node, so walking the tree can light up the corresponding construct in
// every language at once. The generators are pure bottom-up string
// concatenation, so an absolute offset is not knowable while the walk is in
// progress — but NESTING is. Each of the four node emitters therefore wraps its
// own output in invisible sentinels carrying the node's id, and one post-pass
// strips them while recording the (id, start, length) each pair enclosed.
//
// That is exact where a text search would not be: two sibling constructs that
// happen to project to the same source get distinct spans, and a nested node's
// span is genuinely inside its parent's. It also costs each emitter one line,
// rather than threading an offset accumulator through 800 lines of per-kind
// TypeScript emission — which is the difference between a change that can be
// reviewed and one that cannot.
//
// **Marking is off unless a span map was asked for, and the plain entry points
// strip regardless.** Both halves of that are load-bearing, and the corpus is
// why: the node fixture `btn-json-payloads` carries a literal U+0001 INSIDE a
// string payload, deliberately, to pin control-character escaping. So a sentinel
// scheme cannot assume tree content is sentinel-free, and an earlier draft that
// scrubbed content to make the assumption true broke that fixture's byte
// round-trip — quietly, in the one leg with a conformance gate to catch it.
// The resolution keeps the guarantee where it matters: `projectTo` and the
// `toX` family never mark at all, so they emit exactly what they always did;
// `projectSpans` marks for one walk and REFUSES to mark a tree that already
// contains a sentinel, returning that projection with an empty side map (no
// highlight is the honest answer; a wrong highlight over corrupted text is not).

/// A mapped construct in a projection: the wire node id, and the half-open
/// `[Start, Start + Length)` character range of the projected source that the
/// node occupies.
type Span =
  { NodeId: string
    Start: int
    Length: int }

/// A projection and its id → span side map.
type Projected = { Text: string; Spans: Span list }

// C0 controls 1–3: unprintable, and absent from every language's source
// vocabulary. Tree CONTENT may still carry them — the corpus proves it — which
// is what the guard in `projectSpans` is for.
let private spanOpenCh = '\u0001'
let private spanIdEndCh = '\u0002'
let private spanCloseCh = '\u0003'
let private spanOpen = string spanOpenCh
let private spanIdEnd = string spanIdEndCh
let private spanClose = string spanCloseCh

/// Whether a string carries a character the sentinel scheme uses — the guard's
/// test, applied to a whole wire document before any marking is switched on.
let private carriesSentinel (s: string) : bool =
  s.IndexOf spanOpen >= 0 || s.IndexOf spanIdEnd >= 0 || s.IndexOf spanClose >= 0

/// Whether the walk currently in progress is marking. Module-level and mutable
/// deliberately, and the only state in this module:
///
///  • it is a per-WALK setting, not a per-node one, so passing it as a parameter
///    would mean threading a boolean through four recursive walkers including
///    the 800-line per-kind TypeScript emitter — a large diff for no behaviour;
///  • the browser is single-threaded and every walk is synchronous, so there is
///    no interleaving to reason about; and
///  • `projectSpans` is its only writer and sets/clears it around one call in a
///    `try`/`finally`, so it is false everywhere else even if a walk throws.
let mutable private marking = false

/// Wrap a rendered node in the sentinels that record it as `id`'s span — when
/// the walk in progress is marking. An id-less construct is returned untouched;
/// it is simply not in the side map.
let private markSpan (id: string) (body: string) : string =
  if marking && id <> "" then
    spanOpen + id + spanIdEnd + body + spanClose
  else
    body

/// Strip every sentinel from a marked projection, returning the clean text and
/// the spans the sentinels enclosed. Total: unbalanced or absent sentinels
/// degrade to "no spans", never a throw — a projection that cannot be mapped
/// still renders.
let private stripSpans (marked: string) : string * Span list =
  if marked.IndexOf spanOpen < 0 then
    marked, []
  else
    let parts = ResizeArray<string>()
    let spans = ResizeArray<Span>()
    let stack = ResizeArray<string * int>()
    let n = marked.Length
    let mutable clean = 0
    let mutable i = 0

    while i < n do
      let c = marked.[i]

      if c = spanOpenCh then
        match marked.IndexOf(spanIdEnd, i + 1) with
        | j when j < 0 -> i <- n // malformed — abandon the rest rather than guess
        | j ->
          stack.Add(marked.Substring(i + 1, j - i - 1), clean)
          i <- j + 1
      elif c = spanCloseCh then
        if stack.Count > 0 then
          let id, start = stack[stack.Count - 1]
          stack.RemoveAt(stack.Count - 1)

          spans.Add
            { NodeId = id
              Start = start
              Length = clean - start }

        i <- i + 1
      else
        // Copy the whole run of plain characters in one slice.
        let mutable j = i

        while j < n && marked[j] <> spanOpenCh && marked[j] <> spanCloseCh do
          j <- j + 1

        parts.Add(marked.Substring(i, j - i))
        clean <- clean + (j - i)
        i <- j

    String.concat "" parts, List.ofSeq spans

// ─── casing + literal helpers (Fable-safe) ────────────────────────────────────

let private pad (depth: int) : string = System.String(' ', depth * 2)

let private lowerFirst (s: string) : string =
  if s = "" then s else s.[0..0].ToLower() + s.[1..]

let private upperFirst (s: string) : string =
  if s = "" then s else s.[0..0].ToUpper() + s.[1..]

/// "MyKind" / "myField" → "my_kind" / "my_field" (Python field/ctor naming).
let private toSnake (s: string) : string =
  s
  |> Seq.mapi (fun i c ->
    if System.Char.IsUpper c && i > 0 then
      "_" + string (System.Char.ToLower c)
    elif System.Char.IsUpper c then
      string (System.Char.ToLower c)
    else
      string c)
  |> String.concat ""

let private numLit (n: float) : string =
  if n = floor n && abs n < 1e15 then
    string (int64 n)
  else
    string n

let private escape (quote: char) (s: string) : string =
  let q = string quote

  s.Replace("\\", "\\\\").Replace(q, "\\" + q).Replace("\n", "\\n").Replace("\r", "\\r")

// ─── per-language emission spec ───────────────────────────────────────────────
//
// One generic recursive walker (`renderValue` / `renderNode`) drives all three
// builder targets; a `LangSpec` supplies the per-language tokens so the walk
// logic – and thus the structural fidelity – cannot drift between languages.

type private LangSpec =
  {
    /// A node constructor: kind `$type`, node id, rendered (field, value) pairs.
    Node: string -> string -> (string * string) list -> int -> string
    /// A plain object / record literal from rendered (key, value) pairs.
    Obj: (string * string) list -> int -> string
    /// An array / list literal from rendered items.
    Arr: string list -> int -> string
    /// A string literal.
    Str: string -> string
    /// A boolean literal.
    Bool: bool -> string
    /// The null / none literal.
    Null: string
    /// A `Binding.Static` over an already-rendered inner value.
    StaticBinding: string -> string
    /// A `TextSource.Literal` over a raw text string.
    TextLiteral: string -> string
  }

// ─── wire-tree predicates over the shared JsonValue model ─────────────────────

let private dollarType (v: JsonValue) : string option =
  v |> JsonValue.tryField "$type" |> Option.bind JsonValue.asString

/// A node object: carries a string `id` and a `kind` object with a `$type`.
let private isNode (v: JsonValue) : bool =
  match v |> JsonValue.tryField "id" |> Option.bind JsonValue.asString with
  | Some _ -> v |> JsonValue.tryField "kind" |> Option.bind dollarType |> Option.isSome
  | None -> false

/// Wrap a rendered node in its span sentinels, keyed by the wire `id` — the one
/// line each of the four node emitters spends on the Phase 714 side map. A value
/// with no id is returned untouched.
let private markNode (v: JsonValue) (body: string) : string =
  match v |> JsonValue.tryField "id" |> Option.bind JsonValue.asString with
  | Some id -> markSpan id body
  | None -> body

/// Descend through a nested `kind` wrapper (category kinds carry an inner kind)
/// to the innermost kind object – the one whose `$type` names the constructor.
let rec private resolveKind (kindObj: JsonValue) : JsonValue =
  match kindObj |> JsonValue.tryField "kind" with
  | Some inner when (dollarType inner).IsSome -> resolveKind inner
  | _ -> kindObj

let rec private renderValue (spec: LangSpec) (depth: int) (v: JsonValue) : string =
  match v with
  | JNull -> spec.Null
  | JBool b -> spec.Bool b
  | JNumber n -> numLit n
  | JString s -> spec.Str s
  | JArray items -> spec.Arr (items |> List.map (renderValue spec (depth + 1))) depth
  | JObject _ when isNode v -> renderNode spec depth v
  | JObject members ->
    match dollarType v with
    // A literal text source projects as the bare string it carries.
    | Some "Literal" ->
      match v |> JsonValue.tryField "text" |> Option.bind JsonValue.asString with
      | Some t -> spec.TextLiteral t
      | None -> renderObj spec depth members
    // A static binding projects as the builder over its inner value.
    | Some "Static" ->
      match v |> JsonValue.tryField "value" with
      | Some inner -> spec.StaticBinding(renderValue spec depth inner)
      | None -> renderObj spec depth members
    // Any other `$type`-tagged value (other bindings, formats, specs) projects
    // as a generic object literal that keeps its `$type` – a faithful sketch.
    | _ -> renderObj spec depth members

and private renderObj (spec: LangSpec) (depth: int) (members: (string * JsonValue) list) : string =
  spec.Obj (members |> List.map (fun (k, vv) -> k, renderValue spec (depth + 1) vv)) depth

and private renderNode (spec: LangSpec) (depth: int) (v: JsonValue) : string =
  let id =
    v
    |> JsonValue.tryField "id"
    |> Option.bind JsonValue.asString
    |> Option.defaultValue ""

  let kindObj =
    v |> JsonValue.tryField "kind" |> Option.defaultValue JNull |> resolveKind

  let kindName = dollarType kindObj |> Option.defaultValue "Node"

  let fields =
    match kindObj with
    | JObject members ->
      members
      |> List.filter (fun (k, _) -> k <> "$type" && k <> "kind")
      |> List.map (fun (k, vv) -> k, renderValue spec (depth + 1) vv)
    | _ -> []

  markSpan id (spec.Node kindName id fields depth)

// ─── TypeScript (@fuaran-ui/ui) ───────────────────────────────────────────────

let private tsSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let ctor = "fuaran." + lowerFirst kind
        let idLit = "'" + escape '\'' id + "'"

        if List.isEmpty fields then
          ctor + "(" + idLit + ", {})"
        else
          let body =
            fields
            |> List.map (fun (k, v) -> pad (depth + 1) + k + ": " + v)
            |> String.concat ",\n"

          ctor + "(" + idLit + ", {\n" + body + ",\n" + pad depth + "})"
    Obj =
      fun members depth ->
        if List.isEmpty members then
          "{}"
        else
          let body =
            members
            |> List.map (fun (k, v) -> pad (depth + 1) + k + ": " + v)
            |> String.concat ",\n"

          "{\n" + body + ",\n" + pad depth + "}"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "[]"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat ",\n"
          "[\n" + body + ",\n" + pad depth + "]"
    Str = fun s -> "'" + escape '\'' s + "'"
    Bool = fun b -> if b then "true" else "false"
    Null = "undefined"
    StaticBinding = fun inner -> "binding.static(" + inner + ")"
    TextLiteral = fun t -> "'" + escape '\'' t + "'" }

// ─── F# (Fuaran.UI smart constructors) ────────────────────────────────────────

let private fsSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let ctor = "Fuaran." + lowerFirst kind
        let idLit = "\"" + escape '"' id + "\""

        if List.isEmpty fields then
          ctor + " " + idLit + " Defaults." + lowerFirst kind
        else
          let body =
            fields
            |> List.map (fun (k, v) -> pad (depth + 1) + upperFirst k + " = " + v)
            |> String.concat "\n"

          ctor
          + " "
          + idLit
          + "\n"
          + pad depth
          + "  { Defaults."
          + lowerFirst kind
          + " with\n"
          + body
          + " }"
    Obj =
      fun members depth ->
        if List.isEmpty members then
          "{| |}"
        else
          let body =
            members
            |> List.map (fun (k, v) -> pad (depth + 1) + upperFirst k + " = " + v)
            |> String.concat "\n"

          "{|\n" + body + " |}"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "[]"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat "\n"
          "[\n" + body + " ]"
    Str = fun s -> "\"" + escape '"' s + "\""
    Bool = fun b -> if b then "true" else "false"
    Null = "null"
    StaticBinding = fun inner -> "Binding.Static(" + inner + ")"
    TextLiteral = fun t -> "TextSource.Literal \"" + escape '"' t + "\"" }

// ─── C# (Fuaran.UI.CSharp) ────────────────────────────────────────────────────
//
// The C# authoring veneer (Wave 45) is static-factory + options-object – the same
// object-literal shape as TS, so it drives the generic walker as a fourth spec.
// The one difference from TS: the veneer carries `Id` INSIDE the options object
// (`Metric(new() { Id = …, … })`), not as a positional first argument. Blessed
// idiom `using static Fuaran.UI.CSharp.Fuaran;` binds the bare factory name.

let private csSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let ctor = upperFirst kind
        let members = ("Id", "\"" + escape '"' id + "\"") :: fields

        let body =
          members
          |> List.map (fun (k, v) -> pad (depth + 1) + upperFirst k + " = " + v)
          |> String.concat ",\n"

        ctor + "(new() {\n" + body + ",\n" + pad depth + "})"
    Obj =
      fun members depth ->
        if List.isEmpty members then
          "new() { }"
        else
          let body =
            members
            |> List.map (fun (k, v) -> pad (depth + 1) + upperFirst k + " = " + v)
            |> String.concat ",\n"

          "new() {\n" + body + ",\n" + pad depth + "}"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "[]"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat ",\n"
          "[\n" + body + ",\n" + pad depth + "]"
    Str = fun s -> "\"" + escape '"' s + "\""
    Bool = fun b -> if b then "true" else "false"
    Null = "null"
    StaticBinding = fun inner -> "Binding.Static(" + inner + ")"
    TextLiteral = fun t -> "\"" + escape '"' t + "\"" }

// ─── VB (Fuaran.UI.VisualBasic) – XML-literal shape ───────────────────────────
//
// VB authors via first-class XML literals (Wave 46), which does NOT fit the
// builder-shaped `LangSpec` – there is no `{ … }` object-literal token. A node is
// `<Kind attr="…">children</Kind>`: a scalar field is an attribute, a nested node
// is a child element, a bound query is `$name`, a `CellFormat` is a `format-*`
// attribute (Phase 310/311 vocabulary). So VB gets its own recursive walker,
// with the same never-crash guarantee (anything unrecognised falls to a best-
// effort attribute or a commented child – a faithful sketch, never a throw).

let private escapeXml (s: string) : string =
  s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

/// A `CellFormat` `$type` → the VB `format-*` attribute suffix (Phase 311 vocab).
let private formatSuffix (ty: string) : string option =
  match ty with
  | "Currency" -> Some "currency"
  | "Number" -> Some "number"
  | "Percent" -> Some "percent"
  | "Date" -> Some "date"
  | _ -> None

/// Render a value as an XML attribute string, or `None` if it must become a child
/// element (a node, an array, or an object we cannot flatten to a scalar).
let rec private vbAttr (v: JsonValue) : string option =
  match v with
  | JString s -> Some(escapeXml s)
  | JNumber n -> Some(numLit n)
  | JBool b -> Some(if b then "true" else "false")
  | JNull -> None
  | JObject _ when isNode v -> None
  | JArray _ -> None
  | JObject _ ->
    match dollarType v with
    // A literal text source projects as the bare string it carries.
    | Some "Literal" ->
      v
      |> JsonValue.tryField "text"
      |> Option.bind JsonValue.asString
      |> Option.map escapeXml
    // A static binding projects as its inner scalar (if flattenable).
    | Some "Static" -> v |> JsonValue.tryField "value" |> Option.bind vbAttr
    // Any other binding / query projects as the `$name` bound-attribute convention.
    | Some other -> Some("$" + lowerFirst other)
    | None -> None

/// The child elements a node field contributes (nodes recurse; array items map;
/// anything else becomes a commented sketch child – never a throw).
let rec private vbChildren (depth: int) (fieldName: string) (v: JsonValue) : string list =
  match v with
  | JObject _ when isNode v -> [ vbNode depth v ]
  | JArray items -> items |> List.collect (vbChildren depth fieldName)
  | _ -> [ "<!-- " + fieldName + " (sketched) -->" ]

and private vbNode (depth: int) (v: JsonValue) : string = markNode v (vbNodeRaw depth v)

and private vbNodeRaw (depth: int) (v: JsonValue) : string =
  let id =
    v
    |> JsonValue.tryField "id"
    |> Option.bind JsonValue.asString
    |> Option.defaultValue ""

  let kindObj =
    v |> JsonValue.tryField "kind" |> Option.defaultValue JNull |> resolveKind

  let kindName = dollarType kindObj |> Option.defaultValue "Node"

  let fields =
    match kindObj with
    | JObject members -> members |> List.filter (fun (k, _) -> k <> "$type" && k <> "kind")
    | _ -> []

  // Partition each field into an XML attribute or child element(s). `id` leads.
  let attrs = System.Collections.Generic.List<string * string>()
  attrs.Add("id", escapeXml id)
  let children = System.Collections.Generic.List<string>()

  for (k, vv) in fields do
    match vv with
    // A `format` field carrying a CellFormat → the `format-<kind>` attribute.
    | JObject _ when k = "format" && (dollarType vv |> Option.bind formatSuffix).IsSome ->
      let suffix =
        dollarType vv |> Option.bind formatSuffix |> Option.defaultValue "currency"

      let inner =
        vv
        |> JsonValue.tryField "code"
        |> Option.bind JsonValue.asString
        |> Option.map escapeXml
        |> Option.defaultValue ""

      attrs.Add("format-" + suffix, inner)
    | _ ->
      match vbAttr vv with
      | Some s -> attrs.Add(k, s)
      | None -> children.AddRange(vbChildren (depth + 1) k vv)

  let attrStr =
    attrs
    |> Seq.map (fun (n, value) -> n + "=\"" + value + "\"")
    |> String.concat " "

  if children.Count = 0 then
    "<" + kindName + " " + attrStr + " />"
  else
    let body = children |> Seq.map (fun c -> pad (depth + 1) + c) |> String.concat "\n"

    "<"
    + kindName
    + " "
    + attrStr
    + ">\n"
    + body
    + "\n"
    + pad depth
    + "</"
    + kindName
    + ">"

// ─── Verified TypeScript projection (conformance-gated) ──────────────────────
//
// The TypeScript leg is emitted per-kind against the real `@fuaran-ui/ui`
// authoring surface and carries the verified byte-round-trip guarantee: the
// `tests/projection-conformance/` harness executes the generated source against
// the real packages and asserts the re-encoded canonical JSON is byte-identical
// to the shared wire-format corpus. (The Python / F# / C# / VB legs remain the
// illustrative generic walk above.) Two consequences shape the emitter,
// mirroring the pre-rebuild TS-shell projector it restores:
//
//   • Closure-valued fields (handlers, accessors, parse/format) are erased to
//     "<closure>" by the canonical encoder, so the emitter uses structurally
//     correct placeholders (`() => action.chain([])`, `() => undefined`, `0`)
//     – only the observable payload is projected faithfully.
//   • The smart constructors inject per-kind ARIA defaults the canonical
//     minimal trees omit; the emitter pins the node's base traits (style /
//     state / accessibility) back to the wire's exact values so the re-encode
//     is byte-stable.
//
// A kind the emitter does not recognise falls back to the illustrative generic
// walk – never a crash, and never a false claim of fidelity (the fallback is
// visibly a sketch; the conformance gate covers every corpus kind).

let private qs (s: string) : string = "'" + escape '\'' s + "'"

let private isJsIdent (s: string) : bool =
  s <> ""
  && (System.Char.IsLetter s.[0] || s.[0] = '_' || s.[0] = '$')
  && s |> Seq.forall (fun c -> System.Char.IsLetterOrDigit c || c = '_' || c = '$')

let private jsKey (k: string) : string = if isJsIdent k then k else qs k

let private boolLit (b: bool) : string = if b then "true" else "false"

/// Multi-line options-object literal (the shape the smart ctors take).
let private tsObjLit (fields: (string * string) list) (depth: int) : string =
  if List.isEmpty fields then
    "{}"
  else
    let body =
      fields
      |> List.map (fun (k, v) -> pad (depth + 1) + k + ": " + v)
      |> String.concat ",\n"

    "{\n" + body + ",\n" + pad depth + "}"

/// Single-line object literal (small nested records).
let private tsInline (fields: (string * string) list) : string =
  if List.isEmpty fields then
    "{}"
  else
    "{ "
    + (fields |> List.map (fun (k, v) -> k + ": " + v) |> String.concat ", ")
    + " }"

// ── Wire-field accessors (Phase 1603) ───────────────────────────────────────
//
// There used to be ONE accessor here and it was total: `fieldD` answered `JNull`
// for a member the wire OMITTED and `JNull` for one it carried as an explicit
// `null`, so no call site could tell the two apart. That conflation is the
// `Tabs.activeIndex` defect: an omitted omit-at-default member reached the
// binding projector as `JNull` and came back out as an EXPLICIT `Static`, which
// the builder's own default could no longer fill and the encoder could no longer
// omit — so a key the fixture does not have survived the re-encode. `activeIndex`
// is only the FIRST omit-at-default binding slot, so the fix has to be the class
// rather than the instance.
//
// Every read is therefore CLASSIFIED by which accessor it uses:
//
//   absent-is-omit   `fieldOpt`        the member is optional at this site, so an
//                                      absent one is OMITTED from the emission
//                                      and the re-encode carries no key either.
//   absent-is-omit   `fieldOrIdentity` the same classification, spelled as the
//                                      slot's IDENTITY DEFAULT — for a target
//                                      model that has no absence at a slot the
//                                      wire omits at its default. Which of the
//                                      two spellings a site takes is decided by
//                                      the target, never by the wire.
//   absent-is-error  `fieldReq`        a canonical emission always carries the
//                                      member at this site, so absence is
//                                      malformed input rather than a shape to
//                                      project. It stays TOTAL — the projector
//                                      never crashes on a tree it does not
//                                      understand — but every absence is RECORDED
//                                      BY NAME and `tests/projection-conformance/`
//                                      asserts the record stays EMPTY over the
//                                      canonical node corpus. A fixture that later
//                                      omits one fails there, by name, instead of
//                                      quietly projecting a default.
//
// `WIRE_FORMAT.md` §4 rule 4 is what makes the split decidable: "`null` does not
// appear anywhere in a canonical Fuaran emission … absence is structural,
// expressed by a missing key". A member present AS null is therefore UNREADABLE
// — neither the absence the wire spells structurally nor a value the model has —
// and the accessors REFUSE it BY NAME rather than picking one of the two wrong
// readings. Reading it as absent would silently drop input; reading it as a value
// re-emits a default the encoder cannot drop, which is the defect above.

/// The refusal an accessor makes for a member that is present but carries an
/// explicit JSON `null`. Unreachable from any canonical emission (§4 rule 4) and
/// from the two lenient positions, which `fieldOrIdentity` normalises — so it
/// fires only on input no conformant encoder produces, and naming the member is
/// the only honest thing left to say about it.
let private unreadableMember (name: string) : 'a =
  failwithf
    "Projection: wire member '%s' is present but carries an explicit null; a canonical emission spells absence structurally (WIRE_FORMAT §4 rule 4)"
    name

/// Absent reads at `fieldReq` — **absent-is-error** — sites, by member name.
/// A diagnostic accumulator, not projector state: nothing reads it during a
/// projection and the emission does not depend on it. It exists so the
/// classification above is CHECKED rather than asserted — see
/// `absentRequiredMembers`.
let mutable private absentRequired: Set<string> = Set.empty

/// The member names an **absent-is-error** read has found absent since this
/// module was loaded. The conformance harness projects the canonical node corpus
/// through every target and requires this to be empty; a name appearing here is a
/// site whose classification is wrong and which belongs on `fieldOpt`.
let absentRequiredMembers () : string list = absentRequired |> Set.toList

/// **absent-is-omit.** `None` for a member the wire does not carry, so the site
/// omits it and the re-encode carries no key either. An unreadable member is
/// refused by name (see above) rather than folded into the absent case.
let private fieldOpt (name: string) (v: JsonValue) : JsonValue option =
  match JsonValue.tryField name v with
  | None -> None
  | Some JNull -> unreadableMember name
  | Some x -> Some x

/// **absent-is-omit, spelled as the slot's IDENTITY DEFAULT.** The second of the
/// two absent-is-omit spellings, and which one a site takes is decided by the
/// TARGET model, not by the wire: several in-memory models have no absence at a
/// slot the wire omits at its default. A grid column's `format` / `width` are
/// the worked example — `@fuaran-ui/ops` dereferences `c.format.kind` and
/// `c.width.kind` unconditionally and then drops the value when it is the
/// identity, and `fuaran_py`'s `binding.state` takes `default_value` as a
/// REQUIRED positional — so at those sites the projection of an absent member is
/// the identity (`{ kind: 'None' }`, `{ kind: 'Auto' }`, `None`), which every
/// slot projector already yields for `JNull`, and the re-encode drops it.
///
/// This is the case `Tabs.activeIndex` is NOT, and the pair is the whole point of
/// the classification: there the ctor's own `?? Static 0` could not fire over an
/// explicit `Static`, so the default SURVIVED the re-encode and a key the fixture
/// does not have appeared in the bytes. Naming the two spellings apart is what
/// makes the difference readable at the site instead of discoverable only from a
/// failing fixture.
///
/// A member present as `null` reads as the identity too, rather than being
/// refused: `Binding.Static.value` and `Binding.State.defaultValue` are the two
/// positions a decoder accepts `null` at as §16 shorthand for absence
/// (WIRE_FORMAT §4 rule 4), and normalising it here is what the decoder does.
let private fieldOrIdentity (name: string) (v: JsonValue) : JsonValue =
  match JsonValue.tryField name v with
  | None
  | Some JNull -> JNull
  | Some x -> x

/// **absent-is-error.** Total, so the projector still never crashes, but the
/// absence is recorded by name for the conformance assertion described above.
let private fieldReq (name: string) (v: JsonValue) : JsonValue =
  match JsonValue.tryField name v with
  | None ->
    absentRequired <- Set.add name absentRequired
    JNull
  | Some JNull -> unreadableMember name
  | Some x -> x

let private optStr (name: string) (v: JsonValue) : string option =
  match JsonValue.tryField name v with
  | Some(JString s) -> Some s
  | _ -> None

let private optNum (name: string) (v: JsonValue) : float option =
  match JsonValue.tryField name v with
  | Some(JNumber n) -> Some n
  | _ -> None

let private strOf (name: string) (v: JsonValue) : string = optStr name v |> Option.defaultValue ""

let private numOf (name: string) (v: JsonValue) : float =
  optNum name v |> Option.defaultValue 0.0

let private boolOf (name: string) (v: JsonValue) : bool =
  match JsonValue.tryField name v with
  | Some(JBool b) -> b
  | _ -> false

let private arrOf (name: string) (v: JsonValue) : JsonValue list =
  match JsonValue.tryField name v with
  | Some(JArray xs) -> xs
  | _ -> []

let private membersOf (name: string) (v: JsonValue) : (string * JsonValue) list =
  match JsonValue.tryField name v with
  | Some(JObject ms) -> ms
  | _ -> []

let private strItem (v: JsonValue) : string =
  match v with
  | JString s -> qs s
  | _ -> qs ""

let private numItem (v: JsonValue) : string =
  match v with
  | JNumber n -> numLit n
  | _ -> "0"

/// A `JsonValue` payload as a TS literal (Notify payloads, Custom props, …).
let rec private tsJson (v: JsonValue) : string =
  match v with
  | JNull -> "null"
  | JBool b -> boolLit b
  | JNumber n -> numLit n
  | JString s -> qs s
  | JArray xs -> "[" + (xs |> List.map tsJson |> String.concat ", ") + "]"
  | JObject ms ->
    if List.isEmpty ms then
      "{}"
    else
      "{ "
      + (ms |> List.map (fun (k, x) -> jsKey k + ": " + tsJson x) |> String.concat ", ")
      + " }"

/// A `Binding.Static` payload: primitives faithfully; wire `null` re-emerges as
/// `undefined` (the encoder maps it back to wire `null`); arrays / objects are
/// projected faithfully too (real collections — sparkline seqs, map markers —
/// carry real data on the wire; the decoder's opaque sentinel is the *string*
/// `"<opaque>"`, which stays a quoted string here and re-encodes identically).
let rec private tsStaticValue (v: JsonValue) : string =
  match v with
  | JNull -> "undefined"
  | JBool b -> boolLit b
  | JNumber n -> numLit n
  | JString s -> qs s
  | JArray xs -> "[" + (xs |> List.map tsStaticValue |> String.concat ", ") + "]"
  | JObject ms ->
    "{ "
    + (ms
       |> List.map (fun (k, x) -> jsKey k + ": " + tsStaticValue x)
       |> String.concat ", ")
    + " }"

// ── Compute layer (Binding.Transform) – DataSource / ColExpr / Cell ──────────

let private tsCellLit (v: JsonValue) : string =
  match dollarType v with
  | Some "Int" -> tsInline [ "kind", qs "Int"; "value", numLit (numOf "value" v) ]
  | Some "Float" -> tsInline [ "kind", qs "Float"; "value", numLit (numOf "value" v) ]
  | Some "Bool" -> tsInline [ "kind", qs "Bool"; "value", boolLit (boolOf "value" v) ]
  | Some "Str" -> tsInline [ "kind", qs "Str"; "value", qs (strOf "value" v) ]
  | Some "Date" -> tsInline [ "kind", qs "Date"; "value", qs (strOf "value" v) ]
  | Some "Timestamp" -> tsInline [ "kind", qs "Timestamp"; "value", qs (strOf "value" v) ]
  | _ -> "{ kind: 'Null' }"

let rec private tsColExpr (v: JsonValue) : string =
  match dollarType v with
  | Some "lit" -> tsInline [ "kind", qs "lit"; "cell", tsCellLit (fieldReq "cell" v) ]
  | Some "binary" ->
    tsInline
      [ "kind", qs "binary"
        "op", qs (strOf "op" v)
        "left", tsColExpr (fieldReq "left" v)
        "right", tsColExpr (fieldReq "right" v) ]
  | Some "not" -> tsInline [ "kind", qs "not"; "expr", tsColExpr (fieldReq "expr" v) ]
  | Some "coalesce" ->
    tsInline
      [ "kind", qs "coalesce"
        "exprs", "[" + (arrOf "exprs" v |> List.map tsColExpr |> String.concat ", ") + "]" ]
  | Some "case" ->
    let cases =
      arrOf "cases" v
      |> List.map (fun c -> tsInline [ "when", tsColExpr (fieldReq "when" c); "then", tsColExpr (fieldReq "then" c) ])
      |> String.concat ", "

    tsInline
      [ "kind", qs "case"
        "cases", "[" + cases + "]"
        "else", tsColExpr (fieldReq "else" v) ]
  | Some "cast" ->
    tsInline
      [ "kind", qs "cast"
        "type", qs (strOf "type" v)
        "expr", tsColExpr (fieldReq "expr" v) ]
  | Some "apply" ->
    tsInline
      [ "kind", qs "apply"
        "fn", qs (strOf "fn" v)
        "args", "[" + (arrOf "args" v |> List.map tsColExpr |> String.concat ", ") + "]" ]
  | Some "param" -> tsInline [ "kind", qs "param"; "name", qs (strOf "name" v) ]
  | Some "isNull" -> tsInline [ "kind", qs "isNull"; "expr", tsColExpr (fieldReq "expr" v) ]
  // The wire spells BOTH membership forms `in`; the in-memory model splits them
  // by which payload is carried — a literal `items` list, or the `param` naming
  // a transform parameter resolved at evaluation time.
  | Some "in" ->
    (match optStr "param" v with
     | Some p -> tsInline [ "kind", qs "inParam"; "expr", tsColExpr (fieldReq "expr" v); "param", qs p ]
     | None ->
       tsInline
         [ "kind", qs "in"
           "expr", tsColExpr (fieldReq "expr" v)
           "items", "[" + (arrOf "items" v |> List.map tsColExpr |> String.concat ", ") + "]" ])
  | _ -> tsInline [ "kind", qs "col"; "name", qs (strOf "name" v) ]

/// Reconstruct the in-memory `DataSource` from its columnar wire form
/// (`values` + the validity mask + the schema's column type).
let private tsDataSource (v: JsonValue) : string =
  match optStr "ref" v with
  | Some name -> tsInline [ "kind", qs "Ref"; "name", qs name ]
  | None ->
    let schema = arrOf "schema" v
    let cols = membersOf "columns" v

    let schemaLits =
      schema
      |> List.map (fun e -> tsInline [ "name", qs (strOf "name" e); "type", qs (strOf "type" e) ])

    let columnLits =
      schema
      |> List.map (fun e ->
        let name = strOf "name" e
        let ty = strOf "type" e

        let col =
          cols
          |> List.tryFind (fun (k, _) -> k = name)
          |> Option.map snd
          |> Option.defaultValue JNull

        let validity = arrOf "validity" col

        let cells =
          arrOf "values" col
          |> List.mapi (fun i value ->
            match List.tryItem i validity with
            | Some(JBool false) -> "{ kind: 'Null' }"
            | _ ->
              match ty, value with
              | "int", JNumber n -> tsInline [ "kind", qs "Int"; "value", numLit n ]
              | "float", JNumber n -> tsInline [ "kind", qs "Float"; "value", numLit n ]
              | "bool", JBool b -> tsInline [ "kind", qs "Bool"; "value", boolLit b ]
              | "date", JString s -> tsInline [ "kind", qs "Date"; "value", qs s ]
              | "timestamp", JString s -> tsInline [ "kind", qs "Timestamp"; "value", qs s ]
              | _, JString s -> tsInline [ "kind", qs "Str"; "value", qs s ]
              | _ -> "{ kind: 'Null' }")

        tsInline
          [ "name", qs name
            "type", qs ty
            "cells", "[" + String.concat ", " cells + "]" ])

    tsInline
      [ "kind", qs "Embedded"
        "table",
        tsInline
          [ "schema", "[" + String.concat ", " schemaLits + "]"
            "columns", "[" + String.concat ", " columnLits + "]" ] ]

let private tsTransformStep (v: JsonValue) : string =
  let pair (p: JsonValue) =
    tsInline [ "a", qs (strOf "a" p); "b", qs (strOf "b" p) ]

  let sortKey (s: JsonValue) =
    tsInline [ "col", qs (strOf "col" s); "dir", qs (strOf "dir" s) ]

  let strArr (name: string) =
    "[" + (arrOf name v |> List.map strItem |> String.concat ", ") + "]"

  match dollarType v with
  | Some "filter" -> tsInline [ "kind", qs "filter"; "pred", tsColExpr (fieldReq "pred" v) ]
  | Some "project" ->
    tsInline
      [ "kind", qs "project"
        "cols", "[" + (arrOf "cols" v |> List.map pair |> String.concat ", ") + "]" ]
  | Some "derive" ->
    tsInline
      [ "kind", qs "derive"
        "name", qs (strOf "name" v)
        "expr", tsColExpr (fieldReq "expr" v) ]
  | Some "groupBy" ->
    let aggs =
      arrOf "aggs" v
      |> List.map (fun a ->
        tsInline
          [ "name", qs (strOf "name" a)
            "fn", qs (strOf "fn" a)
            "of", qs (strOf "of" a) ])
      |> String.concat ", "

    tsInline [ "kind", qs "groupBy"; "keys", strArr "keys"; "aggs", "[" + aggs + "]" ]
  | Some "join" ->
    tsInline
      [ "kind", qs "join"
        "source", tsDataSource (fieldReq "source" v)
        "on", "[" + (arrOf "on" v |> List.map pair |> String.concat ", ") + "]"
        "how", qs (strOf "how" v) ]
  | Some "window" ->
    tsInline
      [ "kind", qs "window"
        "spec",
        tsInline
          [ "partitionBy", strArr "partitionBy"
            "orderBy", "[" + (arrOf "orderBy" v |> List.map sortKey |> String.concat ", ") + "]"
            "fn", qs (strOf "fn" v)
            "of", qs (strOf "of" v)
            "as", qs (strOf "as" v) ] ]
  | Some "pivot" ->
    tsInline
      [ "kind", qs "pivot"
        "spec",
        tsInline
          [ "index", strArr "index"
            "on", qs (strOf "on" v)
            "values", qs (strOf "values" v)
            "agg", qs (strOf "agg" v) ] ]
  | Some "unpivot" ->
    tsInline
      [ "kind", qs "unpivot"
        "idVars", strArr "idVars"
        "valueVars", strArr "valueVars" ]
  | Some "sort" ->
    tsInline
      [ "kind", qs "sort"
        "by", "[" + (arrOf "by" v |> List.map sortKey |> String.concat ", ") + "]" ]
  | Some "limit" ->
    tsInline
      [ "kind", qs "limit"
        "n", numLit (numOf "n" v)
        "offset", numLit (numOf "offset" v) ]
  | Some "union" -> tsInline [ "kind", qs "union"; "source", tsDataSource (fieldReq "source" v) ]
  | _ -> "{ kind: 'distinct' }"

// ── Bindings / actions / text / formats ───────────────────────────────────────

let private tsFormatIntent (v: JsonValue) : string =
  match dollarType v with
  | Some "Currency" -> tsInline [ "kind", qs "Currency"; "isoCode", qs (strOf "isoCode" v) ]
  | Some "Percent" ->
    (match optNum "decimals" v with
     | Some d -> tsInline [ "kind", qs "Percent"; "decimals", numLit d ]
     | None -> "{ kind: 'Percent' }")
  | Some "Date" -> tsInline [ "kind", qs "Date"; "dateStyle", qs (strOf "dateStyle" v) ]
  | Some "RelativeTime" -> tsInline [ "kind", qs "RelativeTime"; "unit", qs (strOf "unit" v) ]
  | Some "Duration" ->
    tsInline
      [ "kind", qs "Duration"
        "unit", qs (strOf "unit" v)
        "style", qs (strOf "style" v) ]
  // Phase 1533 — elapsed-time-since. `unit` is omitted when absent, and the
  // absence IS the auto-selection request rather than a default to spell out.
  | Some "Since" ->
    (match optStr "unit" v with
     | Some u -> tsInline [ "kind", qs "Since"; "unit", qs u ]
     | None -> "{ kind: 'Since' }")
  | _ ->
    (match optNum "decimals" v with
     | Some d -> tsInline [ "kind", qs "Number"; "decimals", numLit d ]
     | None -> "{ kind: 'Number' }")

let private tsLocaleSource (v: JsonValue) : string =
  match dollarType v with
  | Some "Explicit" -> tsInline [ "kind", qs "Explicit"; "tag", qs (strOf "tag" v) ]
  | _ -> "{ kind: 'Ambient' }"

let private tsFlushTrigger (v: JsonValue) : string =
  match dollarType v with
  | Some "OnDebounce" -> tsInline [ "kind", qs "OnDebounce"; "milliseconds", numLit (numOf "milliseconds" v) ]
  | Some other -> tsInline [ "kind", qs other ]
  | None -> "{ kind: 'OnBlur' }"

/// The `args` of a `Binding.Invoke` / `Action.Invoke` – scalar (addr, value) pairs.
let private tsInvokeArgs (v: JsonValue) : string =
  "["
  + (arrOf "args" v
     |> List.map (fun a -> tsInline [ "addr", qs (strOf "addr" a); "value", qs (strOf "value" a) ])
     |> String.concat ", ")
  + "]"

/// Where a binding's element type is a collection, the decoder's "<opaque>"
/// sentinel projects as an empty ARRAY (types against the slot); scalar slots
/// keep the literal sentinel string. Both re-encode to "<opaque>" identically.
[<RequireQualifiedAccess>]
type private Opq =
  | Scalar
  | Collection

let rec private tsBinding (opq: Opq) (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JString "<opaque>") ->
       (match opq with
        | Opq.Collection -> "binding.static([])"
        | Opq.Scalar -> "binding.static('<opaque>')")
     | Some value -> "binding.static(" + tsStaticValue value + ")"
     | None -> "binding.static(undefined)")
  | Some "Query" ->
    let dep = arrOf "dependsOn" v

    if List.isEmpty dep then
      "binding.query(" + qs (strOf "name" v) + ", () => undefined)"
    else
      "{ kind: 'Query', name: "
      + qs (strOf "name" v)
      + ", dependsOn: ["
      + (dep |> List.map strItem |> String.concat ", ")
      + "], accessor: () => undefined }"
  | Some "Filter" -> "binding.filter(" + qs (strOf "name" v) + ")"
  | Some "Selection" ->
    // 0.2.9/0.2.10 — `defaultValue` + `field` (declarative row-field projection)
    // ride the wire when present; omitted for the pre-629 minimal form.
    "{ kind: 'Selection', nodeId: "
    + qs (strOf "nodeId" v)
    + ", accessor: () => undefined"
    + (match JsonValue.tryField "defaultValue" v with
       | Some d -> ", defaultValue: " + tsStaticValue d
       | None -> "")
    + (match optStr "field" v with
       | Some f -> ", field: " + qs f
       | None -> "")
    + " }"
  | Some "State" ->
    // `defaultValue` is absent-is-omit spelled as the IDENTITY DEFAULT: the ctor
    // declares the argument as required, so the absence is spelled `undefined`
    // and the encoder drops it (`b.defaultValue == null ? [] : …`) — and the
    // `Switch.on` short spelling keys off the same `undefined`. A §16 lenient
    // `null` here reads as the same absence, which is what the decoder does
    // with it.
    "binding.state("
    + qs (strOf "key" v)
    + ", "
    + tsStaticValue (fieldOrIdentity "defaultValue" v)
    + ")"
  | Some "Computed" -> "binding.computed(() => undefined)"
  | Some "I18n" ->
    let args = membersOf "args" v

    if List.isEmpty args then
      "binding.i18n(" + qs (strOf "key" v) + ")"
    else
      let rendered =
        args
        |> List.map (fun (k, b) -> jsKey k + ": " + tsBinding Opq.Scalar b)
        |> String.concat ", "

      "binding.i18n(" + qs (strOf "key" v) + ", { " + rendered + " })"
  | Some "Now" ->
    // Phase 765 — the host-furnished instant. `project` is erased on encode
    // (the wire form is the bare `{"$type":"Now"}`), so the identity keeps
    // the round-trip byte-exact; no smart-ctor exists in the TS tier yet.
    // Phase 1533 — the declared `grain` DOES ride the wire, and only when the
    // author named one, so it projects as present-or-absent, never as a default.
    "{ kind: 'Now', project: (iso) => iso"
    + (match optStr "grain" v with
       | Some g -> ", grain: " + qs g
       | None -> "")
    + " }"
  | Some "Local" ->
    // The buffer has two commit spellings and they are mutually exclusive: a
    // host `onCommit` closure (`binding.local`'s ctor, which requires one), or
    // the DECLARATIVE `commitTo` + `codec` pair a decoding host builds its own
    // `format` / `parse` from. No ctor reaches the second — passing an
    // `onCommit` would make the document a decode refusal — so it takes the
    // literal form. `format` and `parse` ride the wire as closure sentinels
    // either way, so only `parse` need be spelled.
    let codec = JsonValue.tryField "codec" v
    let commitTo = optStr "commitTo" v

    if codec.IsSome || commitTo.IsSome then
      "{ kind: 'Local', local: { initialFrom: "
      + tsBinding Opq.Scalar (fieldReq "initialFrom" v)
      + ", flushOn: "
      + tsFlushTrigger (fieldReq "flushOn" v)
      + ", parse: () => ({ ok: false, error: '' })"
      + (match codec with
         | Some c -> ", codec: " + tsFormatIntent c
         | None -> "")
      + (match commitTo with
         | Some k -> ", commitTo: " + qs k
         | None -> "")
      + " } }"
    else
      "binding.local("
      + tsBinding Opq.Scalar (fieldReq "initialFrom" v)
      + ", "
      + tsFlushTrigger (fieldReq "flushOn" v)
      + ", () => action.chain([]), () => ({ ok: false, error: '' }))"
  | Some "Format" ->
    "binding.format("
    + tsBinding Opq.Scalar (fieldReq "source" v)
    + ", "
    + tsFormatIntent (fieldReq "format" v)
    + ", "
    + tsLocaleSource (fieldReq "locale" v)
    + ")"
  | Some "Transform" ->
    // Raw `Binding.Transform` literal (not `binding.transform`, whose ctor takes
    // no `params`): `params` binds `ColExpr.Param` names to scalar sources (0.2.x
    // Phase 424), omitted-when-empty so a param-free Transform is byte-identical.
    let paramsPart = tsBindingParams v

    // The `source` slot is a `TransformSource` DU, not a bare `DataSource`:
    // `Data` carries the columnar / `ref` table, `Live` preserves a
    // binding-shaped source (State / Selection / Query) so a runtime
    // re-evaluates when its channel changes. The wire discriminates
    // structurally — a `Live` source rides as a Binding case object (it has a
    // `$type`), a `Data` source as the bare table — and `initial` is the
    // decode-time snapshot, never encoded, so the empty table round-trips.
    let srcV = fieldReq "source" v

    let sourcePart =
      match dollarType srcV with
      | Some _ ->
        "{ kind: 'Live', binding: "
        + tsBinding Opq.Collection srcV
        + ", initial: { kind: 'Embedded', table: { schema: [], columns: [] } } }"
      | None -> "{ kind: 'Data', source: " + tsDataSource srcV + " }"

    "{ kind: 'Transform', source: "
    + sourcePart
    + ", pipeline: ["
    + (arrOf "pipeline" v |> List.map tsTransformStep |> String.concat ", ")
    + "]"
    + paramsPart
    + " }"
  | Some "Invoke" -> "binding.invoke(" + qs (strOf "capabilityId" v) + ", " + tsInvokeArgs v + ")"
  // Phase 1538 — a scalar `ColExpr` evaluated over `params`, with no table
  // anywhere: the arithmetic half of `Transform` addressed to a value slot. No
  // ctor carries it, so it takes the literal form, and `params` is the same
  // omitted-when-empty list the Transform arm carries (a param-free expression
  // is a closed computation over literals).
  | Some "Expr" ->
    "{ kind: 'Expr', expr: "
    + tsColExpr (fieldReq "expr" v)
    + tsBindingParams v
    + " }"
  | _ -> "binding.static(undefined)"

/// The `params` list a `Binding.Transform` / `Binding.Expr` binds its
/// `ColExpr.Param` names through — omitted when empty, so a param-free binding
/// is byte-identical to its pre-`params` form.
and private tsBindingParams (v: JsonValue) : string =
  match arrOf "params" v with
  | [] -> ""
  | ps ->
    ", params: ["
    + (ps
       |> List.map (fun p ->
         "{ from: "
         + tsBinding Opq.Scalar (fieldReq "from" p)
         + ", name: "
         + qs (strOf "name" p)
         + " }")
       |> String.concat ", ")
    + "]"

/// The explicit `TextSource` object form – for record fields typed as raw
/// `TextSource` (FormField.label, TabHeader.label, FilterSpec.label, SelectOption
/// label, Fact/Drawing text), which the smart ctors do NOT coerce from a bare
/// string. The 0.2.0 canonical `Literal` is a BARE JSON STRING; the legacy
/// `{"$type":"Literal","text":…}` envelope stays decode-accepted (fallback read).
let private tsTextSourceLit (v: JsonValue) : string =
  match v with
  | JString s -> "{ kind: 'Literal', value: " + qs s + " }"
  | _ ->
    match dollarType v with
    | Some "Bound" ->
      "{ kind: 'Bound', binding: "
      + tsBinding Opq.Scalar (fieldReq "binding" v)
      + " }"
    | Some "I18n" ->
      let args =
        membersOf "args" v
        |> List.map (fun (k, x) -> jsKey k + ": " + tsJson x)
        |> String.concat ", "

      "{ kind: 'I18n', key: " + qs (strOf "key" v) + ", args: { " + args + " } }"
    | _ -> "{ kind: 'Literal', value: " + qs (strOf "text" v) + " }"

/// A `TextInput` slot: the canonical bare-string `Literal` (or the legacy
/// envelope) collapses to the bare string the smart ctor coerces; `Bound` /
/// `I18n` emit the explicit `TextSource` object.
let private tsTextInput (v: JsonValue) : string =
  match v with
  | JString s -> qs s
  | _ ->
    match dollarType v with
    | Some "Literal" -> qs (strOf "text" v)
    | _ -> tsTextSourceLit v

/// A `SelectOption` (Choice / SegmentedChoice / Select) — the `label` rides the
/// wire as a bare string but is a `TextSource` in memory, so it must be wrapped.
let private tsSelectOption (o: JsonValue) : string =
  tsInline [ "label", tsTextSourceLit (fieldReq "label" o); "value", qs (strOf "value" o) ]

/// A collection binding whose element type is `SelectOption` — the `Static`
/// array form wraps each option's `label` as a `TextSource`; everything else
/// defers to the generic collection binding.
let private tsOptionsBinding (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JArray opts) ->
       "binding.static(["
       + (opts |> List.map tsSelectOption |> String.concat ", ")
       + "])"
     | Some(JString "<opaque>") -> "binding.static([])"
     | _ -> tsBinding Opq.Collection v)
  | _ -> tsBinding Opq.Collection v

/// A `Map` marker: `{ label, latitude, longitude }` where the `label` rides the
/// wire as a bare string but is a `TextSource` in memory.
let private tsMarker (m: JsonValue) : string =
  match m with
  | JObject ms ->
    "{ "
    + (ms
       |> List.map (fun (k, x) ->
         if k = "label" then
           "label: " + tsTextSourceLit x
         else
           jsKey k + ": " + tsStaticValue x)
       |> String.concat ", ")
    + " }"
  | _ -> tsStaticValue m

/// A `Map`'s marker-collection source binding (marker `label` is a `TextSource`).
let private tsMarkerBinding (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JArray ms) -> "binding.static([" + (ms |> List.map tsMarker |> String.concat ", ") + "])"
     | Some(JString "<opaque>") -> "binding.static([])"
     | _ -> tsBinding Opq.Collection v)
  | _ -> tsBinding Opq.Collection v

let rec private tsAction (v: JsonValue) : string =
  match dollarType v with
  | Some "Dispatch" -> "action.dispatch(0)"
  | Some "Call" ->
    // Phase 428 — `into` (declarative result target: State / Query write-back) and
    // `onResult` (closure) are sibling optional slots; omitted when absent.
    let intoPart =
      match JsonValue.tryField "into" v with
      | Some intoV ->
        (match dollarType intoV with
         | Some "State" -> ", into: { kind: 'State', key: " + qs (strOf "key" intoV) + " }"
         | Some "Query" -> ", into: { kind: 'Query', name: " + qs (strOf "name" intoV) + " }"
         | _ -> "")
      | None -> ""

    let onResultPart =
      match JsonValue.tryField "onResult" v with
      | Some _ -> ", onResult: () => 0"
      | None -> ""

    "{ kind: 'Call', endpoint: "
    + qs (strOf "endpoint" v)
    + intoPart
    + onResultPart
    + " }"
  | Some "Notify" ->
    "action.notify("
    + qs (strOf "channel" v)
    + ", "
    + tsJson (fieldReq "payload" v)
    + ")"
  // Phase 1536 — the route is a `TextSource`, so a tree can name a destination
  // it computes from what the reader selected. `target` is omitted at `Self`,
  // and a literal route in the current context keeps the short `navigate`
  // spelling (identical bytes, and the commonest intent stays readable).
  | Some "Navigate" ->
    let route = fieldReq "route" v

    (match route, optStr "target" v with
     | JString s, (None | Some "Self") -> "action.navigate(" + qs s + ")"
     | _, Some t when t <> "Self" -> "action.navigateTo(" + tsTextSourceLit route + ", " + qs t + ")"
     | _, _ -> "action.navigateTo(" + tsTextSourceLit route + ")")
  | Some "SetState" ->
    // `value` (a literal) and `valueFrom` (a Binding read at dispatch time) are
    // sibling slots with distinct ctors; `valueFrom` wins when present.
    (match JsonValue.tryField "valueFrom" v with
     | Some src ->
       "action.setStateFrom("
       + qs (strOf "key" v)
       + ", "
       + tsBinding Opq.Scalar src
       + ")"
     | None ->
       "action.setState("
       + qs (strOf "key" v)
       + ", "
       + tsJson (fieldReq "value" v)
       + ")")
  | Some "AiTool" ->
    "action.aiTool("
    + qs (strOf "toolName" v)
    + ", "
    + tsJson (fieldReq "args" v)
    + ")"
  | Some "Chain" ->
    "action.chain(["
    + (arrOf "ops" v |> List.map tsAction |> String.concat ", ")
    + "])"
  | Some "CommitLocal" -> "action.commitLocal(" + qs (strOf "nodeId" v) + ")"
  // The clipboard payload is a `TextSource`, not a bare string: the canonical
  // `Literal` form IS the bare JSON string, but a `Bound` payload rides as the
  // envelope and must project as one (reading it with `strOf` erased it to '').
  | Some "WriteToClipboard" -> "action.writeToClipboard(" + tsTextInput (fieldReq "text" v) + ")"
  // Phase 1124 — the reader's own print dialogue. It takes nothing, and the
  // encoder refuses any member beside `$type`.
  | Some "Print" -> "action.print()"
  // Phase 1537 — ask, then continue. The first case to recurse into NAMED
  // members rather than a list, so a reader looking only for `ops` misses both
  // continuations; `onCancel` is omitted when absent, and its absence means
  // nothing happens rather than some substituted default.
  | Some "Confirm" ->
    "action.confirm("
    + tsTextInput (fieldReq "prompt" v)
    + ", "
    + tsAction (fieldReq "onConfirm" v)
    + (match JsonValue.tryField "onCancel" v with
       | Some c -> ", " + tsAction c
       | None -> "")
    + ")"
  // Phase 1537 — a bare node id, never a `TextSource`: it addresses a node in
  // this document, which the author wrote.
  | Some "Focus" -> "action.focus(" + qs (strOf "nodeId" v) + ")"
  | Some "ReadFileBody" ->
    "action.readFileBody({ id: "
    + qs (strOf "fileRef" v)
    + " }, "
    + qs (strOf "encoding" v)
    + ", () => 0)"
  | Some "Invoke" -> "action.invoke(" + qs (strOf "capabilityId" v) + ", " + tsInvokeArgs v + ")"
  | _ -> "action.chain([])"

let private tsCellFormat (v: JsonValue) : string =
  match dollarType v with
  | Some "Number" ->
    (match optNum "decimals" v with
     | Some d -> "format.number(" + numLit d + ")"
     | None -> "format.number()")
  | Some "Currency" -> "format.currency(" + qs (strOf "code" v) + ")"
  | Some "Percent" ->
    (match optNum "decimals" v with
     | Some d -> "format.percent(" + numLit d + ")"
     | None -> "format.percent()")
  | Some "SignificantDigits" -> "format.significantDigits(" + numLit (numOf "digits" v) + ")"
  | Some "Date" -> "format.date(" + qs (strOf "format" v) + ")"
  // `Duration` / `RelativeTime` have no `format.*` helper in the TS tier, so
  // they project as the typed literal (the `Custom` precedent below).
  | Some "Duration" ->
    tsInline
      [ "kind", qs "Duration"
        "unit", qs (strOf "unit" v)
        "style", qs (strOf "style" v) ]
  | Some "RelativeTime" -> tsInline [ "kind", qs "RelativeTime"; "unit", qs (strOf "unit" v) ]
  | Some "Custom" -> "{ kind: 'Custom', fn: () => '' }"
  | _ -> "format.none()"

// ── Form fields / filters / grid columns / tab headers ────────────────────────

// 0.2.0 filters-unification: a filter chip's control is an ordinary
// `FormFieldKind` auto-bound to its own filter key; a form field's control is a
// `FormFieldKind` auto-bound to the field's own `State` slot. Both share the
// vocabulary. When the wire OMITS the control's `value`, it is the auto-binding
// (recovered here so the re-encode omits it identically); an explicit `value`
// is projected faithfully. Handlers ride the wire only when present (Phase 426):
// a present handler → the `"<closure>"` sentinel; an omitted one arms the
// renderer's write-back default.
[<RequireQualifiedAccess>]
type private AutoBind =
  | Form of fieldId: string
  | Filter of name: string

/// The per-control auto-binding default value (mirrors `controlValueDefaults`).
let private ctrlDefault (kind: string) : string =
  match kind with
  | "Number"
  // Phase 1122 — a rating reads the numeric control default.
  | "Rating"
  | "RangedNumber" -> "0"
  // Phase 766 — Toggle shares Checkbox's boolean value slot.
  | "Checkbox"
  | "Toggle" -> "false"
  // Phase 1119 — a combobox reads the choice control default (unset).
  | "Combobox"
  | "Choice"
  | "SegmentedChoice" -> "undefined"
  | "Range" -> "[0, 0]"
  | "DateRange" -> "['', '']"
  // Phase 1121 — an auto-bound token field starts with no chips at all.
  | "Tokens" -> "[]"
  // Phase 1130 — the unset swatch: `#000000` is the native colour input's own
  // default, and the one `#rrggbb` form the control can hold.
  | "Color" -> "'#000000'"
  | _ -> "''" // Text / TextArea / Date

let private tsAutoBindValue (ab: AutoBind) (kind: string) : string =
  match ab with
  | AutoBind.Form id -> "binding.state(" + qs id + ", " + ctrlDefault kind + ")"
  | AutoBind.Filter name -> "binding.filter(" + qs name + ")"

/// The control's `value` binding: the wire's explicit value if present, else the
/// reconstructed auto-binding. `Range`'s explicit value rides as a `{min,max}`
/// object (the `Static` pair) and re-hydrates as `binding.static([min, max])`;
/// `DateRange`'s rides as `{from,to}` (the 0.7.0 string pair) and re-hydrates as
/// `binding.static([from, to])`.
let private tsFieldValue (ab: AutoBind) (kind: string) (v: JsonValue) : string =
  match JsonValue.tryField "value" v with
  | Some valV ->
    match kind with
    | "Range" when (dollarType valV).IsNone ->
      "binding.static(["
      + numLit (numOf "min" valV)
      + ", "
      + numLit (numOf "max" valV)
      + "])"
    | "DateRange" when (dollarType valV).IsNone ->
      "binding.static([" + qs (strOf "from" valV) + ", " + qs (strOf "to" valV) + "])"
    | "DateRange" when
      (dollarType valV) = Some "State"
      && (match JsonValue.tryField "defaultValue" valV with
          | Some d -> (dollarType d).IsNone && (JsonValue.tryField "from" d).IsSome
          | None -> false)
      ->
      // A State binding whose defaultValue is the wire's `{from,to}` pair: the
      // in-memory shape is the TUPLE (the `Range` precedent), so the generic
      // object-literal projection would hand the encoder a shape it cannot take.
      let d = fieldReq "defaultValue" valV

      "binding.state("
      + qs (strOf "key" valV)
      + ", ["
      + qs (strOf "from" d)
      + ", "
      + qs (strOf "to" d)
      + "])"
    | _ -> tsBinding Opq.Scalar valV
  | None -> tsAutoBindValue ab kind

/// The `{min, max, step}` constraints object read from the kind's top-level
/// fields (numeric for `Range`/`RangedNumber`, ISO strings for `Date`).
let private tsConstraints (isDate: bool) (v: JsonValue) : string =
  let minMax name =
    if isDate then
      match optStr name v with
      | Some s -> [ name, qs s ]
      | None -> []
    else
      match optNum name v with
      | Some n -> [ name, numLit n ]
      | None -> []

  tsInline (
    minMax "min"
    @ minMax "max"
    @ (match optNum "step" v with
       | Some s -> [ "step", numLit s ]
       | None -> [])
  )

let private tsFieldKindLit (ab: AutoBind) (v: JsonValue) : string =
  let kind = dollarType v |> Option.defaultValue "Text"
  let value = tsFieldValue ab kind v

  let handler name =
    match JsonValue.tryField name v with
    | Some _ -> [ name, "() => action.chain([])" ]
    | None -> []

  match kind with
  | "Number" -> tsInline ([ "kind", qs "Number" ] @ handler "onChange" @ [ "value", value ])
  | "Checkbox" -> tsInline ([ "kind", qs "Checkbox" ] @ handler "onToggle" @ [ "value", value ])
  // Phase 766 — Toggle shares Checkbox's shape exactly (same onToggle handler,
  // same boolean value slot); only the kind discriminator differs.
  | "Toggle" -> tsInline ([ "kind", qs "Toggle" ] @ handler "onToggle" @ [ "value", value ])
  | "Choice" ->
    tsInline (
      [ "kind", qs "Choice" ]
      @ handler "onChange"
      @ [ "options", tsOptionsBinding (fieldReq "options" v); "value", value ]
    )
  | "SegmentedChoice" ->
    tsInline (
      [ "kind", qs "SegmentedChoice" ]
      @ handler "onChange"
      @ [ "options", tsOptionsBinding (fieldReq "options" v)
          "orientation", qs (strOf "orientation" v)
          "value", value ]
    )
  | "TextArea" ->
    tsInline (
      [ "kind", qs "TextArea" ]
      @ handler "onChange"
      @ [ "rows", numLit (numOf "rows" v); "value", value ]
    )
  | "Range" ->
    tsInline (
      [ "kind", qs "Range" ]
      @ handler "onChange"
      @ [ "value", value; "constraints", tsConstraints false v ]
    )
  | "RangedNumber" ->
    tsInline (
      [ "kind", qs "RangedNumber" ]
      @ handler "onChange"
      @ [ "value", value; "constraints", tsConstraints false v ]
    )
  | "Date" ->
    tsInline (
      [ "kind", qs "Date" ]
      @ handler "onChange"
      @ [ "value", value
          "variant", qs (strOf "variant" v)
          "constraints", tsConstraints true v ]
    )
  | "DateRange" ->
    tsInline (
      [ "kind", qs "DateRange" ]
      @ handler "onChange"
      @ [ "value", value
          "variant", qs (strOf "variant" v)
          "constraints", tsConstraints true v ]
    )
  // Phase 1130 — the colour swatch: the plain value/handler pair.
  | "Color" -> tsInline ([ "kind", qs "Color" ] @ handler "onChange" @ [ "value", value ])
  // Phase 1122 — the rating scale. `max` is required in memory (the encoder
  // always writes it); `allowHalf` is false-by-default and omitted there.
  | "Rating" ->
    tsInline (
      [ "kind", qs "Rating" ]
      @ (if boolOf "allowHalf" v then [ "allowHalf", "true" ] else [])
      @ [ "max", numLit (numOf "max" v) ]
      @ handler "onChange"
      @ [ "value", value ]
    )
  // Phase 1119 — the free-text-or-pick combobox. `options` is required in
  // memory; `allowFreeText` defaults to FALSE, so an absent wire field is false.
  | "Combobox" ->
    tsInline (
      [ "kind", qs "Combobox"; "allowFreeText", boolLit (boolOf "allowFreeText" v) ]
      @ handler "onChange"
      @ [ "options", tsOptionsBinding (fieldReq "options" v); "value", value ]
    )
  // Phase 1121 — the chip list. `allowFreeText` defaults to TRUE here (the
  // encoder writes it only when false), so an ABSENT wire field is `true` —
  // the one place in the field vocabulary where absence is not `false`.
  | "Tokens" ->
    tsInline (
      [ "kind", qs "Tokens"
        "allowFreeText",
        boolLit (
          match JsonValue.tryField "allowFreeText" v with
          | Some(JBool b) -> b
          | _ -> true
        ) ]
      @ handler "onChange"
      @ (match JsonValue.tryField "suggestions" v with
         | Some s -> [ "suggestions", tsOptionsBinding s ]
         | None -> [])
      @ [ "value", value ]
    )
  | _ -> tsInline ([ "kind", qs "Text" ] @ handler "onChange" @ [ "value", value ])

/// A `FormField`'s declared constraint (Phase 864) — the ACCEPTED SET, where
/// `FormFieldKind` names the CONTROL. Every slot is optional and the whole
/// record is optional on the field, so a form authored before the addition
/// projects byte-identically. `compare.against` is an ordinary `Binding` (that
/// IS the cross-field mechanism — the auto-bind rule puts each field's value in
/// State under its own id), and `message` is a `TextSource`, not a bare string.
let private tsFieldRule (v: JsonValue) : string =
  tsInline (
    (match optStr "format" v with
     | Some f -> [ "format", qs f ]
     | None -> [])
    @ (match optStr "pattern" v with
       | Some p -> [ "pattern", qs p ]
       | None -> [])
    @ (match optNum "minLength" v with
       | Some n -> [ "minLength", numLit n ]
       | None -> [])
    @ (match optNum "maxLength" v with
       | Some n -> [ "maxLength", numLit n ]
       | None -> [])
    @ (match JsonValue.tryField "compare" v with
       | Some c ->
         [ "compare",
           tsInline
             [ "op", qs (strOf "op" c)
               "against", tsBinding Opq.Scalar (fieldReq "against" c) ] ]
       | None -> [])
    @ (match JsonValue.tryField "message" v with
       | Some m -> [ "message", tsTextSourceLit m ]
       | None -> [])
  )

let private tsFormField (depth: int) (v: JsonValue) : string =
  let id = strOf "id" v

  let fields =
    [ "id", qs id
      "label", tsTextSourceLit (fieldReq "label" v)
      "kind", tsFieldKindLit (AutoBind.Form id) (fieldReq "kind" v)
      "required", boolLit (boolOf "required" v) ]
    @ (match JsonValue.tryField "help" v with
       | Some h -> [ "help", tsTextSourceLit h ]
       | None -> [])
    @ (match JsonValue.tryField "rule" v with
       | Some r -> [ "rule", tsFieldRule r ]
       | None -> [])

  tsObjLit fields depth

let private tsFilterSpec (depth: int) (v: JsonValue) : string =
  let name = strOf "name" v

  tsObjLit
    [ "name", qs name
      "label", tsTextSourceLit (fieldReq "label" v)
      "field", tsFieldKindLit (AutoBind.Filter name) (fieldReq "kind" v) ]
    depth

/// A grid's declared initial sort — the zero-based column index plus a
/// lower-case direction, both required whenever the slot is present.
let private tsDefaultSort (v: JsonValue) : string =
  tsInline [ "column", numLit (numOf "column" v); "direction", qs (strOf "direction" v) ]

let private tsColumnWidth (v: JsonValue) : string =
  match dollarType v with
  | Some "Fixed" -> tsInline [ "kind", qs "Fixed"; "pixels", numLit (numOf "pixels" v) ]
  | Some "Flex" -> tsInline [ "kind", qs "Flex"; "weight", numLit (numOf "weight" v) ]
  | _ -> "{ kind: 'Auto' }"

let private tsGridColumn (v: JsonValue) : string =
  // `format` / `width` are absent-is-omit spelled as the IDENTITY DEFAULT: the
  // column model requires both slots and the encoder drops each at its identity.
  tsInline
    [ "label", qs (strOf "label" v)
      "value", "() => ({ kind: 'Empty' })"
      "format", tsCellFormat (fieldOrIdentity "format" v)
      "kind", tsInline [ "kind", qs (dollarType (fieldReq "kind" v) |> Option.defaultValue "Text") ]
      "width", tsColumnWidth (fieldOrIdentity "width" v) ]

/// A `Tree` row (Phase 1120) — recursive, `children` omitted-when-empty and
/// `icon` omitted-when-absent, both of which the ctor re-normalises.
let rec private tsTreeItem (v: JsonValue) : string =
  tsInline (
    [ "id", qs (strOf "id" v); "label", tsTextInput (fieldReq "label" v) ]
    @ (match JsonValue.tryField "children" v with
       | Some(JArray cs) when not (List.isEmpty cs) ->
         [ "children", "[" + (cs |> List.map tsTreeItem |> String.concat ", ") + "]" ]
       | _ -> [])
    @ (match optStr "icon" v with
       | Some i -> [ "icon", qs i ]
       | None -> [])
  )

/// A chart annotation (Phase 1534) — the reference line, the event marker and
/// the shaded band, each with an optional `label`. No ctor surface reaches them,
/// so they project as the typed spec literal the post-edit spread carries.
let private tsChartAnnotation (v: JsonValue) : string =
  let annX (x: JsonValue) =
    match dollarType x with
    | Some "Date" -> tsInline [ "kind", qs "Date"; "iso", qs (strOf "iso" x) ]
    | _ -> tsInline [ "kind", qs "Category"; "key", qs (strOf "key" x) ]

  let range (r: JsonValue) =
    match dollarType r with
    | Some "XRange" ->
      tsInline
        [ "kind", qs "XRange"
          "from", annX (fieldReq "from" r)
          "to", annX (fieldReq "to" r) ]
    | _ ->
      tsInline
        [ "kind", qs "ValueRange"
          "from", numLit (numOf "from" r)
          "to", numLit (numOf "to" r) ]

  let label =
    match JsonValue.tryField "label" v with
    | Some l -> [ "label", tsTextSourceLit l ]
    | None -> []

  match dollarType v with
  | Some "EventMarker" -> tsInline ([ "kind", qs "EventMarker"; "at", annX (fieldReq "at" v) ] @ label)
  | Some "RangeBand" -> tsInline ([ "kind", qs "RangeBand"; "range", range (fieldReq "range" v) ] @ label)
  | _ -> tsInline ([ "kind", qs "ReferenceLine"; "value", numLit (numOf "value" v) ] @ label)

/// A `Media` timed-text track (Phase 1114). `default` is omitted-when-false, so
/// it rides only where the wire asserts it; the ctor coerces `label` from the
/// wire's bare string and passes a `src` Binding straight through.
let private tsMediaTrack (v: JsonValue) : string =
  tsInline (
    [ "kind", qs (strOf "kind" v)
      "src", tsBinding Opq.Scalar (fieldReq "src" v)
      "srcLang", qs (strOf "srcLang" v)
      "label", tsTextInput (fieldReq "label" v) ]
    @ (if boolOf "default" v then [ "default", "true" ] else [])
  )

let private tsTabHeader (v: JsonValue) : string =
  tsInline (
    [ "label", tsTextSourceLit (fieldReq "label" v) ]
    @ (match optStr "icon" v with
       | Some i -> [ "icon", "iconSource(" + qs i + ")" ]
       | None -> [])
    @ (match JsonValue.tryField "disabled" v with
       | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
       | None -> [])
  )

// ── Fragment parameterisation (holes / scalars) ───────────────────────────────

let private tsFragScalar (v: JsonValue) : string =
  match dollarType v with
  | Some "Int" -> tsInline [ "kind", qs "int"; "value", numLit (numOf "value" v) ]
  | Some "Float" -> tsInline [ "kind", qs "float"; "value", numLit (numOf "value" v) ]
  | Some "Bool" -> tsInline [ "kind", qs "bool"; "value", boolLit (boolOf "value" v) ]
  | _ -> tsInline [ "kind", qs "str"; "value", qs (strOf "value" v) ]

let private tsHoleSpace (v: JsonValue) : string =
  match dollarType v with
  | Some "IntRange" ->
    tsInline
      [ "kind", qs "IntRange"
        "min", numLit (numOf "min" v)
        "max", numLit (numOf "max" v) ]
  | Some "FloatRange" ->
    tsInline
      [ "kind", qs "FloatRange"
        "min", numLit (numOf "min" v)
        "max", numLit (numOf "max" v) ]
  | Some "StringLen" ->
    tsInline
      [ "kind", qs "StringLen"
        "minLen", numLit (numOf "minLen" v)
        "maxLen", numLit (numOf "maxLen" v) ]
  | Some "Enum" ->
    tsInline
      [ "kind", qs "Enum"
        "choices", "[" + (arrOf "choices" v |> List.map strItem |> String.concat ", ") + "]" ]
  | _ -> "{ kind: 'AnyString' }"

let private tsHoleDecl (v: JsonValue) : string =
  match dollarType v with
  | Some "Slot" ->
    tsInline (
      [ "kind", qs "Slot"; "name", qs (strOf "name" v) ]
      @ (match optStr "kindConstraint" v with
         | Some c -> [ "kindConstraint", qs c ]
         | None -> [])
    )
  | Some "Repeat" ->
    tsInline
      [ "kind", qs "Repeat"
        "name", qs (strOf "name" v)
        "countSpace", tsHoleSpace (fieldReq "countSpace" v) ]
  | _ ->
    tsInline (
      [ "kind", qs "Value"
        "name", qs (strOf "name" v)
        "space", tsHoleSpace (fieldReq "space" v) ]
      @ (match JsonValue.tryField "default" v with
         | Some d -> [ "default", tsFragScalar d ]
         | None -> [])
    )

// ── Base traits (style / state / accessibility) ───────────────────────────────

let private tsStyleLit (v: JsonValue) : string =
  // tone / weight / emphasis are emitted omitted-when-default by the encoder, so
  // an absent wire field must project as its DEFAULT (not `''`, which the encoder
  // would emit as a non-default override); role / voice are optional (absent ⇒
  // omit).
  tsInline (
    [ "tone", qs (optStr "tone" v |> Option.defaultValue "Default")
      "weight", qs (optStr "weight" v |> Option.defaultValue "Standard")
      "emphasis", qs (optStr "emphasis" v |> Option.defaultValue "Normal") ]
    @ (match optStr "role" v with
       | Some r -> [ "role", qs r ]
       | None -> [])
    @ (match optStr "voice" v with
       | Some vo -> [ "voice", qs vo ]
       | None -> [])
    // Phase 1533 — the writing-direction slot. `auto` is the default and the
    // encoder omits it, so an absent wire field must project as ABSENT here
    // (spelling it out as 'auto' would be a non-default override on re-encode).
    @ (match optStr "direction" v with
       | Some d -> [ "direction", qs d ]
       | None -> [])
  )

let private tsAccessibilityLit (v: JsonValue) : string =
  tsInline (
    (match JsonValue.tryField "label" v with
     | Some b -> [ "label", tsBinding Opq.Scalar b ]
     | None -> [])
    @ (match optStr "labelledBy" v with
       | Some s -> [ "labelledBy", "nodeId(" + qs s + ")" ]
       | None -> [])
    @ (match optStr "describedBy" v with
       | Some s -> [ "describedBy", "nodeId(" + qs s + ")" ]
       | None -> [])
    @ (match optStr "role" v with
       | Some s -> [ "role", qs s ]
       | None -> [])
    @ (match optStr "liveRegion" v with
       | Some s -> [ "liveRegion", qs s ]
       | None -> [])
    @ (match JsonValue.tryField "hidden" v with
       | Some b -> [ "hidden", tsBinding Opq.Scalar b ]
       | None -> [])
  )

/// The per-kind ARIA default the smart ctor injects (mirrors
/// @fuaran-ui/schema `defaults.accessibility`) – the trait the emitter must
/// strip (or confirm) to reach the wire's exact value.
let private ctorAccessibility (kindType: string) : (string * string) list =
  match kindType with
  | "Metric" -> [ "liveRegion", "polite" ]
  | "Callout" -> [ "liveRegion", "assertive"; "role", "alert" ]
  | "Progress" -> [ "liveRegion", "polite"; "role", "progressbar" ]
  | "Toast" -> [ "liveRegion", "polite"; "role", "status" ]
  | "Divider" -> [ "role", "separator" ]
  | "Button" -> [ "role", "button" ]
  | "Select" -> [ "role", "combobox" ]
  | "Form" -> [ "role", "form" ]
  | "FileUpload" -> [ "role", "button" ]
  | "Dashboard" -> [ "role", "main" ]
  | "Card"
  | "SummaryList"
  | "Disclosure" -> [ "role", "region" ]
  | "Modal" -> [ "role", "dialog" ]
  | "Tabs" -> [ "role", "tablist" ]
  | "DataGrid" -> [ "role", "region" ]
  | "Chart" -> [ "liveRegion", "polite"; "role", "region" ]
  | "Map" -> [ "role", "region" ]
  | _ -> []

let private a11yMatchesCtor (wire: JsonValue option) (expected: (string * string) list) : bool =
  match wire with
  | None -> List.isEmpty expected
  | Some(JObject ms) ->
    let actual =
      ms
      |> List.choose (fun (k, x) ->
        match x with
        | JString s -> Some(k, s)
        | _ -> None)
      |> List.sortBy fst

    List.length actual = List.length ms && actual = (expected |> List.sortBy fst)
  | Some _ -> false

/// The value binding of a `Metric` / `LabelValueRow`: the 0.2.0 value/source law
/// renamed `source` → `value` for these two kinds; the legacy `source` field is
/// still read as a fallback so pasted pre-0.2.0 wire keeps projecting.
let private valueOrSource (k: JsonValue) : JsonValue =
  match JsonValue.tryField "value" k with
  | Some v -> v
  | None -> fieldReq "source" k

/// A `DataGrid`'s erased column: `field` (declarative) and `value` (closure) are
/// sibling optional slots; `format`/`width` project their (default-omitted) form.
let private tsGridColumnErased (v: JsonValue) : string =
  let kindObj = fieldReq "kind" v

  let cellKind =
    match dollarType kindObj |> Option.defaultValue "Text" with
    | "Numeric" -> "{ kind: 'Numeric' }"
    | "Date" -> "{ kind: 'Date' }"
    | "Editable" -> "{ kind: 'Editable', onEdit: () => action.chain([]) }"
    | "TonedPill" ->
      // Phase 750's declarative pill: wholly wire-expressible, so it projects
      // faithfully. In-memory `defaultTone` is required; the wire omits
      // `default` when it is 'Default' (the omit-on-default discipline), so
      // absence reconstructs as 'Default'.
      let mapLit =
        membersOf "map" kindObj
        |> List.map (fun (value, tone) ->
          qs value
          + ": "
          + qs (
            match tone with
            | JString t -> t
            | _ -> ""
          ))
        |> String.concat ", "

      let defaultTone = optStr "default" kindObj |> Option.defaultValue "Default"

      "{ kind: 'TonedPill', field: "
      + qs (strOf "field" kindObj)
      + ", map: { "
      + mapLit
      + " }, defaultTone: "
      + qs defaultTone
      + " }"
    | _ -> "{ kind: 'Text' }"

  tsInline (
    [ "kind", cellKind
      "label", qs (strOf "label" v)
      "format", tsCellFormat (fieldOrIdentity "format" v)
      "width", tsColumnWidth (fieldOrIdentity "width" v) ]
    @ (match JsonValue.tryField "value" v with
       | Some _ -> [ "value", "() => ({ kind: 'Text', value: '' })" ]
       | None -> [])
    @ (match optStr "field" v with
       | Some f -> [ "field", qs f ]
       | None -> [])
    // Per-column opt-OUT slots: both are optional and ride the wire only when
    // explicitly set, so an absent slot must project as absent, not as a default.
    @ (match JsonValue.tryField "sortable" v with
       | Some(JBool b) -> [ "sortable", boolLit b ]
       | _ -> [])
    @ (match JsonValue.tryField "editable" v with
       | Some(JBool b) -> [ "editable", boolLit b ]
       | _ -> [])
  )

/// `Drawing` (Phase 524) has no smart ctor, so it projects as the typed in-memory
/// spec literal: the wire tree is walked structurally (`$type` → `kind`; `Static`
/// bindings ride `{ kind: 'Static', value }`); the `text` / `description` /
/// `title` slots — and a `DrawStyle.tip` — are `TextSource`, wrapped as
/// `Literal` from their bare string. A `tip` already in envelope form
/// (`Bound` / `I18n`) walks structurally and needs no wrapping.
let rec private tsDrawingConv (key: string) (v: JsonValue) : string =
  match v with
  | JString s when key = "text" || key = "description" || key = "title" || key = "tip" ->
    "{ kind: 'Literal', value: " + qs s + " }"
  | JNull -> "undefined"
  | JBool b -> boolLit b
  | JNumber n -> numLit n
  | JString s -> qs s
  | JArray xs -> "[" + (xs |> List.map (tsDrawingConv "") |> String.concat ", ") + "]"
  | JObject ms ->
    "{ "
    + (ms
       |> List.map (fun (k, x) -> (if k = "$type" then "kind" else k) + ": " + tsDrawingConv k x)
       |> String.concat ", ")
    + " }"

// ── The per-kind node emitter ─────────────────────────────────────────────────

let rec private tsNodeExpr (depth: int) (nodeV: JsonValue) : string =
  markNode nodeV (tsNodeExprRaw depth nodeV)

and private tsNodeExprRaw (depth: int) (nodeV: JsonValue) : string =
  let id = optStr "id" nodeV |> Option.defaultValue ""
  let kindObj = fieldReq "kind" nodeV
  let kindType = dollarType kindObj |> Option.defaultValue ""

  if kindType = "Mount" then
    tsMountNode depth id kindObj nodeV
  elif kindType = "Box" then
    tsBoxNode depth id kindObj nodeV
  elif kindType = "Fact" then
    tsFactNode depth id kindObj nodeV
  elif kindType = "Drawing" then
    tsDrawingNode depth id kindObj nodeV
  elif kindType = "DataGrid" then
    tsDataGridNode depth id kindObj nodeV
  else
    match tsKindCtor depth kindType id kindObj with
    | None ->
      // Unknown kind – the illustrative generic sketch (never crash, never a
      // false fidelity claim; the conformance gate covers every corpus kind).
      renderNode tsSpec depth nodeV
    | Some ctorExpr ->
      let overrides =
        (match JsonValue.tryField "style" nodeV with
         | Some st -> [ "style", tsStyleLit st ]
         | None -> [])
        @ (match JsonValue.tryField "state" nodeV with
           | Some st -> [ "state", tsStateLit (depth + 1) st ]
           | None -> [])
        @ (let wireA = JsonValue.tryField "accessibility" nodeV
           // A `Box` dispatches to the ctor matching its `role`, and each injects
           // that ctor's ARIA default (dashboard → main, card → region); the plain
           // stack / gridLayout inject none. Reach the wire's exact a11y by pinning
           // against the dispatched ctor's default, not the (empty) `Box` default.
           let expectedA11y =
             if kindType = "Box" then
               match strOf "role" kindObj with
               | "Dashboard" -> [ "role", "main" ]
               | "Card" -> [ "role", "region" ]
               | _ -> []
             else
               ctorAccessibility kindType

           if a11yMatchesCtor wireA expectedA11y then
             []
           else
             [ "accessibility",
               (match wireA with
                | Some a -> tsAccessibilityLit a
                | None -> "undefined") ])
        // Phase 1112 — the node-level tooltip trait. No smart ctor carries it,
        // so it rides the same override spread the other base traits take. It is
        // a `TextSource` in memory even where the wire spells it as a bare
        // string, so it takes the explicit object form.
        @ (match JsonValue.tryField "tooltip" nodeV with
           | Some t -> [ "tooltip", tsTextSourceLit t ]
           | None -> [])
        // Phase 1535 — conditional presence. A `Binding<boolean>` whose resolved
        // `false` removes the node entirely, which is a different statement from
        // `accessibility.hidden` (rendered, occupying space, out of the a11y
        // tree). No smart ctor carries it, so it rides the override spread.
        @ (match JsonValue.tryField "visible" nodeV with
           | Some vis -> [ "visible", tsBinding Opq.Scalar vis ]
           | None -> [])

      if List.isEmpty overrides then
        ctorExpr
      else
        "{ ...("
        + ctorExpr
        + "), "
        + (overrides |> List.map (fun (k, v) -> k + ": " + v) |> String.concat ", ")
        + " }"

and private tsStateLit (depth: int) (v: JsonValue) : string =
  let fields =
    (match JsonValue.tryField "onLoading" v with
     | Some n -> [ "onLoading", tsNodeExpr (depth + 1) n ]
     | None -> [])
    @ (match JsonValue.tryField "onEmpty" v with
       | Some n -> [ "onEmpty", tsNodeExpr (depth + 1) n ]
       | None -> [])
    @ (match JsonValue.tryField "onError" v with
       | Some _ -> [ "onError", "() => fuaran.skeleton('fuaran-on-error-placeholder', 1)" ]
       | None -> [])

  tsObjLit fields depth

and private tsChildren (depth: int) (k: JsonValue) : string =
  let children = arrOf "children" k

  if List.isEmpty children then
    "[]"
  else
    "[\n"
    + (children
       |> List.map (fun c -> pad (depth + 2) + tsNodeExpr (depth + 2) c)
       |> String.concat ",\n")
    + ",\n"
    + pad (depth + 1)
    + "]"

and private tsFragArg (depth: int) (v: JsonValue) : string =
  match dollarType v with
  | Some "SlotArg" -> "{ kind: 'slot', tree: " + tsNodeExpr (depth + 1) (fieldReq "tree" v) + " }"
  | _ -> "{ kind: 'value', value: " + tsFragScalar v + " }"

and private tsMountNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  // `@fuaran-ui/ui` ships no Mount smart ctor (Phase 265 landed wire parity
  // only), so Mount projects as the typed in-memory node literal directly.
  let channelV = fieldReq "channel" k

  let channel =
    tsInline (
      [ "direction", qs (strOf "direction" channelV) ]
      @ (match optStr "messageShape" channelV with
         | Some s -> [ "messageShape", qs s ]
         | None -> [])
    )

  let inputs = membersOf "inputs" k

  let inputsLit =
    if List.isEmpty inputs then
      "{}"
    else
      "{ "
      + (inputs
         |> List.map (fun (key, a) -> jsKey key + ": " + tsFragArg (depth + 1) a)
         |> String.concat ", ")
      + " }"

  let spec =
    tsInline
      [ "scopeId", qs (strOf "scopeId" k)
        "inputs", inputsLit
        "channel", channel
        "capabilities", "[" + (arrOf "capabilities" k |> List.map strItem |> String.concat ", ") + "]" ]

  let fields =
    [ "id", qs id
      "kind", "{ kind: 'Mount', spec: " + spec + " }"
      "state",
      (match JsonValue.tryField "state" nodeV with
       | Some st -> tsStateLit (depth + 1) st
       | None -> "{}")
      "style",
      (match JsonValue.tryField "style" nodeV with
       | Some st -> tsStyleLit st
       | None -> "{ tone: 'Default', weight: 'Standard', emphasis: 'Normal' }") ]
    @ (match JsonValue.tryField "accessibility" nodeV with
       | Some a -> [ "accessibility", tsAccessibilityLit a ]
       | None -> [])

  tsObjLit fields depth

// The base traits (state / style / accessibility) for a raw in-memory node
// literal (Fact / Drawing / DataGrid have no smart ctor, so they bypass the
// `tsKindCtor` override path and pin their own traits from the wire node — an
// absent trait projects as its default, which the encoder omits).
and private tsBaseTraits (depth: int) (nodeV: JsonValue) : (string * string) list =
  [ "state",
    (match JsonValue.tryField "state" nodeV with
     | Some st -> tsStateLit (depth + 1) st
     | None -> "{}")
    "style",
    (match JsonValue.tryField "style" nodeV with
     | Some st -> tsStyleLit st
     | None -> "{ tone: 'Default', weight: 'Standard', emphasis: 'Normal' }") ]
  @ (match JsonValue.tryField "accessibility" nodeV with
     | Some a -> [ "accessibility", tsAccessibilityLit a ]
     | None -> [])
  @ (match JsonValue.tryField "tooltip" nodeV with
     | Some t -> [ "tooltip", tsTextSourceLit t ]
     | None -> [])
  @ (match JsonValue.tryField "visible" nodeV with
     | Some vis -> [ "visible", tsBinding Opq.Scalar vis ]
     | None -> [])

and private tsBoxNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  // 0.2.0 — the consolidated `Box` layout kind (`role` + `layout`, absorbing
  // Dashboard / Stack / GridLayout / Card). Projected as the typed in-memory
  // node literal rather than a ctor, so a `Dashboard`-role Box can carry a
  // `heading` (the `dashboard` ctor takes none) and no ctor ARIA default leaks.
  let layoutV = fieldReq "layout" k

  // `gap` is an optional slot on every laid-out `BoxLayout` case (Flex / Grid /
  // Masonry), omitted-when-absent so a gapless layout stays byte-identical.
  let gapPart =
    match optNum "gap" layoutV with
    | Some g -> ", gap: " + numLit g
    | None -> ""

  let layout =
    match dollarType layoutV with
    | Some "Flex" ->
      "{ kind: 'Flex', direction: "
      + qs (strOf "direction" layoutV)
      + ", wrap: "
      + boolLit (boolOf "wrap" layoutV)
      + gapPart
      + " }"
    | Some "Grid" ->
      "{ kind: 'Grid', cols: "
      + numLit (numOf "cols" layoutV)
      + gapPart
      + (match optStr "templateColumns" layoutV with
         | Some t -> ", templateColumns: " + qs t
         | None -> "")
      + " }"
    // Phase 1082's masonry hang — `cols` is required, `gap` optional.
    | Some "Masonry" -> "{ kind: 'Masonry', cols: " + numLit (numOf "cols" layoutV) + gapPart + " }"
    | Some other -> "{ kind: '" + other + "' }"
    | None -> "{ kind: 'Auto' }"

  let spec =
    tsInline (
      [ "layout", layout
        "role", qs (strOf "role" k)
        "children", tsChildren depth k ]
      @ (match JsonValue.tryField "heading" k with
         | Some h -> [ "heading", tsTextSourceLit h ]
         | None -> [])
      // Phase 1124 — the print-pagination hints. Both are `false`-by-default
      // booleans the encoder omits at their default, so they ride only when the
      // wire asserts them.
      @ (if boolOf "keepTogether" k then
           [ "keepTogether", "true" ]
         else
           [])
      @ (if boolOf "breakBefore" k then
           [ "breakBefore", "true" ]
         else
           [])
    )

  tsObjLit
    ([ "id", qs id
       "kind", "{ kind: 'Layout', layout: { kind: 'Box', spec: " + spec + " } }" ]
     @ tsBaseTraits depth nodeV)
    depth

and private tsFactNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  // `@fuaran-ui/ui` ships no `Fact` ctor carrying `icon`, so Fact projects as the
  // typed in-memory Display node literal.
  let spec =
    tsInline (
      [ "label", tsTextSourceLit (fieldReq "label" k)
        "value", tsTextSourceLit (fieldReq "value" k)
        // `tone` is required by the spec (encoder omits it when 'Default'); an
        // absent wire field projects as the default, never undefined.
        "tone", qs (optStr "tone" k |> Option.defaultValue "Default") ]
      @ (if boolOf "emphasis" k then [ "emphasis", "true" ] else [])
      @ (match JsonValue.tryField "help" k with
         | Some h -> [ "help", tsTextSourceLit h ]
         | None -> [])
      @ (match optStr "icon" k with
         | Some i -> [ "icon", qs i ]
         | None -> [])
    )

  tsObjLit
    ([ "id", qs id
       "kind", "{ kind: 'Display', display: { kind: 'Fact', spec: " + spec + " } }" ]
     @ tsBaseTraits depth nodeV)
    depth

and private tsDrawingNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  let spec =
    match k with
    | JObject ms ->
      "{ "
      + (ms
         |> List.filter (fun (kk, _) -> kk <> "$type")
         |> List.map (fun (kk, x) -> (if kk = "kind" then "kind" else kk) + ": " + tsDrawingConv kk x)
         |> String.concat ", ")
      + " }"
    | _ -> "{}"

  tsObjLit
    ([ "id", qs id
       "kind", "{ kind: 'Display', display: { kind: 'Drawing', spec: " + spec + " } }" ]
     @ tsBaseTraits depth nodeV)
    depth

and private tsDataGridNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  let cols = arrOf "columns" k

  let staticRows =
    match JsonValue.tryField "staticRows" k with
    | Some sr ->
      let headers =
        "["
        + (arrOf "headers" sr |> List.map tsTextSourceLit |> String.concat ", ")
        + "]"

      let rows =
        "["
        + (arrOf "rows" sr
           |> List.map (fun row ->
             match row with
             | JArray cells -> "[" + (cells |> List.map tsTextSourceLit |> String.concat ", ") + "]"
             | _ -> "[]")
           |> String.concat ", ")
        + "]"

      // `sortable` / `defaultSort` are the static-rows sort affordance; both
      // optional, both omitted-when-absent.
      let extras =
        (match JsonValue.tryField "sortable" sr with
         | Some(JBool b) -> ", sortable: " + boolLit b
         | _ -> "")
        + (match JsonValue.tryField "defaultSort" sr with
           | Some d -> ", defaultSort: " + tsDefaultSort d
           | None -> "")

      [ "staticRows", "{ headers: " + headers + ", rows: " + rows + extras + " }" ]
    | None -> []

  let spec =
    tsInline (
      [ "columns", "[" + (cols |> List.map tsGridColumnErased |> String.concat ", ") + "]"
        "source", tsBinding Opq.Collection (fieldReq "source" k) ]
      @ (if boolOf "editable" k then [ "editable", "true" ] else [])
      @ (if boolOf "reorderable" k then
           [ "reorderable", "true" ]
         else
           [])
      @ (match JsonValue.tryField "onRowClick" k with
         | Some _ -> [ "onRowClick", "() => action.chain([])" ]
         | None -> [])
      @ (match JsonValue.tryField "rowKey" k with
         | Some _ -> [ "rowKey", "() => ''" ]
         | None -> [])
      @ (match optStr "rowKeyField" k with
         | Some f -> [ "rowKeyField", qs f ]
         | None -> [])
      // The declarative sort / page / edit state slots: each names the State key
      // the grid reads its own affordance from, so each rides the wire verbatim.
      @ (match optStr "sortStateKey" k with
         | Some s -> [ "sortStateKey", qs s ]
         | None -> [])
      @ (match JsonValue.tryField "defaultSort" k with
         | Some d -> [ "defaultSort", tsDefaultSort d ]
         | None -> [])
      @ (match optNum "pageSize" k with
         | Some n -> [ "pageSize", numLit n ]
         | None -> [])
      @ (match optStr "pageStateKey" k with
         | Some s -> [ "pageStateKey", qs s ]
         | None -> [])
      @ (match optStr "editStateKey" k with
         | Some s -> [ "editStateKey", qs s ]
         | None -> [])
      // Phase 1124 — the grid's print-pagination hints (`false`-by-default, so
      // omitted at their default) and the drag-transfer keys (optional strings
      // naming the transfer group a row may leave / join).
      @ (if boolOf "exportable" k then
           [ "exportable", "true" ]
         else
           [])
      @ (if boolOf "keepRowsTogether" k then
           [ "keepRowsTogether", "true" ]
         else
           [])
      @ (if boolOf "repeatHeader" k then
           [ "repeatHeader", "true" ]
         else
           [])
      @ (match optStr "transferOutKey" k with
         | Some s -> [ "transferOutKey", qs s ]
         | None -> [])
      @ (match optStr "transferInKey" k with
         | Some s -> [ "transferInKey", qs s ]
         | None -> [])
      @ staticRows
    )

  tsObjLit
    ([ "id", qs id
       "kind", "{ kind: 'Visualisation', visualisation: { kind: 'Grid', spec: " + spec + " } }" ]
     @ tsBaseTraits depth nodeV)
    depth

and private tsKindCtor (depth: int) (kindType: string) (id: string) (k: JsonValue) : string option =
  let idF = ("id", qs id)

  let call (ctor: string) (fields: (string * string) list) =
    Some("fuaran." + ctor + "(" + tsObjLit fields depth + ")")

  match kindType with
  // ── Layout ────────────────────────────────────────────────────────────────
  // 0.2.0 — the distinct Dashboard / Stack / GridLayout / Card kinds consolidated
  // into a single `Box` carrying `role` + `layout`; the authoring vocabulary
  // (`dashboard` / `stack` / `gridLayout` / `card`) is unchanged, so dispatch by
  // role + layout to the ctor that emits the matching Box. (The legacy per-kind
  // arms below stay for pre-0.2.0 wire + the never-crash contract.)
  | "Box" ->
    let layoutV = fieldReq "layout" k
    let layoutType = dollarType layoutV |> Option.defaultValue "Auto"
    let children = tsChildren depth k

    match strOf "role" k, layoutType with
    | "Dashboard", _ -> call "dashboard" [ idF; "children", children ]
    | "Card", _ ->
      call
        "card"
        ([ idF ]
         @ (match JsonValue.tryField "heading" k with
            | Some h -> [ "heading", tsTextInput h ]
            | None -> [])
         @ [ "children", children ])
    | _, "Grid" ->
      (match optStr "templateColumns" layoutV with
       | Some t ->
         call
           "gridLayoutTemplated"
           [ idF
             "cols", numLit (numOf "cols" layoutV)
             "templateColumns", qs t
             "children", children ]
       | None -> call "gridLayout" [ idF; "cols", numLit (numOf "cols" layoutV); "children", children ])
    | _, "Flex" ->
      call
        "stack"
        [ idF
          "orientation", qs (strOf "direction" layoutV)
          "wrap", boolLit (boolOf "wrap" layoutV)
          "children", children ]
    | _ ->
      // role Group with an `Auto` (or unknown) layout — the plain vertical stack.
      call "stack" [ idF; "orientation", qs "Vertical"; "wrap", "false"; "children", children ]
  | "Dashboard" -> call "dashboard" [ idF; "children", tsChildren depth k ]
  | "Stack" ->
    call
      "stack"
      [ idF
        "orientation", qs (strOf "orientation" k)
        "wrap", boolLit (boolOf "wrap" k)
        "children", tsChildren depth k ]
  | "GridLayout" ->
    let fields =
      [ idF; "cols", numLit (numOf "cols" k) ]
      @ (match optStr "templateColumns" k with
         | Some t -> [ "templateColumns", qs t ]
         | None -> [])
      @ [ "children", tsChildren depth k ]

    call
      (match optStr "templateColumns" k with
       | Some _ -> "gridLayoutTemplated"
       | None -> "gridLayout")
      fields
  | "SplitPanel" -> call "splitPanel" [ idF; "weight", numLit (numOf "weight" k); "children", tsChildren depth k ]
  | "Tabs" ->
    let headers =
      match JsonValue.tryField "tabHeaders" k with
      | Some(JArray hs) -> Some hs
      | _ -> None

    let tags =
      match JsonValue.tryField "tabTags" k with
      | Some(JArray ts) -> Some ts
      | _ -> None

    let fields =
      [ idF ]
      @ (match optStr "orientation" k with
         | Some o -> [ "orientation", qs o ]
         | None -> [])
      // Omitted-when-absent, not defaulted. `activeIndex` is the one binding
      // slot the encoder omits at its default (`Static 0`), so the canonical
      // wire for an unset one carries no key at all — and passing the absent
      // JNull through `tsBinding` yields `binding.static(undefined)`, which is
      // an EXPLICIT `Static` the builder's own `?? Static 0` default can no
      // longer fill and the encoder can no longer omit. It re-encodes as
      // `"activeIndex":{"$type":"Static"}` against a fixture that has no such
      // key. Same shape as `activeTag` below.
      @ (match fieldOpt "activeIndex" k with
         | Some ai -> [ "activeIndex", tsBinding Opq.Scalar ai ]
         | None -> [])
      @ (match JsonValue.tryField "onSelect" k with
         | Some _ -> [ "onSelect", "() => action.chain([])" ]
         | None -> [])
      @ (match headers with
         | Some hs -> [ "tabHeaders", "[" + (hs |> List.map tsTabHeader |> String.concat ", ") + "]" ]
         | None -> [])
      @ (match tags with
         | Some ts -> [ "tabTags", "[" + (ts |> List.map strItem |> String.concat ", ") + "]" ]
         | None -> [])
      @ (match JsonValue.tryField "activeTag" k with
         | Some t -> [ "activeTag", tsBinding Opq.Scalar t ]
         | None -> [])
      @ (match JsonValue.tryField "onSelectTag" k with
         | Some _ -> [ "onSelectTag", "() => action.chain([])" ]
         | None -> [])
      @ [ "children", tsChildren depth k ]

    call
      (if headers.IsSome && tags.IsSome then
         "tabsTagged"
       else
         "tabs")
      fields
  | "Card" ->
    call
      "card"
      ([ idF ]
       @ (match JsonValue.tryField "heading" k with
          | Some h -> [ "heading", tsTextInput h ]
          | None -> [])
       @ [ "children", tsChildren depth k ])
  | "Stepper" ->
    call
      "stepper"
      [ idF
        "activeStep", tsBinding Opq.Scalar (fieldReq "activeStep" k)
        "children", tsChildren depth k ]
  | "SummaryList" ->
    call
      "summaryList"
      ([ idF ]
       @ (match JsonValue.tryField "heading" k with
          | Some h -> [ "heading", tsTextInput h ]
          | None -> [])
       @ [ "children", tsChildren depth k ])
  | "Disclosure" ->
    call
      "disclosure"
      ([ idF
         "heading", tsTextInput (fieldReq "heading" k)
         "open", tsBinding Opq.Scalar (fieldReq "open" k)
         "defaultOpen", boolLit (boolOf "defaultOpen" k) ]
       @ (match JsonValue.tryField "onToggle" k with
          | Some _ -> [ "onToggle", "() => action.chain([])" ]
          | None -> [])
       @ [ "children", tsChildren depth k ])
  | "Modal" ->
    call
      "modal"
      ([ idF ]
       @ (match JsonValue.tryField "heading" k with
          | Some h -> [ "heading", tsTextInput h ]
          | None -> [])
       @ [ "open", tsBinding Opq.Scalar (fieldReq "open" k)
           "dismissable", boolLit (boolOf "dismissable" k) ]
       @ (match JsonValue.tryField "onDismiss" k with
          | Some d -> [ "onDismiss", tsAction d ]
          | None -> [])
       // Phase 1113 — the popover form: `modality` selects the presentation
       // (Dialog is the default and the encoder omits it) and `anchor` names the
       // node the popover hangs off. Both optional, both omitted when absent.
       @ (match optStr "modality" k with
          | Some m -> [ "modality", qs m ]
          | None -> [])
       @ (match optStr "anchor" k with
          | Some a -> [ "anchor", qs a ]
          | None -> [])
       @ [ "children", tsChildren depth k ])
  | "ScrollArea" ->
    call
      "scrollArea"
      ([ idF; "orientation", qs (strOf "orientation" k) ]
       @ (match optNum "maxHeight" k with
          | Some m -> [ "maxHeight", numLit m ]
          | None -> [])
       @ (match optNum "maxWidth" k with
          | Some m -> [ "maxWidth", numLit m ]
          | None -> [])
       @ [ "children", tsChildren depth k ])
  // ── Display ───────────────────────────────────────────────────────────────
  | "Heading" ->
    call
      "heading"
      [ idF
        "text", tsTextInput (fieldReq "text" k)
        "level", numLit (numOf "level" k)
        "variant", qs (strOf "variant" k) ]
  | "Markdown" ->
    let t = fieldReq "text" k

    (match t with
     | JString s -> Some("fuaran.markdown(" + qs id + ", " + qs s + ")")
     | _ ->
       match dollarType t with
       | Some "Literal" -> Some("fuaran.markdown(" + qs id + ", " + qs (strOf "text" t) + ")")
       | _ -> Some("fuaran.markdownSpec(" + qs id + ", " + tsTextSourceLit t + ")"))
  | "Metric" ->
    call
      "metric"
      ([ idF
         "label", tsTextInput (fieldReq "label" k)
         "value", tsBinding Opq.Scalar (valueOrSource k)
         "format", tsCellFormat (fieldOrIdentity "format" k) ]
       @ (match optStr "tone" k with
          | Some t -> [ "tone", qs t ]
          | None -> [])
       @ (match optStr "weight" k with
          | Some w -> [ "weight", qs w ]
          | None -> [])
       @ (match optStr "emphasis" k with
          | Some e -> [ "emphasis", qs e ]
          | None -> [])
       @ (match JsonValue.tryField "trend" k with
          | Some t -> [ "trend", tsBinding Opq.Scalar t ]
          | None -> [])
       @ (match JsonValue.tryField "trendFormat" k with
          | Some t -> [ "trendFormat", tsCellFormat t ]
          | None -> [])
       @ (match optStr "trendPolarity" k with
          | Some p -> [ "trendPolarity", qs p ]
          | None -> [])
       @ (match optStr "icon" k with
          | Some i -> [ "icon", qs i ]
          | None -> [])
       @ (match JsonValue.tryField "subtext" k with
          | Some s -> [ "subtext", tsTextInput s ]
          | None -> []))
  | "Badge" ->
    call
      "badge"
      [ idF
        "label", tsTextInput (fieldReq "label" k)
        "variant", qs (strOf "variant" k) ]
  | "Sparkline" -> call "sparkline" [ idF; "source", tsBinding Opq.Collection (fieldReq "source" k) ]
  | "Spacer" -> call "spacer" [ idF; "size", qs (strOf "size" k) ]
  | "Skeleton" -> Some("fuaran.skeleton(" + qs id + ", " + numLit (numOf "rows" k) + ")")
  | "Callout" ->
    call
      "callout"
      ([ idF
         "body", tsTextInput (fieldReq "body" k)
         "tone", qs (strOf "tone" k)
         "dismissable", boolLit (boolOf "dismissable" k) ]
       @ (match JsonValue.tryField "heading" k with
          | Some h -> [ "heading", tsTextInput h ]
          | None -> [])
       @ (match optStr "icon" k with
          | Some i -> [ "icon", qs i ]
          | None -> []))
  | "Progress" ->
    call
      "progress"
      ([ idF
         "fraction", tsBinding Opq.Scalar (fieldReq "fraction" k)
         "indeterminate", boolLit (boolOf "indeterminate" k)
         "tone", qs (strOf "tone" k) ]
       @ (match JsonValue.tryField "label" k with
          | Some l -> [ "label", tsTextInput l ]
          | None -> [])
       @ (match JsonValue.tryField "caveat" k with
          | Some c -> [ "caveat", tsTextInput c ]
          | None -> []))
  | "LabelValueRow" ->
    call
      "labelValueRow"
      ([ idF
         "label", tsTextInput (fieldReq "label" k)
         "value", tsBinding Opq.Scalar (valueOrSource k)
         "format", tsCellFormat (fieldOrIdentity "format" k)
         "emphasis", boolLit (boolOf "emphasis" k) ]
       @ (match JsonValue.tryField "help" k with
          | Some h -> [ "help", tsTextInput h ]
          | None -> []))
  | "Link" ->
    call
      "link"
      ([ idF
         "href", tsBinding Opq.Scalar (fieldReq "href" k)
         "label", tsTextInput (fieldReq "label" k)
         "download", boolLit (boolOf "download" k) ]
       @ (match optStr "rel" k with
          | Some r -> [ "rel", qs r ]
          | None -> [])
       @ (match optStr "target" k with
          | Some t -> [ "target", qs t ]
          | None -> [])
       @ (match optStr "protection" k with
          | Some p -> [ "protection", qs p ]
          | None -> []))
  | "Image" ->
    call
      "image"
      ([ idF
         "src", tsBinding Opq.Scalar (fieldReq "src" k)
         "alt", tsTextInput (fieldReq "alt" k)
         "variant", qs (strOf "variant" k) ]
       // The presentation slots (fit / aspectRatio / loading) and the figure
       // slots (caption / expandable) are each optional and omitted-when-absent.
       @ (match optStr "fit" k with
          | Some f -> [ "fit", qs f ]
          | None -> [])
       @ (match optStr "aspectRatio" k with
          | Some a -> [ "aspectRatio", qs a ]
          | None -> [])
       @ (match optStr "loading" k with
          | Some l -> [ "loading", qs l ]
          | None -> [])
       @ (match JsonValue.tryField "caption" k with
          | Some c -> [ "caption", tsTextInput c ]
          | None -> [])
       @ (match JsonValue.tryField "expandable" k with
          | Some(JBool b) -> [ "expandable", boolLit b ]
          | _ -> [])
       // Phase 1080 — the candidate renditions. `width` is the descriptor the
       // browser selects on; the entry's `src` is an ordinary string binding.
       @ (match JsonValue.tryField "srcSet" k with
          | Some(JArray entries) ->
            [ "srcSet",
              "["
              + (entries
                 |> List.map (fun e ->
                   tsInline
                     [ "src", tsBinding Opq.Scalar (fieldReq "src" e)
                       "width", numLit (numOf "width" e) ])
                 |> String.concat ", ")
              + "]" ]
          | _ -> []))
  // Phase 821 — the standalone icon display kind. `fuaran.icon` is the
  // decorative shorthand (id + name only); the full record needs `iconSpec`,
  // whose `tone` is required in memory and omitted-on-default on the wire.
  | "Icon" ->
    Some(
      "fuaran.iconSpec("
      + qs id
      + ", "
      + tsInline (
        [ "icon", qs (strOf "icon" k)
          "size", qs (optStr "size" k |> Option.defaultValue "Medium")
          "tone", qs (optStr "tone" k |> Option.defaultValue "Default") ]
        @ (match optStr "label" k with
           | Some l -> [ "label", qs l ]
           | None -> [])
      )
      + ")"
    )
  // `Media` splits by its inner `kind` into two ctors with different surfaces:
  // only `Video` carries `autoplay` / `poster`. `controls` and `loop` default
  // to true / false respectively and are omitted on the wire at their default.
  | "Media" ->
    let inner = fieldReq "kind" k
    let isVideo = (dollarType inner |> Option.defaultValue "Video") = "Video"

    let common =
      [ idF
        "src", tsBinding Opq.Scalar (fieldReq "src" k)
        "label", tsTextInput (fieldReq "label" k) ]
      @ (match JsonValue.tryField "controls" k with
         | Some(JBool b) -> [ "controls", boolLit b ]
         | _ -> [])
      @ (match JsonValue.tryField "loop" k with
         | Some(JBool b) -> [ "loop", boolLit b ]
         | _ -> [])
      // Phase 1114 — the timed-text `tracks` and the `transcript` fallback. A
      // track's `label` is a `TextSource` in memory though the wire spells it as
      // a bare string; `default` is omitted-when-false.
      @ (match JsonValue.tryField "tracks" k with
         | Some(JArray ts) -> [ "tracks", "[" + (ts |> List.map tsMediaTrack |> String.concat ", ") + "]" ]
         | _ -> [])
      @ (match JsonValue.tryField "transcript" k with
         | Some t -> [ "transcript", tsTextInput t ]
         | None -> [])

    if isVideo then
      call
        "video"
        (common
         @ (match JsonValue.tryField "autoplay" inner with
            | Some(JBool b) -> [ "autoplay", boolLit b ]
            | _ -> [])
         @ (match JsonValue.tryField "poster" inner with
            | Some p -> [ "poster", tsBinding Opq.Scalar p ]
            | None -> []))
    else
      call "audio" common
  // Phase 1111 — the sandboxed third-party embed. `aspectRatio` defaults to
  // `Natural` and `permissions` to the empty list (total denial); the encoder
  // omits both at their default, so each rides only where the wire asserts it.
  | "Embed" ->
    call
      "embed"
      ([ idF
         "src", tsBinding Opq.Scalar (fieldReq "src" k)
         "title", tsTextInput (fieldReq "title" k) ]
       @ (match optStr "aspectRatio" k with
          | Some a -> [ "aspectRatio", qs a ]
          | None -> [])
       @ (match JsonValue.tryField "permissions" k with
          | Some(JArray ps) -> [ "permissions", "[" + (ps |> List.map strItem |> String.concat ", ") + "]" ]
          | _ -> []))
  // Phase 1120 — the hierarchical disclosure list. `onSelect` is a closure the
  // encoder erases, so its presence (not its body) is what must be projected.
  | "Tree" ->
    call
      "tree"
      ([ idF
         "items", "[" + (arrOf "items" k |> List.map tsTreeItem |> String.concat ", ") + "]" ]
       @ (match optStr "expandedStateKey" k with
          | Some s -> [ "expandedStateKey", qs s ]
          | None -> [])
       @ (match optStr "selectionStateKey" k with
          | Some s -> [ "selectionStateKey", qs s ]
          | None -> [])
       @ (match JsonValue.tryField "onSelect" k with
          | Some _ -> [ "onSelect", "() => action.chain([])" ]
          | None -> []))
  | "List" ->
    call
      "list"
      [ idF
        "items", "[" + (arrOf "items" k |> List.map tsTextInput |> String.concat ", ") + "]"
        "ordered", boolLit (boolOf "ordered" k) ]
  | "Divider" ->
    call
      "divider"
      ([ idF; "orientation", qs (strOf "orientation" k) ]
       @ (match JsonValue.tryField "label" k with
          | Some l -> [ "label", tsTextInput l ]
          | None -> []))
  | "Toast" ->
    // 0.2.0 — a Toast is dismissable UNLESS said otherwise (omitted-when-true);
    // an absent wire field means `true`, an explicit `false` rides.
    call
      "toast"
      [ idF
        "message", tsTextInput (fieldReq "message" k)
        "tone", qs (strOf "tone" k)
        "open", tsBinding Opq.Scalar (fieldReq "open" k)
        "dismissable",
        (match JsonValue.tryField "dismissable" k with
         | Some(JBool b) -> boolLit b
         | _ -> "true") ]
  | "CodeBlock" ->
    call
      "codeBlock"
      [ idF
        "code", qs (strOf "code" k)
        "language", qs (strOf "language" k)
        "lineNumbers", boolLit (boolOf "lineNumbers" k)
        "highlightLines", "[" + (arrOf "highlightLines" k |> List.map numItem |> String.concat ", ") + "]"
        "copyable", boolLit (boolOf "copyable" k) ]
  | "Math" -> call "math" [ idF; "source", qs (strOf "source" k); "display", qs (strOf "display" k) ]
  // ── Input ─────────────────────────────────────────────────────────────────
  | "Button" ->
    call
      "button"
      ([ idF
         "label", tsTextInput (fieldReq "label" k)
         "onClick", tsAction (fieldReq "onClick" k)
         "variant", qs (strOf "variant" k) ]
       @ (match optStr "icon" k with
          | Some i -> [ "icon", qs i ]
          | None -> [])
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
          | None -> []))
  | "Select" ->
    call
      "select"
      ([ idF
         "label", tsTextInput (fieldReq "label" k)
         "source", tsOptionsBinding (fieldReq "source" k)
         "value", tsBinding Opq.Scalar (fieldReq "value" k) ]
       @ (match JsonValue.tryField "onChange" k with
          | Some _ -> [ "onChange", "() => action.chain([])" ]
          | None -> [])
       @ (match JsonValue.tryField "placeholder" k with
          | Some p -> [ "placeholder", tsTextInput p ]
          | None -> [])
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
          | None -> [])
       @ (if boolOf "multiple" k then [ "multiple", "true" ] else [])
       @ (match JsonValue.tryField "values" k with
          | Some vs -> [ "values", tsBinding Opq.Collection vs ]
          | None -> [])
       @ (match JsonValue.tryField "onChangeMulti" k with
          | Some _ -> [ "onChangeMulti", "() => action.chain([])" ]
          | None -> []))
  | "Form" ->
    let fs = arrOf "fields" k

    let fieldsArr =
      if List.isEmpty fs then
        "[]"
      else
        "[\n"
        + (fs
           |> List.map (fun f -> pad (depth + 2) + tsFormField (depth + 2) f)
           |> String.concat ",\n")
        + ",\n"
        + pad (depth + 1)
        + "]"

    call
      "form"
      ([ idF
         "fields", fieldsArr
         "onSubmit", tsAction (fieldReq "onSubmit" k)
         "submitLabel", tsTextInput (fieldReq "submitLabel" k) ]
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
          | None -> []))
  | "Filters" ->
    let items = arrOf "items" k

    let specsArr =
      if List.isEmpty items then
        "[]"
      else
        "[\n"
        + (items
           |> List.map (fun s -> pad (depth + 2) + tsFilterSpec (depth + 2) s)
           |> String.concat ",\n")
        + ",\n"
        + pad (depth + 1)
        + "]"

    call "filters" [ idF; "filters", specsArr ]
  | "FileUpload" ->
    call
      "fileUpload"
      ([ idF
         "label", tsTextInput (fieldReq "label" k)
         "accept", "[" + (arrOf "accept" k |> List.map strItem |> String.concat ", ") + "]"
         "multiple", boolLit (boolOf "multiple" k)
         "onSelect", "() => action.chain([])" ]
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
          | None -> [])
       // Phase 1123 — the intake affordances. `dropTarget` / `acceptPaste` are
       // `false`-by-default booleans the encoder omits at their default;
       // `capture` names a device and `destination` a server-side bucket, both
       // optional strings omitted when absent.
       @ (if boolOf "dropTarget" k then
            [ "dropTarget", "true" ]
          else
            [])
       @ (if boolOf "acceptPaste" k then
            [ "acceptPaste", "true" ]
          else
            [])
       @ (match optStr "capture" k with
          | Some c -> [ "capture", qs c ]
          | None -> [])
       @ (match optStr "destination" k with
          | Some d -> [ "destination", qs d ]
          | None -> []))
  // ── Visualisation ─────────────────────────────────────────────────────────
  | "Chart" ->
    let ctorExpr =
      "fuaran.chart("
      + tsObjLit
          ([ idF
             "source", tsBinding Opq.Collection (fieldReq "source" k)
             "xField", qs (strOf "xField" k)
             "yFields", "[" + (arrOf "yFields" k |> List.map strItem |> String.concat ", ") + "]"
             "kind", qs (strOf "kind" k)
             "stacked", boolLit (boolOf "stacked" k) ]
           @ (match JsonValue.tryField "title" k with
              | Some t -> [ "title", tsTextInput t ]
              | None -> []))
          depth
      + ")"

    // `ChartOptions` reaches only the five core slots plus `title`; the axis
    // titles, subtitle, value format, legend placement, data labels and x-scale
    // are all `ChartSpec` slots with NO ctor surface in the TS tier yet. So the
    // projection post-edits the built node's spec — the Switch-arm precedent:
    // exact, executable, and honest about the ctor gap rather than silently
    // dropping the slot. The three text slots are raw `TextSource` here (no
    // ctor `text()` coercion runs), so they take the explicit object form.
    let extras =
      (match JsonValue.tryField "subtitle" k with
       | Some s -> [ "subtitle", tsTextSourceLit s ]
       | None -> [])
      @ (match JsonValue.tryField "xTitle" k with
         | Some t -> [ "xTitle", tsTextSourceLit t ]
         | None -> [])
      @ (match JsonValue.tryField "yTitle" k with
         | Some t -> [ "yTitle", tsTextSourceLit t ]
         | None -> [])
      @ (match JsonValue.tryField "valueFormat" k with
         | Some f -> [ "valueFormat", tsFormatIntent f ]
         | None -> [])
      @ (match optStr "legendPosition" k with
         | Some p -> [ "legendPosition", qs p ]
         | None -> [])
      @ (match optStr "dataLabels" k with
         | Some d -> [ "dataLabels", qs d ]
         | None -> [])
      @ (match optStr "xScale" k with
         | Some s -> [ "xScale", qs s ]
         | None -> [])
      @ (match JsonValue.tryField "annotations" k with
         | Some(JArray anns) ->
           [ "annotations", "[" + (anns |> List.map tsChartAnnotation |> String.concat ", ") + "]" ]
         | _ -> [])

    if List.isEmpty extras then
      Some ctorExpr
    else
      Some(
        "(() => { const n = "
        + ctorExpr
        + "; const v = n.kind.visualisation; return { ...n, kind: { ...n.kind, visualisation: { ...v, spec: { ...v.spec, "
        + (extras
           |> List.map (fun (key, value) -> key + ": " + value)
           |> String.concat ", ")
        + " } } } }; })()"
      )
  | "Table" ->
    let rows =
      arrOf "rows" k
      |> List.map (fun row ->
        match row with
        | JArray cells -> "[" + (cells |> List.map tsTextInput |> String.concat ", ") + "]"
        | _ -> "[]")
      |> String.concat ", "

    call
      "table"
      [ idF
        "headers", "[" + (arrOf "headers" k |> List.map tsTextInput |> String.concat ", ") + "]"
        "rows", "[" + rows + "]" ]
  | "Map" ->
    call
      "map"
      [ idF
        "source", tsMarkerBinding (fieldReq "source" k)
        "centreLatitude", numLit (numOf "centreLatitude" k)
        "centreLongitude", numLit (numOf "centreLongitude" k)
        "zoom", numLit (numOf "zoom" k) ]
  | "DataGrid" ->
    call
      "grid"
      ([ idF
         "source", tsBinding Opq.Collection (fieldReq "source" k)
         "rowKey", "() => ''"
         "columns", "[" + (arrOf "columns" k |> List.map tsGridColumn |> String.concat ", ") + "]"
         "editable", boolLit (boolOf "editable" k) ]
       @ (match JsonValue.tryField "onRowClick" k with
          | Some _ -> [ "onRowClick", "() => action.chain([])" ]
          | None -> []))
  // ── Custom / ErrorBoundary / Fragments ────────────────────────────────────
  | "Custom" ->
    call
      "custom"
      ([ idF
         "moduleId", qs (strOf "moduleId" k)
         "componentId", qs (strOf "componentId" k)
         "props", tsJson (fieldReq "props" k) ]
       @ (match JsonValue.tryField "contentHash" k with
          | Some h ->
            [ "contentHash",
              tsInline
                [ "algorithm", qs (strOf "algorithm" h)
                  "hash", qs (strOf "hash" h)
                  "strictness", qs (strOf "strictness" h) ] ]
          | None -> [])
       @ (let ids = arrOf "exposedNodeIds" k

          if List.isEmpty ids then
            []
          else
            [ "exposedNodeIds",
              "["
              + (ids |> List.map (fun n -> "nodeId(" + strItem n + ")") |> String.concat ", ")
              + "]" ]))
  | "ErrorBoundary" ->
    call
      "errorBoundary"
      [ idF
        "child", tsNodeExpr (depth + 1) (fieldReq "child" k)
        "fallback", tsNodeExpr (depth + 1) (fieldReq "fallback" k) ]
  | "FragmentDecl" ->
    call
      "fragmentDecl"
      ([ idF
         "name", qs (strOf "name" k)
         "body", tsNodeExpr (depth + 1) (fieldReq "body" k) ]
       @ (let holes = arrOf "holes" k

          if List.isEmpty holes then
            []
          else
            [ "holes", "[" + (holes |> List.map tsHoleDecl |> String.concat ", ") + "]" ])
       @ (match JsonValue.tryField "effect" k with
          | Some e ->
            [ "effect",
              tsInline
                [ "hostEffect", qs (strOf "hostEffect" e)
                  "determinism", qs (strOf "determinism" e) ] ]
          | None -> []))
  | "FragmentRef" ->
    call
      "fragmentRef"
      ([ idF; "name", qs (strOf "name" k) ]
       @ (let args = membersOf "args" k

          if List.isEmpty args then
            []
          else
            [ "args",
              "{ "
              + (args
                 |> List.map (fun (key, a) -> jsKey key + ": " + tsFragArg depth a)
                 |> String.concat ", ")
              + " }" ]))
  | "Switch" ->
    let caseVs = arrOf "cases" k

    // Phase 1535 — a case selects on `match` (the selector's string form) OR on
    // `when` (a predicate consulting no selector at all), never both. The ctor
    // maps every case to `{ match, child }` and drops anything else, so a `when`
    // case reaches the encoder carrying NEITHER key — silently, since an absent
    // `match` is simply omitted. Predicate cases therefore ride the same spec
    // post-edit the Phase 768 `on` selector takes.
    let cases =
      caseVs
      |> List.map (fun c ->
        (match JsonValue.tryField "when" c with
         | Some w -> "{ when: " + tsBinding Opq.Scalar w + ", "
         | None -> "{ match: " + qs (strOf "match" c) + ", ")
        + "child: "
        + tsNodeExpr (depth + 1) (fieldReq "child" c)
        + " }")
      |> String.concat ", "

    let hasPredicate =
      caseVs |> List.exists (fun c -> (JsonValue.tryField "when" c).IsSome)

    // Phase 768 — the selector is any Binding. The State form keeps its compact
    // `stateKey` wire spelling and the smart-ctor expresses it directly; a
    // non-State `on` (Selection etc.) has NO ctor surface in the TS tier yet,
    // so the projection post-edits the built node's spec — exact, executable,
    // and honest about the ctor gap (a future SwitchOptions.on simplifies it).
    let onV = JsonValue.tryField "on" k

    let specOverrides =
      (if hasPredicate then [ "cases", "[" + cases + "]" ] else [])
      @ (match onV with
         | Some o -> [ "on", tsBinding Opq.Scalar o ]
         | None -> [])

    let ctorFields =
      [ idF
        "stateKey", (if onV.IsSome then "''" else qs (strOf "stateKey" k))
        // The cases the ctor would drop are supplied by the post-edit instead of
        // being built twice — the child subtrees are projected once either way.
        "cases", (if hasPredicate then "[]" else "[" + cases + "]")
        "default", tsNodeExpr (depth + 1) (fieldReq "default" k) ]
      // Phase 1531 — the carousel's self-advance interval, optional and
      // omitted when absent (a switch with no interval never advances itself).
      @ (match optNum "autoAdvanceMs" k with
         | Some ms -> [ "autoAdvanceMs", numLit ms ]
         | None -> [])

    if List.isEmpty specOverrides then
      call "switch" ctorFields
    else
      Some(
        "(() => { const n = fuaran.switch("
        + tsObjLit ctorFields depth
        + "); return { ...n, kind: { ...n.kind, spec: { ...n.kind.spec, "
        + (specOverrides |> List.map (fun (n, x) -> n + ": " + x) |> String.concat ", ")
        + " } } }; })()"
      )
  | _ -> None

// ─── Python (fuaran_py.ui) — per-kind exact emission (Phase 1142) ─────────────
//
// The 27.F/281 shape, transposed. Every corpus-reachable construct is emitted
// against the real `fuaran_py` authoring surface — `fuaran.*` smart constructors
// for nodes, `binding.*` / `action.*` / `format.*` for the cross-cutting
// vocabulary, and the typed model `fuaran_py.schema.types` (imported as `t`,
// with the compute layer as `cp`) for the records those namespaces do not reach.
// Executing the emitted expression and passing the result to `fuaran_py.ui.encode`
// re-encodes byte-identically to the wire fixture; the Python arm under
// `tests/projection-conformance/` is the gate.
//
// Two differences from the TypeScript leg shape every function below.
//
//  • The Python surface takes KEYWORD ARGUMENTS rather than an options object,
//    so a constructor call is `fuaran.stack('id', children=[…])`, and a nested
//    record is a typed dataclass call rather than a re-spelling of the wire.
//  • There is NO structural escape hatch that composes with the typed layer:
//    `encode` calls `.to_wire()` on the root and `fuaran_py.model.Obj` has no
//    such method, so a construct the typed model does not carry cannot be
//    projected exactly at all. Those are named — with the missing construct — in
//    the arm's quarantine and in docs/PROJECTION_FIDELITY.md rather than being
//    sketched into a shape that would read as faithful.
//
//    That second point needs one qualification since fuaran-py 0.1.0, and the
//    qualification is what keeps the quarantine meaningful. Three slots are
//    typed as a raw wire `Value` rather than as the record they describe —
//    `UiNode.visible`, `SwitchCase.when` and `Navigate.route` — and the encoder
//    accepts a hand-built `Obj` in each. Taking that would dissolve the whole
//    quarantine, because `_lower` passes an `Obj` through wherever a `Binding`
//    is expected, so `Binding.Expr` and `Binding.Query` would "project" in every
//    slot while remaining unmodelled. THE RULE HERE IS THEREFORE: build the
//    value from a TYPED RECORD, and lower it with that record's own `to_wire`
//    where the host's slot does not lower it for you. That can spell exactly
//    what the typed model carries and nothing more, so an absent case is still
//    absent — which is what the arm is measuring.

let private pq (s: string) : string = "'" + escape '\'' s + "'"

let private pyBool (b: bool) : string = if b then "True" else "False"

/// A number as a Python literal. Python distinguishes `int` from `float` where
/// JavaScript does not, and the canonical encoder honours that distinction — so
/// a whole number large enough that the wire carries it in exponent form has to
/// reach Python as a float. `123456789012345680` and `123456789012345680.0`
/// encode differently and only the second matches the wire; a rendering that
/// already carries a point or an exponent is a float in Python as it stands.
let private pyNum (n: float) : string =
  let rendered = numLit n

  if rendered.Contains "." || rendered.Contains "e" || rendered.Contains "E" then
    rendered
  elif n = floor n && abs n >= 1e15 then
    rendered + ".0"
  else
    rendered

/// A typed FLOAT slot, read from the wire (Phase 1596). §7's non-finite
/// sentinels ride the wire as the STRINGS `"NaN"` / `"Infinity"` /
/// `"-Infinity"`, and a typed slot takes them as the Python floats the canonical
/// encoder writes back as those same strings. The generic fallback this replaces
/// passed the string straight through, which round-tripped by coincidence — the
/// slot is a float, and a string reaching it only survived because nothing typed
/// it on the way.
let private pyFloat (v: JsonValue) : string =
  match v with
  | JString "NaN" -> "float('nan')"
  | JString "Infinity" -> "float('inf')"
  | JString "-Infinity" -> "float('-inf')"
  | JNumber n -> pyNum n
  | _ -> "0"

let private pyFloatOf (name: string) (v: JsonValue) : string = pyFloat (fieldReq name v)

/// A call with positional then keyword arguments; keyword names are snake_case.
let private pyCall (ctor: string) (positional: string list) (kw: (string * string) list) : string =
  let args = positional @ (kw |> List.map (fun (k, v) -> toSnake k + "=" + v))
  ctor + "(" + String.concat ", " args + ")"

let private pyList (items: string list) : string = "[" + String.concat ", " items + "]"

/// A multi-line call — the shape a node constructor takes, so a projected tree
/// reads as authored source rather than as one long line.
let private pyCallBlock (ctor: string) (positional: string list) (kw: (string * string) list) (depth: int) : string =
  if List.isEmpty kw then
    ctor + "(" + String.concat ", " positional + ")"
  else
    let lines =
      (positional |> List.map (fun p -> pad (depth + 1) + p))
      @ (kw |> List.map (fun (k, v) -> pad (depth + 1) + toSnake k + "=" + v))

    ctor + "(\n" + String.concat ",\n" lines + ",\n" + pad depth + ")"

let private pyStrItem (v: JsonValue) : string =
  match v with
  | JString s -> pq s
  | _ -> pq ""

/// A control's handler slot, read from the WIRE KEY's presence and passed
/// explicitly — never left to the record's own default.
///
/// From fuaran-py 0.2.0 every handler is a `bool` flag the encoder turns into
/// the `"<closure>"` sentinel, and the defaults are NOT uniform: `on_change` and
/// `on_select` default True, `on_toggle` and `on_select_tag` default False, and
/// `ToggleField.on_toggle` defaults False where `CheckboxField.on_toggle`
/// defaults True. A projector that omitted the argument would therefore emit
/// whichever sentinel that release happens to default to rather than the one the
/// fixture carries, and would break silently on a release that flipped one. The
/// wire key's presence is the whole signal, so it is the whole input here.
let private pyHandler (wireKey: string) (pyKw: string) (v: JsonValue) : (string * string) list =
  [ pyKw, pyBool (JsonValue.tryField wireKey v).IsSome ]

/// A tri-state `bool | None` slot: `False` and ABSENT are two different
/// documents, so the keyword is emitted only where the wire carries the key.
let private pyTriBool (wireKey: string) (pyKw: string) (v: JsonValue) : (string * string) list =
  match JsonValue.tryField wireKey v with
  | Some(JBool b) -> [ pyKw, pyBool b ]
  | _ -> []

/// A plain `dict` literal (Notify payloads, Custom props) — the one place the
/// projection keeps the wire's own key spelling, because these are data maps
/// rather than record fields.
let rec private pyJson (v: JsonValue) : string =
  match v with
  | JNull -> "None"
  | JBool b -> pyBool b
  | JNumber n -> pyNum n
  | JString s -> pq s
  | JArray xs -> pyList (xs |> List.map pyJson)
  | JObject ms ->
    if List.isEmpty ms then
      "{}"
    else
      "{"
      + (ms |> List.map (fun (k, x) -> pq k + ": " + pyJson x) |> String.concat ", ")
      + "}"

/// A `Binding.Static` payload — the `pyJson` rules, with wire `null` becoming
/// `None`, which `Static` omits on encode (absence is structural).
let private pyStaticValue (v: JsonValue) : string = pyJson v

// ── Compute layer (Binding.Transform) ────────────────────────────────────────
//
// The compute vocabulary lives in `fuaran_py.ui.compute` (imported as `cp`), a
// module separate from the typed schema because several of its names collide
// with binding cases — `Filter` is both a binding and a transform step.

/// A compute cell. The named helpers rather than `cp.Cell(tag, value)`: the
/// Python tag vocabulary is the lower-case COLUMN-TYPE name (`'int'`, `'string'`)
/// where the wire discriminator is the capitalised cell tag (`Int`, `Str`), and
/// the helpers are the only spelling that cannot get that mapping wrong.
let private pyCell (helper: string) (value: string) : string = "cp." + helper + "(" + value + ")"

let private pyCellLit (v: JsonValue) : string =
  match dollarType v with
  | Some "Int" -> pyCell "cell_int" (numLit (numOf "value" v))
  | Some "Float" -> pyCell "cell_float" (pyNum (numOf "value" v))
  | Some "Bool" -> pyCell "cell_bool" (pyBool (boolOf "value" v))
  | Some "Str" -> pyCell "cell_str" (pq (strOf "value" v))
  | Some "Date" -> pyCell "cell_date" (pq (strOf "value" v))
  | Some "Timestamp" -> pyCell "cell_timestamp" (pq (strOf "value" v))
  | _ -> "cp.NULL"

let rec private pyColExpr (v: JsonValue) : string =
  match dollarType v with
  | Some "lit" -> "cp.Lit(" + pyCellLit (fieldReq "cell" v) + ")"
  | Some "binary" ->
    "cp.Binary("
    + pq (strOf "op" v)
    + ", "
    + pyColExpr (fieldReq "left" v)
    + ", "
    + pyColExpr (fieldReq "right" v)
    + ")"
  | Some "not" -> "cp.Not(" + pyColExpr (fieldReq "expr" v) + ")"
  | Some "coalesce" -> "cp.Coalesce(" + pyList (arrOf "exprs" v |> List.map pyColExpr) + ")"
  | Some "case" ->
    let cases =
      arrOf "cases" v
      |> List.map (fun c -> "(" + pyColExpr (fieldReq "when" c) + ", " + pyColExpr (fieldReq "then" c) + ")")

    "cp.Case(" + pyList cases + ", " + pyColExpr (fieldReq "else" v) + ")"
  | Some "cast" -> "cp.Cast(" + pq (strOf "type" v) + ", " + pyColExpr (fieldReq "expr" v) + ")"
  | Some "apply" ->
    "cp.ApplyFn("
    + pq (strOf "fn" v)
    + ", "
    + pyList (arrOf "args" v |> List.map pyColExpr)
    + ")"
  // fuaran#1170 — a declared parameter read at evaluation time. It carries a
  // `name` exactly as a `col` does, so without its own arm it fell through to
  // the `col` fallback and projected as a COLUMN reference: same shape, wrong
  // discriminator, and nothing raised.
  | Some "param" -> "cp.Param(" + pq (strOf "name" v) + ")"
  // Phase 1581 — membership. The same trap the `param` arm above records, one
  // discriminator further on: with no arm of its own `in` fell through to the
  // `col` fallback and projected as `cp.Col('')` — a column reference named by
  // a key the node does not carry, which neither raises nor resembles the wire.
  // It was found by re-deriving `multiselect-chip-list-param`'s reason, which
  // had blamed the host for a handler slot 0.2.0 made expressible.
  //
  // `cp.InList` is the sibling case for a literal set. The corpus reaches only
  // the parameterised form today, so that branch is the mapping rather than a
  // measurement — the discriminator is shared and the fallback is what a missing
  // arm costs here.
  | Some "in" ->
    (match optStr "param" v with
     | Some p -> "cp.InParam(" + pyColExpr (fieldReq "expr" v) + ", " + pq p + ")"
     | None ->
       "cp.InList("
       + pyColExpr (fieldReq "expr" v)
       + ", "
       + pyList (arrOf "items" v |> List.map pyColExpr)
       + ")")
  | _ -> "cp.Col(" + pq (strOf "name" v) + ")"

let private pyDataSource (v: JsonValue) : string =
  match optStr "ref" v with
  | Some name -> "cp.Ref(" + pq name + ")"
  | None ->
    let schema = arrOf "schema" v
    let cols = membersOf "columns" v

    let schemaLits =
      schema
      |> List.map (fun e -> "(" + pq (strOf "name" e) + ", " + pq (strOf "type" e) + ")")

    let columnLits =
      schema
      |> List.map (fun e ->
        let name = strOf "name" e
        let ty = strOf "type" e

        let col =
          cols
          |> List.tryFind (fun (k, _) -> k = name)
          |> Option.map snd
          |> Option.defaultValue JNull

        let validity = arrOf "validity" col

        let cells =
          arrOf "values" col
          |> List.mapi (fun i value ->
            match List.tryItem i validity with
            | Some(JBool false) -> "cp.NULL"
            | _ ->
              match ty, value with
              | "int", JNumber n -> pyCell "cell_int" (numLit n)
              | "float", JNumber n -> pyCell "cell_float" (pyNum n)
              | "bool", JBool b -> pyCell "cell_bool" (pyBool b)
              | "date", JString s -> pyCell "cell_date" (pq s)
              | "timestamp", JString s -> pyCell "cell_timestamp" (pq s)
              | _, JString s -> pyCell "cell_str" (pq s)
              | _ -> "cp.NULL")

        "cp.Column(" + pq name + ", " + pq ty + ", " + pyList cells + ")")

    "cp.Embedded(cp.Table(" + pyList schemaLits + ", " + pyList columnLits + "))"

let private pyTransformStep (v: JsonValue) : string =
  let pair (p: JsonValue) =
    "(" + pq (strOf "a" p) + ", " + pq (strOf "b" p) + ")"

  let sortKey (s: JsonValue) =
    "(" + pq (strOf "col" s) + ", " + pq (strOf "dir" s) + ")"

  let strArr (name: string) =
    pyList (arrOf name v |> List.map pyStrItem)

  match dollarType v with
  | Some "filter" -> "cp.Filter(" + pyColExpr (fieldReq "pred" v) + ")"
  | Some "project" -> "cp.Project(" + pyList (arrOf "cols" v |> List.map pair) + ")"
  | Some "derive" -> "cp.Derive(" + pq (strOf "name" v) + ", " + pyColExpr (fieldReq "expr" v) + ")"
  | Some "groupBy" ->
    let aggs =
      arrOf "aggs" v
      |> List.map (fun a ->
        "cp.Agg("
        + pq (strOf "name" a)
        + ", "
        + pq (strOf "fn" a)
        + ", "
        + pq (strOf "of" a)
        + ")")

    "cp.GroupBy(" + strArr "keys" + ", " + pyList aggs + ")"
  | Some "join" ->
    "cp.Join("
    + pyDataSource (fieldReq "source" v)
    + ", "
    + pyList (arrOf "on" v |> List.map pair)
    + ", "
    + pq (strOf "how" v)
    + ")"
  | Some "window" ->
    "cp.Window(cp.WindowSpec("
    + strArr "partitionBy"
    + ", "
    + pyList (arrOf "orderBy" v |> List.map sortKey)
    + ", "
    + pq (strOf "fn" v)
    + ", "
    + pq (strOf "of" v)
    + ", "
    + pq (strOf "as" v)
    + "))"
  | Some "pivot" ->
    "cp.Pivot(cp.PivotSpec("
    + strArr "index"
    + ", "
    + pq (strOf "on" v)
    + ", "
    + pq (strOf "values" v)
    + ", "
    + pq (strOf "agg" v)
    + "))"
  | Some "unpivot" -> "cp.Unpivot(" + strArr "idVars" + ", " + strArr "valueVars" + ")"
  | Some "sort" -> "cp.Sort(" + pyList (arrOf "by" v |> List.map sortKey) + ")"
  | Some "limit" -> "cp.Limit(" + numLit (numOf "n" v) + ", " + numLit (numOf "offset" v) + ")"
  | Some "union" -> "cp.Union(" + pyDataSource (fieldReq "source" v) + ")"
  | _ -> "cp.Distinct()"

// ── Bindings / actions / text / formats ──────────────────────────────────────

/// A COLUMN's `CellFormat` — the `format.*` helper namespace.
///
/// Deliberately adjacent to `pyFormatIntent` below, which projects the `Format`
/// union instead: the two vocabularies overlap by name and differ by one wire
/// key, and reaching for the wrong one is a silent byte difference rather than
/// an error. `format.currency` emits `"code"`; `t.FmtCurrency` emits `"isoCode"`.
/// A column takes the first; `Chart.value_format` and `binding.format` take the
/// second. Moved above `pyBinding` in Phase 1581, which gave the `Local` case a
/// `codec` slot and so made a binding depend on this.
let private pyCellFormat (v: JsonValue) : string =
  match dollarType v with
  | Some "Number" ->
    (match optNum "decimals" v with
     | Some d -> "format.number(" + numLit d + ")"
     | None -> "format.number()")
  | Some "Currency" -> "format.currency(" + pq (strOf "code" v) + ")"
  | Some "Percent" ->
    (match optNum "decimals" v with
     | Some d -> "format.percent(" + numLit d + ")"
     | None -> "format.percent()")
  | Some "SignificantDigits" -> "format.significant_digits(" + numLit (numOf "digits" v) + ")"
  | Some "Date" -> "format.date(" + pq (strOf "format" v) + ")"
  | Some "Duration" -> "format.duration(" + pq (strOf "unit" v) + ", " + pq (strOf "style" v) + ")"
  | Some "RelativeTime" -> "format.relative_time(" + pq (strOf "unit" v) + ")"
  | _ -> "format.none()"

let private pyFormatIntent (v: JsonValue) : string =
  match dollarType v with
  | Some "Currency" -> "t.FmtCurrency(" + pq (strOf "isoCode" v) + ")"
  | Some "Percent" ->
    (match optNum "decimals" v with
     | Some d -> "t.FmtPercent(" + numLit d + ")"
     | None -> "t.FmtPercent()")
  | Some "Date" -> "t.FmtDate(" + pq (strOf "dateStyle" v) + ")"
  | Some "RelativeTime" -> "t.FmtRelativeTime(" + pq (strOf "unit" v) + ")"
  | Some "Duration" -> "t.FmtDuration(" + pq (strOf "unit" v) + ", " + pq (strOf "style" v) + ")"
  // Phase 1533 — elapsed-time-since, modelled by fuaran-py from 0.1.0. `unit`
  // absent is the auto-selection request rather than a default to spell out, so
  // the projection omits the argument exactly where the wire omits the field.
  | Some "Since" ->
    (match optStr "unit" v with
     | Some u -> "t.FmtSince(" + pq u + ")"
     | None -> "t.FmtSince()")
  | _ ->
    (match optNum "decimals" v with
     | Some d -> "t.FmtNumber(" + numLit d + ")"
     | None -> "t.FmtNumber()")

let private pyLocaleSource (v: JsonValue) : string =
  match dollarType v with
  | Some "Explicit" -> "t.Explicit(" + pq (strOf "tag" v) + ")"
  | _ -> "t.Ambient()"

let private pyFlushTrigger (v: JsonValue) : string =
  match dollarType v with
  | Some "OnDebounce" -> "t.OnDebounce(" + numLit (numOf "milliseconds" v) + ")"
  | _ -> "t.OnBlur()"

/// An `Invoke` — the SAME typed record in a value slot and in an action slot
/// (Phase 1596, modelled by fuaran-py from 0.3.0), so both legs share this one
/// spelling rather than each growing its own. `args` is written by the record
/// unconditionally, and `InvokeArg.value` is a string in the model as it is on
/// the wire.
let private pyInvoke (v: JsonValue) : string =
  "t.Invoke("
  + pq (strOf "capabilityId" v)
  + ", "
  + pyList (
    arrOf "args" v
    |> List.map (fun a -> "t.InvokeArg(" + pq (strOf "addr" a) + ", " + pq (strOf "value" a) + ")")
  )
  + ")"

let rec private pyBinding (opq: Opq) (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JString "<opaque>") ->
       (match opq with
        | Opq.Collection -> "binding.static([])"
        | Opq.Scalar -> "binding.static('<opaque>')")
     | Some value -> "binding.static(" + pyStaticValue value + ")"
     | None -> "binding.static(None)")
  | Some "Filter" -> "binding.filter(" + pq (strOf "name" v) + ")"
  | Some "Selection" ->
    let kw =
      (match JsonValue.tryField "defaultValue" v with
       | Some d -> [ "defaultValue", pyStaticValue d ]
       | None -> [])
      @ (match optStr "field" v with
         | Some f -> [ "field", pq f ]
         | None -> [])

    pyCall "binding.selection" [ pq (strOf "nodeId" v) ] kw
  | Some "State" ->
    // absent-is-omit spelled as the identity default, for the same reason as the
    // TS arm and a sharper one: `fuaran_py`'s `binding.state` takes
    // `default_value` as a REQUIRED POSITIONAL, so omitting it is a TypeError
    // rather than a shorter spelling.
    "binding.state("
    + pq (strOf "key" v)
    + ", "
    + pyStaticValue (fieldOrIdentity "defaultValue" v)
    + ")"
  // Phase 1533 — `grain` is the one thing a `Now` carries on the wire, and only
  // when it is not the `Second` default. `binding.now()` takes no argument, so a
  // grain-bearing Now takes the typed record; a bare one keeps the helper.
  | Some "Now" ->
    (match optStr "grain" v with
     | Some g -> "t.Now(" + pq g + ")"
     | None -> "binding.now()")
  // Phase 1581 — `commit_to` / `codec` are modelled from 0.2.0, and with them
  // the DECLARATIVE buffer becomes reachable. `on_commit` is deliberately left
  // to the record: it defaults to `commit_to is None`, which is exactly the
  // wire's own rule (a buffer that names a commit target writes no `onCommit`
  // closure), so stating it would restate the host's semantics rather than read
  // the wire's. The wire refuses a document carrying both commit spellings, so
  // there is no case where both are present to disagree about.
  | Some "Local" ->
    pyCall
      "binding.local"
      [ pyBinding Opq.Scalar (fieldReq "initialFrom" v)
        pyFlushTrigger (fieldReq "flushOn" v) ]
      ((match optStr "commitTo" v with
        | Some c -> [ "commitTo", pq c ]
        | None -> [])
       @ (match JsonValue.tryField "codec" v with
          | Some c -> [ "codec", pyCellFormat c ]
          | None -> []))
  | Some "Format" ->
    "binding.format("
    + pyBinding Opq.Scalar (fieldReq "source" v)
    + ", "
    + pyFormatIntent (fieldReq "format" v)
    + ", "
    + pyLocaleSource (fieldReq "locale" v)
    + ")"
  | Some "Transform" ->
    // `TransformBinding.source` is a bare `DataSource` rather than the wire's
    // `TransformSource` DU, so a `State`- or `Live`-sourced transform still has
    // no spelling and stays quarantined. `params` does have one from 0.1.0 —
    // omitted from the wire when empty, so a param-free binding is unchanged.
    "cp.TransformBinding("
    + pyDataSource (fieldReq "source" v)
    + ", "
    + pyList (arrOf "pipeline" v |> List.map pyTransformStep)
    + (match arrOf "params" v with
       | [] -> ""
       | ps ->
         ", "
         + pyList (
           ps
           |> List.map (fun p ->
             "cp.ParamDecl("
             + pq (strOf "name" p)
             + ", "
             + pyBinding Opq.Scalar (fieldReq "from" p)
             + ")")
         ))
    + ")"
  // Phase 1596 — `Query` and `Invoke`, modelled by fuaran-py from 0.3.0. The
  // deps tuple is omitted from the wire when empty, so a dependency-free query
  // is byte-identical to its pre-`dependsOn` form and the argument is dropped.
  | Some "Query" ->
    (match arrOf "dependsOn" v with
     | [] -> "t.Query(" + pq (strOf "name" v) + ")"
     | deps ->
       "t.Query("
       + pq (strOf "name" v)
       + ", "
       + pyList (deps |> List.map pyStrItem)
       + ")")
  | Some "Invoke" -> pyInvoke v
  | _ ->
    // Expr / Computed — no typed case in `fuaran_py`.
    "binding.static(None)"

/// The explicit `TextSource` record — for slots typed as raw `TextSource`, which
/// the constructors do not coerce from a bare string.
let private pyTextSource (v: JsonValue) : string =
  match v with
  | JString s -> "t.LiteralText(" + pq s + ")"
  | _ ->
    match dollarType v with
    | Some "Bound" -> "t.Bound(" + pyBinding Opq.Scalar (fieldReq "binding" v) + ")"
    // Phase 1596 — the third `TextSource` case, modelled by fuaran-py from
    // 0.3.0. `args` is written unconditionally by the record, so an empty map
    // needs no argument: `t.I18n(key)` and `t.I18n(key, {})` are one document.
    | Some "I18n" ->
      (match membersOf "args" v with
       | [] -> "t.I18n(" + pq (strOf "key" v) + ")"
       | _ -> "t.I18n(" + pq (strOf "key" v) + ", " + pyJson (fieldReq "args" v) + ")")
    | _ -> "t.LiteralText(" + pq (strOf "text" v) + ")"

/// A `TextInput` slot — the bare string the constructors coerce, or the explicit
/// record for the bound / i18n forms.
let private pyTextInput (v: JsonValue) : string =
  match v with
  | JString s -> pq s
  | _ ->
    match dollarType v with
    | Some "Literal" -> pq (strOf "text" v)
    | _ -> pyTextSource v

let private pySelectOption (o: JsonValue) : string =
  "t.SelectOption("
  + pyTextSource (fieldReq "label" o)
  + ", "
  + pq (strOf "value" o)
  + ")"

let private pyOptionsBinding (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JArray opts) -> "binding.static(" + pyList (opts |> List.map pySelectOption) + ")"
     | Some(JString "<opaque>") -> "binding.static([])"
     | _ -> pyBinding Opq.Collection v)
  | _ -> pyBinding Opq.Collection v

let private pyMarker (m: JsonValue) : string =
  "t.MapMarker("
  + pyTextSource (fieldReq "label" m)
  + ", "
  + numLit (numOf "latitude" m)
  + ", "
  + numLit (numOf "longitude" m)
  + ")"

let private pyMarkerBinding (v: JsonValue) : string =
  match dollarType v with
  | Some "Static" ->
    (match JsonValue.tryField "value" v with
     | Some(JArray ms) -> "binding.static(" + pyList (ms |> List.map pyMarker) + ")"
     | Some(JString "<opaque>") -> "binding.static([])"
     | _ -> pyBinding Opq.Collection v)
  | _ -> pyBinding Opq.Collection v

/// A `Call`'s declarative result target (Phase 1596). The Python case names
/// deliberately differ from the wire tags they encode — `t.IntoState` writes
/// `{"$type":"State"}` and `t.IntoQuery` writes `{"$type":"Query"}` — because
/// `State` and `Query` are already taken by the binding union in the same
/// module, so reading the wire tag as the class name is exactly the mistake to
/// avoid here.
let private pyCallTarget (v: JsonValue) : string =
  match dollarType v with
  | Some "Query" -> "t.IntoQuery(" + pq (strOf "name" v) + ")"
  | _ -> "t.IntoState(" + pq (strOf "key" v) + ")"

let rec private pyAction (v: JsonValue) : string =
  match dollarType v with
  | Some "Dispatch" -> "action.dispatch(0)"
  | Some "Notify" ->
    "action.notify("
    + pq (strOf "channel" v)
    + ", "
    + pyJson (fieldReq "payload" v)
    + ")"
  // Phase 1536 — the route is a `TextSource`, so a tree can name a destination
  // it computes from what the reader selected, and `target` names the browsing
  // context (omitted at `Self`). `t.Navigate` places the route into its `Obj`
  // WITHOUT lowering it, so a bound route is handed over already lowered: the
  // typed `t.Bound` record's own `to_wire`, never a hand-built structural
  // literal — see the leg's header note on why that distinction is the whole
  // meaning of the quarantine beside it.
  | Some "Navigate" ->
    let route = fieldReq "route" v

    let routeArg =
      match route with
      | JString s -> pq s
      | _ -> pyTextSource route + ".to_wire()"

    (match optStr "target" v with
     | Some tg when tg <> "Self" -> "action.navigate(" + routeArg + ", " + pq tg + ")"
     | _ -> "action.navigate(" + routeArg + ")")
  | Some "SetState" ->
    (match JsonValue.tryField "valueFrom" v with
     | Some src ->
       "action.set_state_from("
       + pq (strOf "key" v)
       + ", "
       + pyBinding Opq.Scalar src
       + ")"
     | None ->
       "action.set_state("
       + pq (strOf "key" v)
       + ", "
       + pyJson (fieldReq "value" v)
       + ")")
  | Some "Chain" -> "action.chain(" + pyList (arrOf "ops" v |> List.map pyAction) + ")"
  // The clipboard payload is a `TextSource`: `Literal`'s canonical form is the
  // bare JSON string, but a `Bound` payload rides as the envelope and must
  // project as one (reading it with `strOf` erased it to '').
  | Some "WriteToClipboard" -> "action.write_to_clipboard(" + pyTextInput (fieldReq "text" v) + ")"
  // Phase 1124 — the reader's own print dialogue; it takes nothing.
  | Some "Print" -> "action.print()"
  | Some "ReadFileBody" ->
    "action.read_file_body("
    + pq (strOf "fileRef" v)
    + ", "
    + pq (strOf "encoding" v)
    + ")"
  // Phase 1537 — ask, then continue. The first case to recurse into NAMED
  // members rather than a list; `on_cancel` rides only when the wire carries it,
  // and its absence means nothing happens rather than some substituted default.
  // `t.Confirm` lowers its own members, so the prompt is handed over as the
  // typed `TextSource` it is.
  | Some "Confirm" ->
    pyCall
      "t.Confirm"
      [ pyTextInput (fieldReq "prompt" v); pyAction (fieldReq "onConfirm" v) ]
      (match JsonValue.tryField "onCancel" v with
       | Some c -> [ "onCancel", pyAction c ]
       | None -> [])
  // Phase 1537 — a bare node id, never a `TextSource`: it addresses a node in
  // this document, which the author wrote.
  | Some "Focus" -> "t.Focus(" + pq (strOf "nodeId" v) + ")"
  // Phase 1596 — `Call` / `AiTool` / `Invoke`, modelled by fuaran-py from 0.3.0.
  // `on_result` is a bool the record turns into the `"<closure>"` sentinel, and
  // it is read from the WIRE KEY's presence for the reason `pyHandler` states:
  // the record's own default is what a projector that omitted the argument
  // would silently adopt.
  | Some "Call" ->
    pyCall
      "t.Call"
      [ pq (strOf "endpoint" v) ]
      ((match JsonValue.tryField "into" v with
        | Some target -> [ "into", pyCallTarget target ]
        | None -> [])
       @ (match JsonValue.tryField "onResult" v with
          | Some _ -> [ "onResult", "True" ]
          | None -> []))
  | Some "AiTool" -> "t.AiTool(" + pq (strOf "toolName" v) + ", " + pyJson (fieldReq "args" v) + ")"
  | Some "Invoke" -> pyInvoke v
  | _ ->
    // CommitLocal — no typed case in `fuaran_py`.
    "action.chain([])"

// ── Form fields / filters / grid columns / tab headers ───────────────────────

let private pyCtrlDefault (kind: string) : string =
  match kind with
  | "Number"
  | "Rating"
  | "RangedNumber" -> "0"
  | "Checkbox"
  | "Toggle" -> "False"
  | "Combobox"
  | "Choice"
  | "SegmentedChoice" -> "None"
  | "Range" -> "[0, 0]"
  | "DateRange" -> "['', '']"
  // Phase 1121 — an auto-bound token field starts with no chips at all.
  | "Tokens" -> "[]"
  // Phase 1130 — the unset swatch, the one `#rrggbb` form the control can hold.
  | "Color" -> "'#000000'"
  | _ -> "''"

let private pyAutoBindValue (ab: AutoBind) (kind: string) : string =
  match ab with
  | AutoBind.Form id -> "binding.state(" + pq id + ", " + pyCtrlDefault kind + ")"
  | AutoBind.Filter name -> "binding.filter(" + pq name + ")"

let private pyFieldValue (ab: AutoBind) (kind: string) (v: JsonValue) : string =
  match JsonValue.tryField "value" v with
  | Some valV ->
    match kind with
    // Both pair controls carry their explicit value as a bare wire object, and
    // the Python slot takes it as it lies: `_lower` turns a `dict` into the
    // tag-less object the encoder emits, so the pair survives unwrapped. (The
    // TypeScript leg hydrates the same wire into a tuple, because its in-memory
    // shape for these two slots IS a tuple — the difference is the host's, not
    // the wire's.)
    // Phase 1581 — since 0.2.0 models `RangeField`, its literal pair has a TYPED
    // spelling: `value: Binding | tuple[float, float]`, lowered by the record's
    // own `to_wire` into the bare `{"max":…,"min":…}` object. A dict would reach
    // the same bytes through `_lower`, but only by taking a path the annotation
    // does not admit — and "build every value from a typed record" is the rule
    // this whole leg's measurement rests on. `DateRange` keeps its wire-object
    // spelling deliberately: its record is unchanged by this release and the
    // decision to pass the pair through is recorded above.
    | "Range" when (dollarType valV).IsNone -> "(" + pyNum (numOf "min" valV) + ", " + pyNum (numOf "max" valV) + ")"
    | "DateRange" when (dollarType valV).IsNone ->
      "{'from': " + pq (strOf "from" valV) + ", 'to': " + pq (strOf "to" valV) + "}"
    | "DateRange" when
      (dollarType valV) = Some "State"
      && (match JsonValue.tryField "defaultValue" valV with
          | Some d -> (dollarType d).IsNone && (JsonValue.tryField "from" d).IsSome
          | None -> false)
      ->
      // A State binding whose defaultValue is the wire's `{from,to}` pair —
      // carried through as the object it is, for the reason above.
      let d = fieldReq "defaultValue" valV

      "binding.state("
      + pq (strOf "key" valV)
      + ", {'from': "
      + pq (strOf "from" d)
      + ", 'to': "
      + pq (strOf "to" d)
      + "})"
    | _ -> pyBinding Opq.Scalar valV
  | None -> pyAutoBindValue ab kind

let private pyFieldKind (ab: AutoBind) (v: JsonValue) : string =
  let kind = dollarType v |> Option.defaultValue "Text"
  let value = pyFieldValue ab kind v

  /// The `value` slot as a KEYWORD, present only where the wire carries one.
  ///
  /// Every control record made `value` optional in fuaran-py 0.2.0, and absence
  /// is not expressible any other way: `t.Static(None)` encodes
  /// `"value":{"$type":"Static"}`, which is a different document from a control
  /// with no `value` key at all. Before 0.2.0 the slot was required, so the
  /// projector had to reconstruct the control's auto-binding — which is exactly
  /// why the canonical minimal control was unreachable and its fixtures were
  /// quarantined.
  let optValue =
    match JsonValue.tryField "value" v with
    | Some _ -> [ "value", value ]
    | None -> []

  let minMaxStep (isDate: bool) =
    let one name =
      if isDate then
        match optStr name v with
        | Some s -> [ name, pq s ]
        | None -> []
      else
        match optNum name v with
        | Some n -> [ name, numLit n ]
        | None -> []

    one "min"
    @ one "max"
    @ (match optNum "step" v with
       | Some s -> [ "step", numLit s ]
       | None -> [])

  // Every arm below reads its handler slot(s) off the WIRE and passes the flag
  // explicitly (`pyHandler`), and carries `value` only where the wire does
  // (`optValue`). Both became expressible in fuaran-py 0.2.0; before it, a
  // control could neither suppress its handler sentinel nor omit its value, and
  // the whole closure-sentinel quarantine family is what that cost.
  match kind with
  | "Number" -> pyCall "t.NumberField" [] (optValue @ pyHandler "onChange" "on_change" v)
  | "Checkbox" -> pyCall "t.CheckboxField" [] (optValue @ pyHandler "onToggle" "on_toggle" v)
  | "Toggle" -> pyCall "t.ToggleField" [] (optValue @ pyHandler "onToggle" "on_toggle" v)
  | "Choice" ->
    pyCall "t.ChoiceField" [ pyOptionsBinding (fieldReq "options" v) ] (optValue @ pyHandler "onChange" "on_change" v)
  | "SegmentedChoice" ->
    pyCall
      "t.SegmentedChoice"
      [ pyOptionsBinding (fieldReq "options" v) ]
      (optValue
       @ [ "orientation", pq (strOf "orientation" v) ]
       @ pyHandler "onChange" "on_change" v)
  | "TextArea" ->
    pyCall
      "t.TextAreaField"
      []
      (optValue
       @ [ "rows", numLit (numOf "rows" v) ]
       @ pyHandler "onChange" "on_change" v)
  | "RangedNumber" -> pyCall "t.RangedNumber" [] (optValue @ minMaxStep false @ pyHandler "onChange" "on_change" v)
  // Phase 1581 — the slider pair, modelled by fuaran-py from 0.2.0. Before it
  // there was no `RangeField` at all and this kind fell through to the `Text`
  // fallback, which is a different record and a different document.
  | "Range" -> pyCall "t.RangeField" [] (optValue @ minMaxStep false @ pyHandler "onChange" "on_change" v)
  | "Date" ->
    pyCall
      "t.DateField"
      []
      (optValue
       @ [ "variant", pq (strOf "variant" v) ]
       @ minMaxStep true
       @ pyHandler "onChange" "on_change" v)
  | "DateRange" ->
    pyCall
      "t.DateRangeField"
      []
      (optValue
       @ [ "variant", pq (strOf "variant" v) ]
       @ minMaxStep true
       @ pyHandler "onChange" "on_change" v)
  // Phase 1130 — the colour swatch: value only.
  | "Color" -> pyCall "t.ColorField" [] (optValue @ pyHandler "onChange" "on_change" v)
  // Phase 1122 — `max` is the case's only required member; `allow_half` omits
  // at False.
  | "Rating" ->
    pyCall
      "t.RatingField"
      [ numLit (numOf "max" v) ]
      (optValue
       @ (if boolOf "allowHalf" v then
            [ "allow_half", "True" ]
          else
            [])
       @ pyHandler "onChange" "on_change" v)
  // Phase 1119 — `allow_free_text` omits at False here (the opposite polarity
  // to `Tokens`, whose suggestion source is optional and this one's required).
  | "Combobox" ->
    pyCall
      "t.ComboboxField"
      [ pyOptionsBinding (fieldReq "options" v) ]
      (optValue
       @ (if boolOf "allowFreeText" v then
            [ "allow_free_text", "True" ]
          else
            [])
       @ pyHandler "onChange" "on_change" v)
  // Phase 1121 — `allow_free_text` defaults to TRUE, so an ABSENT wire field is
  // `true`: the one place in the field vocabulary where absence is not `false`.
  | "Tokens" ->
    pyCall
      "t.TokensField"
      []
      (optValue
       @ (match JsonValue.tryField "allowFreeText" v with
          | Some(JBool false) -> [ "allow_free_text", "False" ]
          | _ -> [])
       @ (match JsonValue.tryField "suggestions" v with
          | Some s -> [ "suggestions", pyOptionsBinding s ]
          | None -> [])
       @ pyHandler "onChange" "on_change" v)
  | _ -> pyCall "t.TextField" [] (optValue @ pyHandler "onChange" "on_change" v)

/// A filter chip's control — the same vocabulary as a form field, but three of
/// the cases carry their own filter-side dataclass.
let private pyFilterKind (ab: AutoBind) (v: JsonValue) : string =
  let kind = dollarType v |> Option.defaultValue "Text"

  let optValue slot =
    match JsonValue.tryField "value" v with
    | Some _ -> [ "value", pyFieldValue ab slot v ]
    | None -> []

  match kind with
  | "Choice" ->
    pyCall
      "t.ChoiceFilter"
      [ pyOptionsBinding (fieldReq "options" v) ]
      (optValue "Choice" @ pyHandler "onChange" "on_change" v)
  | "SegmentedChoice" ->
    pyCall
      "t.SegmentedFilter"
      [ pyOptionsBinding (fieldReq "options" v) ]
      (optValue "SegmentedChoice"
       @ [ "orientation", pq (strOf "orientation" v) ]
       @ pyHandler "onChange" "on_change" v)
  | "Text" -> pyCall "t.TextFilter" [] (optValue "Text" @ pyHandler "onChange" "on_change" v)
  // Phase 1581 — the filter-side slider. `t.RangeFilter` is the alias of
  // `t.RangeField`, and it arrived with it in 0.2.0.
  | "Range" ->
    pyCall
      "t.RangeFilter"
      []
      (optValue "Range"
       @ (match optNum "min" v with
          | Some n -> [ "min", numLit n ]
          | None -> [])
       @ (match optNum "max" v with
          | Some n -> [ "max", numLit n ]
          | None -> [])
       @ (match optNum "step" v with
          | Some n -> [ "step", numLit n ]
          | None -> [])
       @ pyHandler "onChange" "on_change" v)
  | _ -> pyFieldKind ab v

let private pyFieldRule (v: JsonValue) : string =
  pyCall
    "t.FieldRule"
    []
    ((match optStr "format" v with
      | Some f -> [ "format", pq f ]
      | None -> [])
     @ (match optStr "pattern" v with
        | Some p -> [ "pattern", pq p ]
        | None -> [])
     @ (match optNum "minLength" v with
        | Some n -> [ "minLength", numLit n ]
        | None -> [])
     @ (match optNum "maxLength" v with
        | Some n -> [ "maxLength", numLit n ]
        | None -> [])
     @ (match JsonValue.tryField "compare" v with
        | Some c ->
          [ "compare",
            "t.CompareRule("
            + pyBinding Opq.Scalar (fieldReq "against" c)
            + ", "
            + pq (strOf "op" c)
            + ")" ]
        | None -> [])
     @ (match JsonValue.tryField "message" v with
        | Some m -> [ "message", pyTextSource m ]
        | None -> []))

let private pyFormField (v: JsonValue) : string =
  let id = strOf "id" v

  pyCall
    "t.FormField"
    [ pq id
      pyTextSource (fieldReq "label" v)
      pyFieldKind (AutoBind.Form id) (fieldReq "kind" v) ]
    ([ "required", pyBool (boolOf "required" v) ]
     @ (match JsonValue.tryField "help" v with
        | Some h -> [ "help", pyTextSource h ]
        | None -> [])
     @ (match JsonValue.tryField "rule" v with
        | Some r -> [ "rule", pyFieldRule r ]
        | None -> []))

let private pyFilterSpec (v: JsonValue) : string =
  let name = strOf "name" v

  "t.FilterSpec("
  + pq name
  + ", "
  + pyTextSource (fieldReq "label" v)
  + ", "
  + pyFilterKind (AutoBind.Filter name) (fieldReq "kind" v)
  + ")"

let private pyColumnWidth (v: JsonValue) : string =
  match dollarType v with
  | Some "Fixed" -> "t.ColumnWidth(" + pq "Fixed" + ")"
  | Some "Flex" -> "t.ColumnWidth(" + pq "Flex" + ")"
  | _ -> "t.ColumnWidth()"

/// A column's cell kind. Every kind but one carries a `(row) -> …` closure that
/// erases to `"<closure>"`, so the bare discriminator is the whole of it; Phase
/// 750's toned pill holds no closure at all and therefore survives the wire with
/// its `field` / `map` / `default` intact, and `fuaran_py` models it as its own
/// record. `default` is omitted at `Default`, so an absent wire field
/// reconstructs as the record's own default rather than being spelled out.
let private pyColumnKind (v: JsonValue) : string =
  match dollarType v |> Option.defaultValue "Text" with
  | "TonedPill" ->
    let mapLit =
      membersOf "map" v
      |> List.map (fun (value, tone) ->
        pq value
        + ": "
        + pq (
          match tone with
          | JString t -> t
          | _ -> ""
        ))
      |> String.concat ", "

    pyCall
      "t.TonedPillColumnKind"
      [ pq (strOf "field" v); "{" + mapLit + "}" ]
      (match optStr "default" v with
       | Some d -> [ "default", pq d ]
       | None -> [])
  | other -> "t.ColumnKind(" + pq other + ")"

/// A declarative sort seed — shared by the grid and by the static table, which
/// carries it inside `staticRows`. Both members are required on the record.
let private pyDefaultSort (v: JsonValue) : string =
  "t.DefaultSort("
  + numLit (numOf "column" v)
  + ", "
  + pq (strOf "direction" v)
  + ")"

/// A chart annotation's x-address (Phase 1581). The Python class names differ
/// from the wire discriminators — `Category` / `Date` lower from
/// `AnnotationCategory` / `AnnotationDate` — so this mapping is the one place
/// that correspondence is spelled, rather than being re-derived per call site.
let private pyAnnotationX (v: JsonValue) : string =
  match dollarType v with
  | Some "Date" -> "t.AnnotationDate(" + pq (strOf "iso" v) + ")"
  | _ -> "t.AnnotationCategory(" + pq (strOf "key" v) + ")"

/// A `RangeBand`'s span: a value interval or a pair of x-addresses.
///
/// Both records name their lower bound `from_`, because `from` is a Python
/// keyword — so the usual snake-casing of the wire key would emit a syntax
/// error, and these two are passed POSITIONALLY to sidestep it entirely.
let private pyAnnotationRange (v: JsonValue) : string =
  match dollarType v with
  | Some "XRange" ->
    "t.XRange("
    + pyAnnotationX (fieldReq "from" v)
    + ", "
    + pyAnnotationX (fieldReq "to" v)
    + ")"
  | _ -> "t.ValueRange(" + pyNum (numOf "from" v) + ", " + pyNum (numOf "to" v) + ")"

/// One chart annotation (§4l, closed at three members). The optional `label` is
/// the second positional on all three and is omitted exactly where the wire
/// omits the key — passing `None` would reach the same bytes, but omitting is
/// what keeps the projection readable as authored source.
let private pyChartAnnotation (v: JsonValue) : string =
  let label =
    match JsonValue.tryField "label" v with
    | Some l -> [ pyTextSource l ]
    | None -> []

  match dollarType v with
  | Some "EventMarker" ->
    "t.EventMarker("
    + String.concat ", " (pyAnnotationX (fieldReq "at" v) :: label)
    + ")"
  | Some "RangeBand" ->
    "t.RangeBand("
    + String.concat ", " (pyAnnotationRange (fieldReq "range" v) :: label)
    + ")"
  | _ -> "t.ReferenceLine(" + String.concat ", " (pyNum (numOf "value" v) :: label) + ")"

let private pyGridColumn (v: JsonValue) : string =
  pyCall
    "t.Column"
    [ pq (strOf "label" v) ]
    ([ "format", pyCellFormat (fieldOrIdentity "format" v)
       "kind", pyColumnKind (fieldReq "kind" v)
       "width", pyColumnWidth (fieldOrIdentity "width" v) ]
     // `field` (declarative) and `value` (closure) are sibling optional slots:
     // naming a `field_name` emits `field` and omits the erased `value`, which
     // is the record's own rule rather than something to arrange here.
     @ (match optStr "field" v with
        | Some f -> [ "fieldName", pq f ]
        | None -> [])
     // Phase 1581 — per-column overrides, modelled from 0.2.0 and tri-state on
     // both sides: `sortable=False` and an omitted `sortable` are two different
     // documents, so neither is emitted where the wire carries no key.
     @ pyTriBool "sortable" "sortable" v
     @ pyTriBool "editable" "editable" v)

/// A `Media` timed-text track (Phase 1110). `default` omits at False; the track
/// record takes its `label` as a `TextSource` and its `src` as a `Binding`.
let private pyMediaTrack (v: JsonValue) : string =
  pyCall
    "t.TrackEntry"
    []
    ([ "kind", pq (strOf "kind" v)
       "label", pyTextSource (fieldReq "label" v)
       "src", pyBinding Opq.Scalar (fieldReq "src" v)
       "srcLang", pq (strOf "srcLang" v) ]
     @ (if boolOf "default" v then [ "default", "True" ] else []))

let private pyTabHeader (v: JsonValue) : string =
  pyCall
    "t.TabHeader"
    [ pyTextSource (fieldReq "label" v) ]
    ((match optStr "icon" v with
      | Some i -> [ "icon", pq i ]
      | None -> [])
     @ (match JsonValue.tryField "disabled" v with
        | Some d -> [ "disabled", pyBinding Opq.Scalar d ]
        | None -> []))

// ── Fragment parameterisation ────────────────────────────────────────────────

let private pyFragScalar (v: JsonValue) : string =
  match dollarType v with
  | Some "Int" -> "t.ScalarInt(" + numLit (numOf "value" v) + ")"
  | Some "Float" -> "t.ScalarFloat(" + numLit (numOf "value" v) + ")"
  | Some "Bool" -> "t.ScalarBool(" + pyBool (boolOf "value" v) + ")"
  | _ -> "t.ScalarStr(" + pq (strOf "value" v) + ")"

let private pyHoleSpace (v: JsonValue) : string =
  match dollarType v with
  | Some "IntRange" -> "t.IntRange(" + numLit (numOf "min" v) + ", " + numLit (numOf "max" v) + ")"
  | Some "FloatRange" -> "t.FloatRange(" + numLit (numOf "min" v) + ", " + numLit (numOf "max" v) + ")"
  | Some "StringLen" ->
    "t.StringLen("
    + numLit (numOf "minLen" v)
    + ", "
    + numLit (numOf "maxLen" v)
    + ")"
  | Some "Enum" -> "t.EnumSpace(" + pyList (arrOf "choices" v |> List.map pyStrItem) + ")"
  | _ -> "t.AnyString()"

let private pyHoleDecl (v: JsonValue) : string =
  match dollarType v with
  | Some "Slot" ->
    pyCall
      "t.SlotHole"
      [ pq (strOf "name" v) ]
      (match optStr "kindConstraint" v with
       | Some c -> [ "kindConstraint", pq c ]
       | None -> [])
  | Some "Repeat" ->
    "t.RepeatHole("
    + pq (strOf "name" v)
    + ", "
    + pyHoleSpace (fieldReq "countSpace" v)
    + ")"
  | _ ->
    pyCall
      "t.ValueHole"
      [ pq (strOf "name" v); pyHoleSpace (fieldReq "space" v) ]
      (match JsonValue.tryField "default" v with
       | Some d -> [ "default", pyFragScalar d ]
       | None -> [])

// ── Drawing (Phase 1596) — the vector vocabulary fuaran-py modelled in 0.3.0 ──
//
// Every slot below is a typed record: `t.ViewBox` / `t.DrawPoint` /
// `t.DrawStyle`, the five curve commands and the nine shapes. Before 0.3.0 the
// kind had no constructor arm and fell to `pyGenericNode`, which re-spells the
// wire's own object shape — `t.Drawing(view_box={'height': 100, …})` — and
// round-trips only because the host's lowering passes a raw dict straight
// through. That is the structural escape this leg's header forbids, and the
// cost is not stylistic: the same projection would have gone on "passing" had
// 0.3.0 modelled `Drawing` and NONE of its nine shapes, so the arm would have
// certified a host gap as conformance. A typed record spells exactly what the
// model carries and nothing more, which is the property the quarantine beside
// this file measures.

/// A `DrawStyle` — every slot optional, so only the keys the wire carries are
/// emitted. `rotation` rides EVEN AT ZERO: an explicitly-upright label and one
/// that never mentioned rotation are two different documents.
let private pyDrawStyle (v: JsonValue) : string =
  let bindingSlot (name: string) =
    match JsonValue.tryField name v with
    | Some b -> [ name, pyBinding Opq.Scalar b ]
    | None -> []

  let strSlot (name: string) =
    match optStr name v with
    | Some s -> [ name, pq s ]
    | None -> []

  let numSlot (name: string) =
    match JsonValue.tryField name v with
    | Some(JNumber n) -> [ name, pyNum n ]
    | _ -> []

  pyCall
    "t.DrawStyle"
    []
    (bindingSlot "fill"
     @ bindingSlot "stroke"
     @ bindingSlot "strokeWidth"
     @ bindingSlot "opacity"
     @ strSlot "textAnchor"
     @ numSlot "fontSize"
     @ strSlot "emphasis"
     @ strSlot "fontFamily"
     @ strSlot "markId"
     @ numSlot "rotation"
     @ (match JsonValue.tryField "tip" v with
        | Some tip -> [ "tip", pyTextSource tip ]
        | None -> []))

/// The `style=` keyword a shape contributes — omitted where the wire's style is
/// `{}`, which is exactly what the record's own empty default writes back.
let private pyDrawStyleKw (v: JsonValue) : (string * string) list =
  match JsonValue.tryField "style" v with
  | Some(JObject(_ :: _) as st) -> [ "style", pyDrawStyle st ]
  | _ -> []

let private pyDrawPoint (v: JsonValue) : string =
  "t.DrawPoint(" + pyFloatOf "x" v + ", " + pyFloatOf "y" v + ")"

let private pyCurveCommand (v: JsonValue) : string =
  match dollarType v with
  | Some "MoveTo" -> "t.MoveTo(" + pyDrawPoint (fieldReq "to" v) + ")"
  | Some "LineTo" -> "t.LineTo(" + pyDrawPoint (fieldReq "to" v) + ")"
  | Some "CubicTo" ->
    "t.CubicTo("
    + pyDrawPoint (fieldReq "control1" v)
    + ", "
    + pyDrawPoint (fieldReq "control2" v)
    + ", "
    + pyDrawPoint (fieldReq "to" v)
    + ")"
  | Some "QuadraticTo" ->
    "t.QuadraticTo("
    + pyDrawPoint (fieldReq "control" v)
    + ", "
    + pyDrawPoint (fieldReq "to" v)
    + ")"
  | _ -> "t.Close()"

/// The nine shapes. Coordinates are positional in the record's own order;
/// `Group` is the one case that recurses, over shapes rather than over nodes.
let rec private pyShape (v: JsonValue) : string =
  let style = pyDrawStyleKw v

  match dollarType v with
  | Some "Group" -> pyCall "t.Group" [ pyList (arrOf "children" v |> List.map pyShape) ] style
  | Some "Rectangle" ->
    pyCall
      "t.Rectangle"
      [ pyFloatOf "x" v; pyFloatOf "y" v; pyFloatOf "width" v; pyFloatOf "height" v ]
      ((match JsonValue.tryField "cornerRadius" v with
        | Some(JNumber n) -> [ "cornerRadius", pyNum n ]
        | _ -> [])
       @ style)
  | Some "Line" -> pyCall "t.Line" [ pyFloatOf "x1" v; pyFloatOf "y1" v; pyFloatOf "x2" v; pyFloatOf "y2" v ] style
  | Some "Polyline" -> pyCall "t.Polyline" [ pyList (arrOf "points" v |> List.map pyDrawPoint) ] style
  | Some "Polygon" -> pyCall "t.Polygon" [ pyList (arrOf "points" v |> List.map pyDrawPoint) ] style
  | Some "Curve" -> pyCall "t.Curve" [ pyList (arrOf "commands" v |> List.map pyCurveCommand) ] style
  | Some "Circle" -> pyCall "t.Circle" [ pyFloatOf "cx" v; pyFloatOf "cy" v; pyFloatOf "r" v ] style
  | Some "Ellipse" ->
    pyCall "t.Ellipse" [ pyFloatOf "cx" v; pyFloatOf "cy" v; pyFloatOf "rx" v; pyFloatOf "ry" v ] style
  // `Label.text` is a raw `TextSource` rather than the coerced `TextInput`, so
  // the bare wire string takes the explicit `t.LiteralText` record.
  | _ -> pyCall "t.Label" [ pyFloatOf "x" v; pyFloatOf "y" v; pyTextSource (fieldReq "text" v) ] style

let private pyViewBox (v: JsonValue) : string =
  "t.ViewBox("
  + pyFloatOf "minX" v
  + ", "
  + pyFloatOf "minY" v
  + ", "
  + pyFloatOf "width" v
  + ", "
  + pyFloatOf "height" v
  + ")"

/// A `Mount`'s guest channel (Phase 1596) — `message_shape` rides only on the
/// two-way form the wire spells it on.
let private pyGuestChannel (v: JsonValue) : string =
  pyCall
    "t.GuestChannel"
    [ pq (strOf "direction" v) ]
    (match optStr "messageShape" v with
     | Some m -> [ "messageShape", pq m ]
     | None -> [])

// ── Base traits ──────────────────────────────────────────────────────────────

let private pyStyleLit (v: JsonValue) : string =
  pyCall
    "t.SemanticStyle"
    []
    ([ "emphasis", pq (optStr "emphasis" v |> Option.defaultValue "Normal")
       "tone", pq (optStr "tone" v |> Option.defaultValue "Default")
       "weight", pq (optStr "weight" v |> Option.defaultValue "Standard") ]
     @ (match optStr "role" v with
        | Some r -> [ "role", pq r ]
        | None -> [])
     @ (match optStr "voice" v with
        | Some vo -> [ "voice", pq vo ]
        | None -> [])
     // Phase 1533 — the writing-direction slot; `auto` is the default and the
     // record omits it there, so an absent wire field must project as absent.
     @ (match optStr "direction" v with
        | Some d -> [ "direction", pq d ]
        | None -> []))

let private pyAccessibilityLit (v: JsonValue) : string =
  pyCall
    "t.Accessibility"
    []
    ((match JsonValue.tryField "label" v with
      | Some b -> [ "label", pyBinding Opq.Scalar b ]
      | None -> [])
     @ (match optStr "labelledBy" v with
        | Some s -> [ "labelledBy", pq s ]
        | None -> [])
     @ (match optStr "describedBy" v with
        | Some s -> [ "describedBy", pq s ]
        | None -> [])
     @ (match optStr "role" v with
        | Some s -> [ "role", pq s ]
        | None -> [])
     @ (match optStr "liveRegion" v with
        | Some s -> [ "liveRegion", pq s ]
        | None -> [])
     @ (match JsonValue.tryField "hidden" v with
        | Some b -> [ "hidden", pyBinding Opq.Scalar b ]
        | None -> []))

// ── The per-kind node emitter ────────────────────────────────────────────────

/// `UiNode.visible` (fuaran#1535, modelled by fuaran-py from 0.1.0) — a
/// `Binding[bool]` whose resolved False removes the node entirely.
///
/// `UiNode.to_wire` copies this slot into the extras WITHOUT lowering it, so the
/// typed binding is handed over already lowered by its own `to_wire`. That is
/// deliberately not the same thing as writing the wire out by hand: it can spell
/// exactly what the typed model carries and nothing else, so a `Binding` case
/// `fuaran_py` does not model still cannot be projected here — see the leg's
/// header note.
let private pyVisible (v: JsonValue) : string = pyBinding Opq.Scalar v + ".to_wire()"

/// The ARIA trait each `fuaran.*` constructor injects, as the wire (key, value)
/// pairs the emitter must confirm or override to reach the wire's exact value.
/// Deliberately NOT the TypeScript leg's `ctorAccessibility` table: two of the
/// Python constructors' defaults differ — `scroll_area` carries `role=region`
/// where the TS `scrollArea` carries none, and the static-rows `table`
/// constructor carries none where `grid` carries `region` — so sharing one table
/// was wrong in both directions, which is how `scroll-1` failed.
let private pyCtorA11y (kindType: string) (k: JsonValue) : (string * string) list =
  match kindType with
  | "ScrollArea" -> [ "role", "region" ]
  | "DataGrid" when (JsonValue.tryField "staticRows" k).IsSome -> []
  | _ -> ctorAccessibility kindType

let rec private pyNodeExpr (depth: int) (nodeV: JsonValue) : string =
  markNode nodeV (pyNodeExprRaw depth nodeV)

and private pyNodeExprRaw (depth: int) (nodeV: JsonValue) : string =
  let id = optStr "id" nodeV |> Option.defaultValue ""
  let kindObj = fieldReq "kind" nodeV
  let kindType = dollarType kindObj |> Option.defaultValue ""

  if kindType = "Box" then
    pyBoxNode depth id kindObj nodeV
  else
    let built =
      match pyKindCtor depth kindType id kindObj with
      | Some ctorExpr -> ctorExpr
      | None -> pyGenericNode depth id kindObj

    // The traits the constructors do not take: `style` and `state` are absent
    // from every `fuaran.*` signature, and the injected ARIA default has to be
    // pinned back to the wire's exact value. `UiNode.replace` is the Python
    // spelling of the TS leg's object spread.
    let overrides =
      (match JsonValue.tryField "style" nodeV with
       | Some st -> [ "style", pyStyleLit st ]
       | None -> [])
      @ (match JsonValue.tryField "state" nodeV with
         | Some st -> [ "state", pyStateLit (depth + 1) st ]
         | None -> [])
      @ (let wireA = JsonValue.tryField "accessibility" nodeV

         if a11yMatchesCtor wireA (pyCtorA11y kindType kindObj) then
           []
         else
           [ "accessibility",
             (match wireA with
              | Some a -> pyAccessibilityLit a
              | None -> "None") ])
      @ (match JsonValue.tryField "visible" nodeV with
         | Some vis -> [ "visible", pyVisible vis ]
         | None -> [])
      // Phase 1581 — `UiNode` grew `tooltip` as a sixth member in 0.2.0. It is a
      // `TextSource`, so a bare wire string is the `LiteralText` canonical form
      // and a bound hint is `t.Bound(...)`; no `fuaran.*` constructor takes it,
      // which is why it rides the same `replace` seam `visible` does.
      @ (match JsonValue.tryField "tooltip" nodeV with
         | Some tip -> [ "tooltip", pyTextSource tip ]
         | None -> [])

    if List.isEmpty overrides then
      built
    else
      "("
      + built
      + ").replace("
      + (overrides |> List.map (fun (k, v) -> toSnake k + "=" + v) |> String.concat ", ")
      + ")"

and private pyStateLit (depth: int) (v: JsonValue) : string =
  pyCall
    "t.StateBehaviour"
    []
    ((match JsonValue.tryField "onLoading" v with
      | Some n -> [ "onLoading", pyNodeExpr (depth + 1) n ]
      | None -> [])
     @ (match JsonValue.tryField "onEmpty" v with
        | Some n -> [ "onEmpty", pyNodeExpr (depth + 1) n ]
        | None -> [])
     @ (match JsonValue.tryField "onError" v with
        | Some _ -> [ "onError", "True" ]
        | None -> []))

and private pyChildren (depth: int) (k: JsonValue) : string =
  let children = arrOf "children" k

  if List.isEmpty children then
    "[]"
  else
    "[\n"
    + (children
       |> List.map (fun c -> pad (depth + 2) + pyNodeExpr (depth + 2) c)
       |> String.concat ",\n")
    + ",\n"
    + pad (depth + 1)
    + "]"

/// A `Box` — always the typed record rather than a role-dispatched constructor
/// (the TS leg's choice, for its reason): `t.Box` carries `heading` alongside a
/// `Dashboard` role, which `fuaran.dashboard` cannot, and building the node
/// directly means no constructor ARIA default has to be unpicked.
and private pyBoxNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  let layoutV = fieldReq "layout" k

  let gapKw =
    match optNum "gap" layoutV with
    | Some g -> [ "gap", numLit g ]
    | None -> []

  let layout =
    match dollarType layoutV with
    | Some "Flex" ->
      pyCall
        "t.FlexLayout"
        []
        ([ "direction", pq (strOf "direction" layoutV)
           "wrap", pyBool (boolOf "wrap" layoutV) ]
         @ gapKw)
    | Some "Grid" ->
      pyCall
        "t.GridTemplate"
        []
        ([ "cols", numLit (numOf "cols" layoutV) ]
         @ gapKw
         @ (match optStr "templateColumns" layoutV with
            | Some tc -> [ "templateColumns", pq tc ]
            | None -> []))
    | Some "Masonry" -> pyCall "t.MasonryLayout" [] ([ "cols", numLit (numOf "cols" layoutV) ] @ gapKw)
    | _ -> "t.AutoLayout()"

  let boxKw =
    [ "children", pyChildren depth k
      "layout", layout
      "role", pq (strOf "role" k) ]
    @ (match JsonValue.tryField "heading" k with
       | Some h -> [ "heading", pyTextSource h ]
       | None -> [])
    // Phase 1473 — the print-pagination hints, both omitted-when-false.
    @ (if boolOf "keepTogether" k then
         [ "keep_together", "True" ]
       else
         [])
    @ (if boolOf "breakBefore" k then
         [ "break_before", "True" ]
       else
         [])

  pyCallBlock "t.UiNode" [] ([ "id", pq id; "kind", pyCall "t.Box" [] boxKw ] @ pyBaseTraits depth nodeV) depth

/// The base traits for a node built as a typed record rather than through a
/// constructor: an absent trait projects as absent, which the encoder omits.
and private pyBaseTraits (depth: int) (nodeV: JsonValue) : (string * string) list =
  (match JsonValue.tryField "accessibility" nodeV with
   | Some a -> [ "accessibility", pyAccessibilityLit a ]
   | None -> [])
  @ (match JsonValue.tryField "style" nodeV with
     | Some st -> [ "style", pyStyleLit st ]
     | None -> [])
  @ (match JsonValue.tryField "state" nodeV with
     | Some st -> [ "state", pyStateLit (depth + 1) st ]
     | None -> [])
  @ (match JsonValue.tryField "visible" nodeV with
     | Some vis -> [ "visible", pyVisible vis ]
     | None -> [])
  @ (match JsonValue.tryField "tooltip" nodeV with
     | Some tip -> [ "tooltip", pyTextSource tip ]
     | None -> [])

/// The fallback for a kind with no constructor arm: the typed record named by
/// the wire discriminator, with each field snake-cased. This replaces the
/// generic walker's `$type`-keeping object literal — it is the shape the kind
/// WOULD take, so a kind `fuaran_py` does not model fails by name (an
/// `AttributeError` naming the absent class) rather than passing as a sketch.
and private pyGenericNode (depth: int) (id: string) (k: JsonValue) : string =
  let kindType = dollarType k |> Option.defaultValue "Node"

  let fields =
    match k with
    | JObject members ->
      members
      |> List.filter (fun (kk, _) -> kk <> "$type")
      |> List.map (fun (kk, vv) -> kk, pyGenericValue (depth + 1) vv)
    | _ -> []

  pyCall "t.UiNode" [] [ "id", pq id; "kind", pyCall ("t." + kindType) [] fields ]

and private pyGenericValue (depth: int) (v: JsonValue) : string =
  match v with
  | JNull -> "None"
  | JBool b -> pyBool b
  | JNumber n -> pyNum n
  | JString s -> pq s
  | JArray xs -> pyList (xs |> List.map (pyGenericValue depth))
  | JObject _ when isNode v -> pyNodeExpr depth v
  | JObject members ->
    match dollarType v with
    | Some ty ->
      pyCall
        ("t." + ty)
        []
        (members
         |> List.filter (fun (kk, _) -> kk <> "$type")
         |> List.map (fun (kk, vv) -> kk, pyGenericValue depth vv))
    | None ->
      if List.isEmpty members then
        "{}"
      else
        "{"
        + (members
           |> List.map (fun (kk, vv) -> pq kk + ": " + pyGenericValue depth vv)
           |> String.concat ", ")
        + "}"

and private pyFragArg (depth: int) (v: JsonValue) : string =
  match dollarType v with
  | Some "SlotArg" -> "t.SlotArg(" + pyNodeExpr (depth + 1) (fieldReq "tree" v) + ")"
  | _ -> pyFragScalar v

and private pyKindCtor (depth: int) (kindType: string) (id: string) (k: JsonValue) : string option =
  let call (ctor: string) (positional: string list) (kw: (string * string) list) =
    Some(pyCallBlock ("fuaran." + ctor) (pq id :: positional) kw depth)

  match kindType with
  // ── Layout ────────────────────────────────────────────────────────────────
  | "SplitPanel" -> call "split_panel" [] [ "weight", numLit (numOf "weight" k); "children", pyChildren depth k ]
  | "Tabs" ->
    call
      "tabs"
      []
      // Omitted-when-absent — the same encoder omit-at-default the TS arm
      // documents; a defaulted `activeIndex` here re-encodes as an explicit
      // `Static` the canonical wire does not carry.
      ((match fieldOpt "activeIndex" k with
        | Some ai -> [ "activeIndex", pyBinding Opq.Scalar ai ]
        | None -> [])
       @ (match optStr "orientation" k with
          | Some o -> [ "orientation", pq o ]
          | None -> [])
       @ (match JsonValue.tryField "tabHeaders" k with
          | Some(JArray hs) -> [ "tabHeaders", pyList (hs |> List.map pyTabHeader) ]
          | _ -> [])
       @ (match JsonValue.tryField "tabTags" k with
          | Some(JArray ts) -> [ "tabTags", pyList (ts |> List.map pyStrItem) ]
          | _ -> [])
       @ (match JsonValue.tryField "activeTag" k with
          | Some tg -> [ "activeTag", pyBinding Opq.Scalar tg ]
          | None -> [])
       @ pyHandler "onSelect" "on_select" k
       @ pyHandler "onSelectTag" "on_select_tag" k
       @ [ "children", pyChildren depth k ])
  | "Stepper" ->
    call
      "stepper"
      []
      ([ "activeStep", pyBinding Opq.Scalar (fieldReq "activeStep" k) ]
       @ pyHandler "onSelect" "on_select" k
       @ [ "children", pyChildren depth k ])
  | "SummaryList" ->
    call
      "summary_list"
      []
      ((match JsonValue.tryField "heading" k with
        | Some h -> [ "heading", pyTextInput h ]
        | None -> [])
       @ [ "children", pyChildren depth k ])
  | "Disclosure" ->
    call
      "disclosure"
      []
      ([ "heading", pyTextInput (fieldReq "heading" k)
         "open", pyBinding Opq.Scalar (fieldReq "open" k)
         "defaultOpen", pyBool (boolOf "defaultOpen" k) ]
       @ pyHandler "onToggle" "on_toggle" k
       @ [ "children", pyChildren depth k ])
  | "Modal" ->
    call
      "modal"
      []
      ((match JsonValue.tryField "heading" k with
        | Some h -> [ "heading", pyTextInput h ]
        | None -> [])
       @ [ "open", pyBinding Opq.Scalar (fieldReq "open" k)
           "dismissable", pyBool (boolOf "dismissable" k) ]
       // `on_dismiss` defaults to the no-op `Chain`, which the encoder writes
       // as a real `onDismiss` key — so a wire that omits the slot has to say
       // `None` explicitly. This is the one handler in the vocabulary that is an
       // ACTION rather than a bool flag, so it is the one place absence is
       // declared by an argument instead of by `pyHandler`.
       @ [ "onDismiss",
           (match JsonValue.tryField "onDismiss" k with
            | Some d -> pyAction d
            | None -> "None") ]
       // Phase 1119 — the anchored popover form. `Blocking` is the identity and
       // omits at it; `anchor` names the node the surface is positioned against.
       @ (match optStr "modality" k with
          | Some m -> [ "modality", pq m ]
          | None -> [])
       @ (match optStr "anchor" k with
          | Some a -> [ "anchor", pq a ]
          | None -> [])
       @ [ "children", pyChildren depth k ])
  | "ScrollArea" ->
    call
      "scroll_area"
      []
      ([ "orientation", pq (strOf "orientation" k) ]
       @ (match optNum "maxHeight" k with
          | Some m -> [ "maxHeight", numLit m ]
          | None -> [])
       @ (match optNum "maxWidth" k with
          | Some m -> [ "maxWidth", numLit m ]
          | None -> [])
       @ [ "children", pyChildren depth k ])
  // ── Display ───────────────────────────────────────────────────────────────
  | "Heading" ->
    call
      "heading"
      [ pyTextInput (fieldReq "text" k) ]
      [ "level", numLit (numOf "level" k); "variant", pq (strOf "variant" k) ]
  | "Markdown" -> call "markdown" [ pyTextInput (fieldReq "text" k) ] []
  | "Metric" ->
    call
      "metric"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "value", pyBinding Opq.Scalar (valueOrSource k)
         "format", pyCellFormat (fieldOrIdentity "format" k) ]
       @ (match optStr "tone" k with
          | Some tn -> [ "tone", pq tn ]
          | None -> [])
       @ (match optStr "weight" k with
          | Some w -> [ "weight", pq w ]
          | None -> [])
       @ (match optStr "emphasis" k with
          | Some e -> [ "emphasis", pq e ]
          | None -> [])
       @ (match JsonValue.tryField "trend" k with
          | Some tr -> [ "trend", pyBinding Opq.Scalar tr ]
          | None -> [])
       @ (match JsonValue.tryField "trendFormat" k with
          | Some tf -> [ "trendFormat", pyCellFormat tf ]
          | None -> [])
       @ (match optStr "trendPolarity" k with
          | Some p -> [ "trendPolarity", pq p ]
          | None -> [])
       @ (match optStr "icon" k with
          | Some i -> [ "icon", pq i ]
          | None -> [])
       @ (match JsonValue.tryField "subtext" k with
          | Some s -> [ "subtext", pyTextInput s ]
          | None -> []))
  | "Badge" -> call "badge" [] [ "label", pyTextInput (fieldReq "label" k); "variant", pq (strOf "variant" k) ]
  | "Sparkline" -> call "sparkline" [] [ "source", pyBinding Opq.Collection (fieldReq "source" k) ]
  | "Skeleton" -> call "skeleton" [ numLit (numOf "rows" k) ] []
  | "Callout" ->
    call
      "callout"
      []
      ([ "body", pyTextInput (fieldReq "body" k)
         "tone", pq (strOf "tone" k)
         "dismissable", pyBool (boolOf "dismissable" k) ]
       @ (match JsonValue.tryField "heading" k with
          | Some h -> [ "heading", pyTextInput h ]
          | None -> [])
       @ (match optStr "icon" k with
          | Some i -> [ "icon", pq i ]
          | None -> []))
  | "Progress" ->
    call
      "progress"
      []
      ([ "fraction", pyBinding Opq.Scalar (fieldReq "fraction" k)
         "indeterminate", pyBool (boolOf "indeterminate" k)
         "tone", pq (strOf "tone" k) ]
       @ (match JsonValue.tryField "label" k with
          | Some l -> [ "label", pyTextInput l ]
          | None -> [])
       @ (match JsonValue.tryField "caveat" k with
          | Some c -> [ "caveat", pyTextInput c ]
          | None -> []))
  | "LabelValueRow" ->
    call
      "label_value_row"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "value", pyBinding Opq.Scalar (valueOrSource k)
         "format", pyCellFormat (fieldOrIdentity "format" k)
         "emphasis", pyBool (boolOf "emphasis" k) ]
       @ (match JsonValue.tryField "help" k with
          | Some h -> [ "help", pyTextInput h ]
          | None -> []))
  | "Link" ->
    call
      "link"
      []
      ([ "href", pyBinding Opq.Scalar (fieldReq "href" k)
         "label", pyTextInput (fieldReq "label" k)
         "download", pyBool (boolOf "download" k) ]
       @ (match optStr "rel" k with
          | Some r -> [ "rel", pq r ]
          | None -> [])
       @ (match optStr "target" k with
          | Some tg -> [ "target", pq tg ]
          | None -> [])
       // Phase 1581 — the mailto/tel obfuscation hint, modelled from 0.2.0.
       @ (match optStr "protection" k with
          | Some pr -> [ "protection", pq pr ]
          | None -> []))
  | "Image" ->
    call
      "image"
      []
      ([ "src", pyBinding Opq.Scalar (fieldReq "src" k)
         "alt", pyTextInput (fieldReq "alt" k)
         "variant", pq (strOf "variant" k) ]
       @ (match optStr "fit" k with
          | Some f -> [ "fit", pq f ]
          | None -> [])
       @ (match optStr "aspectRatio" k with
          | Some a -> [ "aspectRatio", pq a ]
          | None -> [])
       @ (match optStr "loading" k with
          | Some l -> [ "loading", pq l ]
          | None -> [])
       @ (match JsonValue.tryField "caption" k with
          | Some c -> [ "caption", pyTextInput c ]
          | None -> [])
       @ (match JsonValue.tryField "expandable" k with
          | Some(JBool b) -> [ "expandable", pyBool b ]
          | _ -> [])
       @ (match JsonValue.tryField "srcSet" k with
          | Some(JArray entries) ->
            [ "srcSet",
              pyList (
                entries
                |> List.map (fun e ->
                  "("
                  + pyBinding Opq.Scalar (fieldReq "src" e)
                  + ", "
                  + numLit (numOf "width" e)
                  + ")")
              ) ]
          | _ -> []))
  | "Icon" ->
    call
      "icon"
      [ pq (strOf "icon" k) ]
      ([ "size", pq (optStr "size" k |> Option.defaultValue "Medium")
         "tone", pq (optStr "tone" k |> Option.defaultValue "Default") ]
       @ (match optStr "label" k with
          | Some l -> [ "label", pq l ]
          | None -> []))
  | "Media" ->
    let inner = fieldReq "kind" k
    let isVideo = (dollarType inner |> Option.defaultValue "Video") = "Video"

    let common =
      [ "src", pyBinding Opq.Scalar (fieldReq "src" k)
        "label", pyTextInput (fieldReq "label" k) ]
      @ (match JsonValue.tryField "controls" k with
         | Some(JBool b) -> [ "controls", pyBool b ]
         | _ -> [])
      @ (match JsonValue.tryField "loop" k with
         | Some(JBool b) -> [ "loop", pyBool b ]
         | _ -> [])
      // Phase 1110/1114 — the timed-text tracks and the transcript fallback.
      // `tracks` omits at the empty list, `transcript` when absent.
      @ (match JsonValue.tryField "tracks" k with
         | Some(JArray ts) when not (List.isEmpty ts) -> [ "tracks", pyList (ts |> List.map pyMediaTrack) ]
         | _ -> [])
      @ (match JsonValue.tryField "transcript" k with
         | Some t -> [ "transcript", pyTextInput t ]
         | None -> [])

    if isVideo then
      call
        "video"
        []
        (common
         @ (match JsonValue.tryField "autoplay" inner with
            | Some(JBool b) -> [ "autoplay", pyBool b ]
            | _ -> [])
         @ (match JsonValue.tryField "poster" inner with
            | Some p -> [ "poster", pyBinding Opq.Scalar p ]
            | None -> []))
    else
      call "audio" [] common
  | "List" ->
    call
      "list"
      []
      [ "items", pyList (arrOf "items" k |> List.map pyTextInput)
        "ordered", pyBool (boolOf "ordered" k) ]
  | "Toast" ->
    call
      "toast"
      []
      [ "message", pyTextInput (fieldReq "message" k)
        "tone", pq (strOf "tone" k)
        "open", pyBinding Opq.Scalar (fieldReq "open" k)
        "dismissable",
        (match JsonValue.tryField "dismissable" k with
         | Some(JBool b) -> pyBool b
         | _ -> "True") ]
  | "CodeBlock" ->
    call
      "code_block"
      []
      [ "code", pq (strOf "code" k)
        "language", pq (strOf "language" k)
        "lineNumbers", pyBool (boolOf "lineNumbers" k)
        "highlightLines", pyList (arrOf "highlightLines" k |> List.map numItem)
        "copyable", pyBool (boolOf "copyable" k) ]
  | "Math" -> call "math" [ pq (strOf "source" k) ] [ "display", pq (strOf "display" k) ]
  // Phase 1596 — `Fact`, modelled by fuaran-py from 0.3.0. `tone` and
  // `emphasis` carry record defaults the wire omits at, so each is emitted only
  // where the wire states it rather than restated from the default.
  | "Fact" ->
    call
      "fact"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "value", pyTextInput (fieldReq "value" k) ]
       @ (match optStr "tone" k with
          | Some tone -> [ "tone", pq tone ]
          | None -> [])
       @ (match JsonValue.tryField "emphasis" k with
          | Some(JBool b) -> [ "emphasis", pyBool b ]
          | _ -> [])
       @ (match JsonValue.tryField "help" k with
          | Some h -> [ "help", pyTextInput h ]
          | None -> [])
       @ (match optStr "icon" k with
          | Some i -> [ "icon", pq i ]
          | None -> []))
  // ── Input ─────────────────────────────────────────────────────────────────
  | "Button" ->
    call
      "button"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "onClick", pyAction (fieldReq "onClick" k)
         "variant", pq (strOf "variant" k) ]
       @ (match optStr "icon" k with
          | Some i -> [ "icon", pq i ]
          | None -> [])
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", pyBinding Opq.Scalar d ]
          | None -> []))
  | "Select" ->
    call
      "select"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "source", pyOptionsBinding (fieldReq "source" k)
         "value", pyBinding Opq.Scalar (fieldReq "value" k) ]
       @ (match JsonValue.tryField "placeholder" k with
          | Some p -> [ "placeholder", pyTextInput p ]
          | None -> [])
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", pyBinding Opq.Scalar d ]
          | None -> [])
       @ (if boolOf "multiple" k then [ "multiple", "True" ] else [])
       @ (match JsonValue.tryField "values" k with
          | Some vs -> [ "values", pyBinding Opq.Collection vs ]
          | None -> [])
       @ pyHandler "onChange" "on_change" k
       @ pyHandler "onChangeMulti" "on_change_multi" k)
  | "Form" ->
    let fs = arrOf "fields" k

    let fieldsArr =
      if List.isEmpty fs then
        "[]"
      else
        "[\n"
        + (fs |> List.map (fun f -> pad (depth + 2) + pyFormField f) |> String.concat ",\n")
        + ",\n"
        + pad (depth + 1)
        + "]"

    call
      "form"
      []
      ([ "fields", fieldsArr
         "onSubmit", pyAction (fieldReq "onSubmit" k)
         "submitLabel", pyTextInput (fieldReq "submitLabel" k) ]
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", pyBinding Opq.Scalar d ]
          | None -> []))
  | "Filters" ->
    let items = arrOf "items" k

    let specsArr =
      if List.isEmpty items then
        "[]"
      else
        "[\n"
        + (items
           |> List.map (fun s -> pad (depth + 2) + pyFilterSpec s)
           |> String.concat ",\n")
        + ",\n"
        + pad (depth + 1)
        + "]"

    call "filters" [] [ "items", specsArr ]
  | "FileUpload" ->
    call
      "file_upload"
      []
      ([ "label", pyTextInput (fieldReq "label" k)
         "accept", pyList (arrOf "accept" k |> List.map pyStrItem)
         "multiple", pyBool (boolOf "multiple" k) ]
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", pyBinding Opq.Scalar d ]
          | None -> [])
       // Phase 1123 — the intake affordances. `dropTarget` / `acceptPaste` omit
       // at False; `capture` names a device and `destination` a server-side
       // bucket, both optional and omitted when absent.
       @ (if boolOf "dropTarget" k then
            [ "dropTarget", "True" ]
          else
            [])
       @ (if boolOf "acceptPaste" k then
            [ "acceptPaste", "True" ]
          else
            [])
       @ (match optStr "capture" k with
          | Some c -> [ "capture", pq c ]
          | None -> [])
       @ (match optStr "destination" k with
          | Some d -> [ "destination", pq d ]
          | None -> [])
       @ pyHandler "onSelect" "on_select" k)
  // ── Visualisation ─────────────────────────────────────────────────────────
  | "Chart" ->
    call
      "chart"
      []
      ([ "source", pyBinding Opq.Collection (fieldReq "source" k)
         "xField", pq (strOf "xField" k)
         "yFields", pyList (arrOf "yFields" k |> List.map pyStrItem)
         "kind", pq (strOf "kind" k)
         "stacked", pyBool (boolOf "stacked" k) ]
       @ (match JsonValue.tryField "title" k with
          | Some ti -> [ "title", pyTextInput ti ]
          | None -> [])
       // Phase 1581 — the eight slots fuaran-py 0.2.0 added to `Chart`. Every
       // one is optional on both sides, so each is emitted exactly where the
       // wire carries it. `valueFormat` takes the `Format` union (`pyFormatIntent`
       // — `t.FmtCurrency('GBP')`, emitting `isoCode`), NOT the `CellFormat` the
       // column vocabulary uses, whose currency case emits `code` instead: the
       // two differ by one key and by nothing else visible.
       @ (match JsonValue.tryField "subtitle" k with
          | Some st -> [ "subtitle", pyTextInput st ]
          | None -> [])
       @ (match JsonValue.tryField "xTitle" k with
          | Some x -> [ "xTitle", pyTextInput x ]
          | None -> [])
       @ (match JsonValue.tryField "yTitle" k with
          | Some y -> [ "yTitle", pyTextInput y ]
          | None -> [])
       @ (match JsonValue.tryField "valueFormat" k with
          | Some f -> [ "valueFormat", pyFormatIntent f ]
          | None -> [])
       @ (match optStr "legendPosition" k with
          | Some l -> [ "legendPosition", pq l ]
          | None -> [])
       @ (match optStr "dataLabels" k with
          | Some d -> [ "dataLabels", pq d ]
          | None -> [])
       @ (match optStr "xScale" k with
          | Some x -> [ "xScale", pq x ]
          | None -> [])
       @ (match JsonValue.tryField "annotations" k with
          | Some(JArray ans) -> [ "annotations", pyList (ans |> List.map pyChartAnnotation) ]
          | _ -> []))
  | "Table" ->
    let rows =
      arrOf "rows" k
      |> List.map (fun row ->
        match row with
        | JArray cells -> pyList (cells |> List.map pyTextInput)
        | _ -> "[]")

    call
      "table"
      []
      [ "headers", pyList (arrOf "headers" k |> List.map pyTextInput)
        "rows", pyList rows ]
  | "Map" ->
    call
      "map"
      []
      [ "source", pyMarkerBinding (fieldReq "source" k)
        "centreLatitude", numLit (numOf "centreLatitude" k)
        "centreLongitude", numLit (numOf "centreLongitude" k)
        "zoom", numLit (numOf "zoom" k) ]
  | "DataGrid" ->
    // A grid carrying `staticRows` is what `fuaran.table` authors — Phase 393
    // folded the standalone `Table` kind into this mode, and the Python
    // constructor lowers back into it, so the static form has to route there
    // rather than through `grid` (which cannot carry rows at all).
    (match JsonValue.tryField "staticRows" k with
     | Some sr ->
       let rows =
         arrOf "rows" sr
         |> List.map (fun row ->
           match row with
           | JArray cells -> pyList (cells |> List.map pyTextSource)
           | _ -> "[]")

       call
         "table"
         []
         ([ "headers", pyList (arrOf "headers" sr |> List.map pyTextSource)
            "rows", pyList rows ]
          // Phase 1581 — both land INSIDE `staticRows`, and `sortable` is
          // tri-state there: `false` and absent are different documents.
          @ pyTriBool "sortable" "sortable" sr
          @ (match JsonValue.tryField "defaultSort" sr with
             | Some ds -> [ "defaultSort", pyDefaultSort ds ]
             | None -> []))
     | None ->
       call
         "grid"
         []
         ([ "source", pyBinding Opq.Collection (fieldReq "source" k)
            "columns", pyList (arrOf "columns" k |> List.map pyGridColumn)
            "editable", pyBool (boolOf "editable" k) ]
          // `rowKey` (closure) and `rowKeyField` (declarative) mirror the column
          // pair above: naming the field emits `rowKeyField` and omits the
          // erased `rowKey`.
          @ (match optStr "rowKeyField" k with
             | Some f -> [ "rowKeyField", pq f ]
             | None -> [])
          // Phase 1581 — the declarative sort / page / edit slots and the five
          // transfer / export / print flags. The record carried the last five
          // before 0.2.0; the CONSTRUCTOR did not reach them, which is why three
          // grid ids were quarantined against the host for a `Binding.Query`
          // source while ALSO failing on a slot fixable here.
          @ (match optStr "sortStateKey" k with
             | Some sk -> [ "sortStateKey", pq sk ]
             | None -> [])
          @ (match JsonValue.tryField "defaultSort" k with
             | Some ds -> [ "defaultSort", pyDefaultSort ds ]
             | None -> [])
          @ (match optNum "pageSize" k with
             | Some n -> [ "pageSize", numLit n ]
             | None -> [])
          @ (match optStr "pageStateKey" k with
             | Some pk -> [ "pageStateKey", pq pk ]
             | None -> [])
          @ (match optStr "editStateKey" k with
             | Some ek -> [ "editStateKey", pq ek ]
             | None -> [])
          @ (if boolOf "reorderable" k then
               [ "reorderable", "True" ]
             else
               [])
          @ (match optStr "transferOutKey" k with
             | Some tk -> [ "transferOutKey", pq tk ]
             | None -> [])
          @ (match optStr "transferInKey" k with
             | Some tk -> [ "transferInKey", pq tk ]
             | None -> [])
          @ (if boolOf "exportable" k then
               [ "exportable", "True" ]
             else
               [])
          @ (if boolOf "keepRowsTogether" k then
               [ "keepRowsTogether", "True" ]
             else
               [])
          @ (if boolOf "repeatHeader" k then
               [ "repeatHeader", "True" ]
             else
               [])))
  // Phase 1596 — `Drawing`, modelled by fuaran-py from 0.3.0. The root `style`
  // is omitted where the wire's is `{}`, exactly as a shape's is; `shapes` rides
  // always, the empty list included, because it is what the kind is for.
  | "Drawing" ->
    call
      "drawing"
      []
      ([ "viewBox", pyViewBox (fieldReq "viewBox" k)
         "shapes", pyList (arrOf "shapes" k |> List.map pyShape) ]
       @ pyDrawStyleKw k
       @ (match JsonValue.tryField "title" k with
          | Some ti -> [ "title", pyTextInput ti ]
          | None -> [])
       @ (match JsonValue.tryField "description" k with
          | Some d -> [ "description", pyTextInput d ]
          | None -> []))
  // ── Custom / ErrorBoundary / Fragments ────────────────────────────────────
  | "Custom" ->
    call
      "custom"
      []
      ([ "moduleId", pq (strOf "moduleId" k)
         "componentId", pq (strOf "componentId" k)
         "props", pyJson (fieldReq "props" k) ]
       @ (match JsonValue.tryField "contentHash" k with
          | Some h ->
            [ "contentHash",
              "t.ContentHash("
              + pq (strOf "algorithm" h)
              + ", "
              + pq (strOf "hash" h)
              + ", "
              + pq (strOf "strictness" h)
              + ")" ]
          | None -> [])
       @ (let ids = arrOf "exposedNodeIds" k

          if List.isEmpty ids then
            []
          else
            [ "exposedNodeIds", pyList (ids |> List.map pyStrItem) ]))
  | "ErrorBoundary" ->
    call
      "error_boundary"
      []
      [ "child", pyNodeExpr (depth + 1) (fieldReq "child" k)
        "fallback", pyNodeExpr (depth + 1) (fieldReq "fallback" k) ]
  | "FragmentDecl" ->
    call
      "fragment_decl"
      []
      ([ "name", pq (strOf "name" k)
         "body", pyNodeExpr (depth + 1) (fieldReq "body" k) ]
       @ (let holes = arrOf "holes" k

          if List.isEmpty holes then
            []
          else
            [ "holes", pyList (holes |> List.map pyHoleDecl) ])
       @ (match JsonValue.tryField "effect" k with
          | Some e ->
            [ "effect",
              "t.EffectClass("
              + pq (strOf "hostEffect" e)
              + ", "
              + pq (strOf "determinism" e)
              + ")" ]
          | None -> []))
  | "FragmentRef" ->
    call
      "fragment_ref"
      []
      ([ "name", pq (strOf "name" k) ]
       @ (let args = membersOf "args" k

          if List.isEmpty args then
            []
          else
            [ "args",
              "{"
              + (args
                 |> List.map (fun (key, a) -> pq key + ": " + pyFragArg depth a)
                 |> String.concat ", ")
              + "}" ]))
  // Phase 1596 — `Mount`, modelled by fuaran-py from 0.3.0. `inputs` reuses the
  // `FragmentArg` vocabulary above rather than growing a second one, which is
  // also what the generic fallback got wrong: it read the wire's `$type` as a
  // class name and reached for a `t.Str` that has never existed. `on_bubble`
  // defaults True and the wire omits `onBubble` when there is no handler, so the
  // absent key is the one that has to be said out loud.
  | "Mount" ->
    call
      "mount"
      []
      ([ "scopeId", pq (strOf "scopeId" k)
         "channel", pyGuestChannel (fieldReq "channel" k)
         "capabilities", pyList (arrOf "capabilities" k |> List.map pyStrItem) ]
       @ (match membersOf "inputs" k with
          | [] -> []
          | args ->
            [ "inputs",
              "{"
              + (args
                 |> List.map (fun (key, a) -> pq key + ": " + pyFragArg depth a)
                 |> String.concat ", ")
              + "}" ])
       @ (match JsonValue.tryField "onBubble" k with
          | Some _ -> []
          | None -> [ "onBubble", "False" ]))
  | "Switch" ->
    let caseVs = arrOf "cases" k

    // Phase 1535 — a case selects on `match` (the selector's string form) OR on
    // `when` (a predicate consulting no selector at all), never both. The
    // `fuaran.switch` ctor takes `(match, child)` PAIRS and so cannot carry a
    // predicate at all — it would reach the encoder with neither key, silently,
    // since an absent `match` is simply omitted. A switch with any predicate
    // case therefore takes the typed node literal, exactly as an
    // `autoAdvanceMs`-bearing one already does.
    let hasPredicate =
      caseVs |> List.exists (fun c -> (JsonValue.tryField "when" c).IsSome)

    // The Python constructor takes BOTH selector spellings, so — unlike the TS
    // leg, which has to post-edit the built node — a non-`State` selector is
    // expressed directly.
    match optNum "autoAdvanceMs" k, hasPredicate with
    | None, false ->
      let cases =
        caseVs
        |> List.map (fun c ->
          "("
          + pq (strOf "match" c)
          + ", "
          + pyNodeExpr (depth + 1) (fieldReq "child" c)
          + ")")

      call
        "switch"
        []
        ([ "cases", pyList cases
           "default", pyNodeExpr (depth + 1) (fieldReq "default" k) ]
         @ (match JsonValue.tryField "on" k with
            | Some onV -> [ "on", pyBinding Opq.Scalar onV ]
            | None -> [ "stateKey", pq (strOf "stateKey" k) ]))
    | autoAdvance, _ ->
      // Phase 1531 — the carousel's self-advance interval is a slot on the
      // record but not a parameter of `fuaran.switch`, so a switch declaring one
      // projects as the typed node literal (the ctor injects no ARIA default, so
      // the two forms are otherwise the same node). A predicate case takes the
      // same route, for the same reason.
      let typedCases =
        caseVs
        |> List.map (fun c ->
          "t.SwitchCase("
          + (match JsonValue.tryField "when" c with
             | Some w -> "when=" + pyBinding Opq.Scalar w
             | None -> "match=" + pq (strOf "match" c))
          + ", child="
          + pyNodeExpr (depth + 1) (fieldReq "child" c)
          + ")")

      Some(
        "t.UiNode(id="
        + pq id
        + ", kind="
        + pyCall
            "t.Switch"
            []
            ([ "stateKey",
               (match JsonValue.tryField "on" k with
                | Some _ -> "None"
                | None -> pq (strOf "stateKey" k))
               "cases", pyList typedCases
               "default", pyNodeExpr (depth + 1) (fieldReq "default" k) ]
             @ (match JsonValue.tryField "on" k with
                | Some onV -> [ "on", pyBinding Opq.Scalar onV ]
                | None -> [])
             @ (match autoAdvance with
                | Some ms -> [ "autoAdvanceMs", numLit ms ]
                | None -> []))
        + ")"
      )
  | _ -> None

// ─── the modern-host Box vocabulary (Go / Kotlin / Rust / Swift) ──────────────
//
// These four hosts model layout with a single `Box` kind carrying `role` +
// `layout`, where the legacy wire uses distinct Dashboard / Card / Stack /
// GridLayout kinds. Each leg folds the layout kinds into `Box` so it emits
// constructors that actually exist in that host – the rest of the fidelity bar
// is the same illustrative "how it would look" grade as the F# / C# / VB legs.

let private modernBoxRole (kind: string) : string option =
  match kind with
  | "Dashboard" -> Some "Dashboard"
  | "Card" -> Some "Card"
  | "Stack" -> Some "Group"
  | "GridLayout" -> Some "Group"
  | _ -> None

let private isGridKind (kind: string) : bool = kind = "GridLayout"

/// Drop the legacy layout params folded into `Box.layout` so they are not also
/// emitted as node fields.
let private dropLayoutParams (fields: (string * string) list) : (string * string) list =
  fields
  |> List.filter (fun (k, _) -> k <> "direction" && k <> "wrap" && k <> "cols" && k <> "columns" && k <> "gap")

// ─── Go (structural wire model – no per-kind builder) ─────────────────────────
//
// Go's host has no fluent builder: you assemble the structural `wire.Node` /
// `wire.Obj{Tag, Fields}` directly, and every leaf is a wrapped `wire.Value`
// (`wire.Int` / `wire.Str` / …). A bespoke walk (like VB) because the shared
// walker emits bare numeric literals, which a `map[string]wire.Value` rejects.

let rec private goValue (depth: int) (v: JsonValue) : string =
  match v with
  | JNull -> "wire.Null{}"
  | JBool b -> "wire.Bool(" + (if b then "true" else "false") + ")"
  | JNumber n ->
    if n = floor n && abs n < 1e15 then
      "wire.Int(" + string (int64 n) + ")"
    else
      "wire.Float(" + string n + ")"
  | JString s -> "wire.Str(\"" + escape '"' s + "\")"
  | JArray items ->
    if List.isEmpty items then
      "wire.Arr{}"
    else
      let body =
        items
        |> List.map (fun it -> pad (depth + 1) + goValue (depth + 1) it + ",")
        |> String.concat "\n"

      "wire.Arr{\n" + body + "\n" + pad depth + "}"
  | JObject _ when isNode v -> goNode depth v
  | JObject members ->
    match dollarType v with
    | Some "Literal" ->
      match v |> JsonValue.tryField "text" |> Option.bind JsonValue.asString with
      | Some t ->
        "wire.Obj{Tag: \"Literal\", Fields: map[string]wire.Value{\"text\": wire.Str(\""
        + escape '"' t
        + "\")}}"
      | None -> goObj depth members
    | _ -> goObj depth members

and private goObj (depth: int) (members: (string * JsonValue) list) : string =
  let tag =
    members
    |> List.tryPick (fun (k, vv) -> if k = "$type" then JsonValue.asString vv else None)
    |> Option.defaultValue ""

  let fields = members |> List.filter (fun (k, _) -> k <> "$type")

  if List.isEmpty fields then
    "wire.Obj{Tag: \"" + tag + "\"}"
  else
    let body =
      fields
      |> List.map (fun (k, vv) -> pad (depth + 1) + "\"" + k + "\": " + goValue (depth + 1) vv + ",")
      |> String.concat "\n"

    "wire.Obj{Tag: \""
    + tag
    + "\", Fields: map[string]wire.Value{\n"
    + body
    + "\n"
    + pad depth
    + "}}"

and private goNode (depth: int) (v: JsonValue) : string = markNode v (goNodeRaw depth v)

and private goNodeRaw (depth: int) (v: JsonValue) : string =
  let id =
    v
    |> JsonValue.tryField "id"
    |> Option.bind JsonValue.asString
    |> Option.defaultValue ""

  let kindObj =
    v |> JsonValue.tryField "kind" |> Option.defaultValue JNull |> resolveKind

  let kindName = dollarType kindObj |> Option.defaultValue "Node"

  let rawFields =
    match kindObj with
    | JObject members -> members |> List.filter (fun (k, _) -> k <> "$type" && k <> "kind")
    | _ -> []

  let renderField (k: string) (vv: JsonValue) =
    pad (depth + 2) + "\"" + k + "\": " + goValue (depth + 2) vv + ","

  let kindObjStr =
    match modernBoxRole kindName with
    | Some role ->
      let layout =
        if isGridKind kindName then
          "wire.Obj{Tag: \"Grid\"}"
        else
          "wire.Obj{Tag: \"Flex\", Fields: map[string]wire.Value{\"direction\": wire.Str(\"Vertical\"), \"wrap\": wire.Bool(false)}}"

      let kept =
        rawFields
        |> List.filter (fun (k, _) -> k <> "direction" && k <> "wrap" && k <> "cols" && k <> "gap")

      let lines =
        (kept |> List.map (fun (k, vv) -> renderField k vv))
        @ [ pad (depth + 2) + "\"role\": wire.Str(\"" + role + "\"),"
            pad (depth + 2) + "\"layout\": " + layout + "," ]

      "wire.Obj{Tag: \"Box\", Fields: map[string]wire.Value{\n"
      + String.concat "\n" lines
      + "\n"
      + pad (depth + 1)
      + "}}"
    | None ->
      if List.isEmpty rawFields then
        "wire.Obj{Tag: \"" + kindName + "\"}"
      else
        let body =
          rawFields |> List.map (fun (k, vv) -> renderField k vv) |> String.concat "\n"

        "wire.Obj{Tag: \""
        + kindName
        + "\", Fields: map[string]wire.Value{\n"
        + body
        + "\n"
        + pad (depth + 1)
        + "}}"

  "wire.Node{ID: \"" + escape '"' id + "\", Kind: " + kindObjStr + "}"

// ─── Rust (typed model – enum/struct literals, no fluent builder) ─────────────

let private rustSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let idLit = "\"" + escape '"' id + "\".into()"

        let specLines (fs: (string * string) list) =
          fs
          |> List.map (fun (k, v) -> pad (depth + 2) + toSnake k + ": " + v)
          |> String.concat ",\n"

        let kindExpr =
          match modernBoxRole kind with
          | Some role ->
            let layout =
              if isGridKind kind then
                "BoxLayout::Grid { .. }"
              else
                "BoxLayout::Flex { direction: Orientation::Vertical, wrap: false }"

            let boxFields =
              dropLayoutParams fields @ [ "layout", layout; "role", "BoxRole::" + role ]

            "NodeKind::Box(BoxSpec {\n"
            + specLines boxFields
            + ",\n"
            + pad (depth + 1)
            + "})"
          | None ->
            if List.isEmpty fields then
              "NodeKind::" + kind + "(" + kind + "Spec::default())"
            else
              "NodeKind::"
              + kind
              + "("
              + kind
              + "Spec {\n"
              + specLines fields
              + ",\n"
              + pad (depth + 1)
              + "})"

        "Node {\n"
        + pad (depth + 1)
        + "id: "
        + idLit
        + ",\n"
        + pad (depth + 1)
        + "kind: "
        + kindExpr
        + ",\n"
        + pad (depth + 1)
        + "state: StateBehaviour::default(),\n"
        + pad (depth + 1)
        + "style: SemanticStyle::default(),\n"
        + pad (depth + 1)
        + "accessibility: None,\n"
        + pad depth
        + "}"
    Obj =
      fun members depth ->
        let body =
          members
          |> List.filter (fun (k, _) -> k <> "$type")
          |> List.map (fun (k, v) -> pad (depth + 1) + toSnake k + ": " + v)
          |> String.concat ",\n"

        if body = "" then
          "Default::default()"
        else
          "{\n" + body + ",\n" + pad depth + "}"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "vec![]"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat ",\n"
          "vec![\n" + body + ",\n" + pad depth + "]"
    Str = fun s -> "\"" + escape '"' s + "\""
    Bool = fun b -> if b then "true" else "false"
    Null = "None"
    StaticBinding = fun inner -> "Binding::StaticValue(" + inner + ")"
    TextLiteral = fun t -> "TextSource::Literal(\"" + escape '"' t + "\".into())" }

// ─── Kotlin (sealed model – data-class construction; decode-only host) ────────

let private ktSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let idLit = "\"" + escape '"' id + "\""

        let named (fs: (string * string) list) =
          fs
          |> List.map (fun (k, v) -> pad (depth + 1) + k + " = " + v)
          |> String.concat ",\n"

        let kindExpr =
          match modernBoxRole kind with
          | Some role ->
            let layout =
              if isGridKind kind then
                "BoxLayout.Grid()"
              else
                "BoxLayout.Flex(Orientation.Vertical, false)"

            let boxFields =
              dropLayoutParams fields @ [ "layout", layout; "role", "BoxRole." + role ]

            "Box(\n" + named boxFields + ")"
          | None ->
            if List.isEmpty fields then
              kind + "()"
            else
              kind + "(\n" + named fields + ")"

        "Node(" + idLit + ", " + kindExpr + ")"
    Obj =
      fun members depth ->
        let body =
          members
          |> List.filter (fun (k, _) -> k <> "$type")
          |> List.map (fun (k, v) -> pad (depth + 1) + "\"" + k + "\" to " + v)
          |> String.concat ",\n"

        if body = "" then "mapOf()" else "mapOf(\n" + body + ")"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "listOf()"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat ",\n"
          "listOf(\n" + body + ")"
    Str = fun s -> "\"" + escape '"' s + "\""
    Bool = fun b -> if b then "true" else "false"
    Null = "null"
    StaticBinding = fun inner -> "Binding.StaticValue(" + inner + ")"
    TextLiteral = fun t -> "LiteralText(\"" + escape '"' t + "\")" }

// ─── Swift (sealed model – enum-case construction; decode-only host) ──────────

let private swiftSpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let idLit = "\"" + escape '"' id + "\""

        let named (fs: (string * string) list) =
          fs
          |> List.map (fun (k, v) -> pad (depth + 1) + k + ": " + v)
          |> String.concat ",\n"

        let kindExpr =
          match modernBoxRole kind with
          | Some role ->
            let layout =
              if isGridKind kind then
                ".grid()"
              else
                ".flex(direction: .vertical, wrap: false)"

            let boxFields =
              dropLayoutParams fields @ [ "layout", layout; "role", "." + lowerFirst role ]

            ".box(BoxSpec(\n" + named boxFields + "))"
          | None ->
            if List.isEmpty fields then
              "." + lowerFirst kind + "(" + kind + "Spec())"
            else
              "." + lowerFirst kind + "(" + kind + "Spec(\n" + named fields + "))"

        "Node(id: " + idLit + ", kind: " + kindExpr + ")"
    Obj =
      fun members depth ->
        let body =
          members
          |> List.filter (fun (k, _) -> k <> "$type")
          |> List.map (fun (k, v) -> pad (depth + 1) + "\"" + k + "\": " + v)
          |> String.concat ",\n"

        if body = "" then
          "[:]"
        else
          "[\n" + body + "\n" + pad depth + "]"
    Arr =
      fun items depth ->
        if List.isEmpty items then
          "[]"
        else
          let body = items |> List.map (fun it -> pad (depth + 1) + it) |> String.concat ",\n"
          "[\n" + body + "\n" + pad depth + "]"
    Str = fun s -> "\"" + escape '"' s + "\""
    Bool = fun b -> if b then "true" else "false"
    Null = "nil"
    StaticBinding = fun inner -> ".staticValue(" + inner + ")"
    TextLiteral = fun t -> ".literal(\"" + escape '"' t + "\")" }

// ─── entry points ─────────────────────────────────────────────────────────────

let private tickHeader (lang: string) : string =
  "' Illustrative projection – how the current tree would look authored in "
  + lang
  + ".\n' Demo-grade (not a verified byte round-trip; closures/handlers are sketched).\n\n"

let private header (lang: string) : string =
  "// Illustrative projection – how the current tree would look authored in "
  + lang
  + ".\n// Demo-grade (not a verified byte round-trip; closures/handlers are sketched).\n\n"

let private hashHeader (lang: string) : string =
  "# Illustrative projection – how the current tree would look authored in "
  + lang
  + ".\n# Demo-grade (not a verified byte round-trip; closures/handlers are sketched).\n\n"

/// Re-indent compact JSON via the host JSON engine for a readable JSON tab
/// (Fable: `JSON.stringify` with 2-space indent).
let private prettyReindent (compact: string) : string =
  emitJsExpr compact "JSON.stringify(JSON.parse($0), null, 2)"

/// Pretty-print the canonical wire JSON for the JSON tab. Falls back to the raw
/// input when it is not parseable.
let toJson (wireJson: string) : string =
  match JsonHost.parse wireJson with
  | Some v -> JsonHost.serialize v |> prettyReindent
  | None -> wireJson

let private project (spec: LangSpec) (wireJson: string) : string =
  match JsonHost.parse wireJson with
  | Some tree -> renderValue spec 0 tree
  | None -> "/* no decodable tree yet */"

// There is exactly ONE walk per language, and both entry points ride it: the
// plain projection (`projectTo` and the `toX` family) runs it with marking off,
// and `projectSpans` runs the same walk with marking on. So the text a
// highlighted pane shows is byte-for-byte the text the Output box shows — the
// drift a second, "span-aware" projector would have invited cannot arise,
// because there is no second projector.

let private tsExprWalk (wireJson: string) : string =
  match JsonHost.parse wireJson with
  | Some tree when isNode tree -> tsNodeExpr 0 tree
  | Some tree -> renderValue tsSpec 0 tree
  | None -> "/* no decodable tree yet */"

let private pyExprWalk (wireJson: string) : string =
  match JsonHost.parse wireJson with
  | Some tree when isNode tree -> pyNodeExpr 0 tree
  | Some tree -> pyGenericValue 0 tree
  | None -> "# no decodable tree yet"

let private vbWalk (wireJson: string) : string =
  let body =
    match JsonHost.parse wireJson with
    | Some tree when isNode tree -> vbNode 0 tree
    | Some _
    | None -> "<!-- no decodable node tree yet -->"

  tickHeader "VB (Fuaran.UI.VisualBasic)" + body

let private goWalk (wireJson: string) : string =
  let body =
    match JsonHost.parse wireJson with
    | Some tree when isNode tree -> goNode 0 tree
    | Some _
    | None -> "// no decodable node tree yet"

  header "Go (wire structural model)" + body

/// The one walk per target, headers included — so when it runs marking, a
/// span's offsets index exactly the text a pane displays rather than a
/// header-less fragment of it. JSON has no generator to instrument (it is the
/// canonical encoding re-indented by the host) and never marks; its spans come
/// from `jsonSpans` below.
let private walkFor (target: Target) (wireJson: string) : string =
  match target with
  | Target.Json -> toJson wireJson
  | Target.TypeScript ->
    "// The current tree as TypeScript (@fuaran-ui/ui) smart-constructor source.\n"
    + "// Verified projection: executing this source re-encodes byte-identically to the canonical\n"
    + "// wire JSON for every corpus-covered kind (closures/handlers are structural placeholders).\n\n"
    + tsExprWalk wireJson
  | Target.Python ->
    "# The current tree as Python (fuaran_py.ui) authoring source.\n"
    + "# Verified projection: executing this source re-encodes byte-identically to the canonical\n"
    + "# wire JSON for every corpus-covered construct (closures/handlers are structural placeholders).\n"
    + "#\n"
    + "#     from fuaran_py.ui import fuaran, binding, action, format, encode\n"
    + "#     from fuaran_py.ui import compute as cp\n"
    + "#     from fuaran_py.schema import types as t\n\n"
    + pyExprWalk wireJson
  | Target.FSharp -> header "F# (Fuaran.UI)" + project fsSpec wireJson
  | Target.CSharp -> header "C# (Fuaran.UI.CSharp)" + project csSpec wireJson
  | Target.VisualBasic -> vbWalk wireJson
  | Target.Go -> goWalk wireJson
  | Target.Kotlin -> header "Kotlin (fuaran-ui – decode-only host)" + project ktSpec wireJson
  | Target.Rust -> header "Rust (fuaran-rs)" + project rustSpec wireJson
  | Target.Swift -> header "Swift (FuaranUI – decode-only host)" + project swiftSpec wireJson

/// The bare projected TypeScript expression (no header) – the input of the
/// `tests/projection-conformance/` harness, which executes it against the real
/// `@fuaran-ui/ui` surface and asserts a byte-identical canonical re-encode.
let projectTypeScriptExpr (wireJson: string) : string = tsExprWalk wireJson

/// The bare projected Python expression (no header) – the input of the
/// `tests/projection-conformance/` Python arm, which executes it against the
/// real `fuaran_py.ui` surface and asserts a byte-identical canonical re-encode.
let projectPythonExpr (wireJson: string) : string = pyExprWalk wireJson

let toTypeScript (wireJson: string) : string = walkFor Target.TypeScript wireJson

let toPython (wireJson: string) : string = walkFor Target.Python wireJson

let toFSharp (wireJson: string) : string = walkFor Target.FSharp wireJson

let toCSharp (wireJson: string) : string = walkFor Target.CSharp wireJson

/// VB is the one target that does not ride the generic `LangSpec` walker – its
/// XML-literal shape has its own `vbNode` projector.
let toVisualBasic (wireJson: string) : string = walkFor Target.VisualBasic wireJson

/// Go rides a bespoke walk (like VB): its host has no per-kind builder, so the
/// tree is assembled as structural `wire.Node` / `wire.Obj` with wrapped values.
let toGo (wireJson: string) : string = walkFor Target.Go wireJson

let toKotlin (wireJson: string) : string = walkFor Target.Kotlin wireJson

let toRust (wireJson: string) : string = walkFor Target.Rust wireJson

let toSwift (wireJson: string) : string = walkFor Target.Swift wireJson

/// Project the wire tree to a target language's source – the single entry the
/// Output pane calls. Every target now renders (no more "coming soon").
let projectTo (target: Target) (wireJson: string) : string = walkFor target wireJson

// ─── the id → span side map (Phase 714) ───────────────────────────────────────

/// The JSON tab is the canonical encoding re-indented by the host engine, so
/// there is no generator to instrument – its spans are recovered by a
/// string-aware brace scan instead. Every object carrying both a string `id` and
/// a `kind` member is a node, and its span is that object's own braces. Reading
/// the rendered text (rather than re-emitting it with sentinels) keeps the JSON
/// tab byte-for-byte what it has always been.
let private jsonSpans (text: string) : Span list =
  let spans = ResizeArray<Span>()
  // One frame per open brace: where it started, the id it declared, and whether
  // it declared a `kind` (the two together are what makes an object a node).
  let starts = ResizeArray<int>()
  let ids = ResizeArray<string>()
  let kinds = ResizeArray<bool>()
  let n = text.Length

  // Read the string literal beginning at the quote `q`; returns its contents and
  // the index just past the closing quote. Escapes are copied verbatim – node
  // ids and key names carry none, and the scan only needs the delimiters right.
  let readString (q: int) : string * int =
    let parts = ResizeArray<string>()
    let mutable j = q + 1
    let mutable finish = -1

    while finish < 0 && j < n do
      if text[j] = '\\' && j + 1 < n then
        parts.Add(text.Substring(j, 2))
        j <- j + 2
      elif text[j] = '"' then
        finish <- j + 1
      else
        parts.Add(string text[j])
        j <- j + 1

    String.concat "" parts, (if finish < 0 then n else finish)

  let skipWs (j: int) : int =
    let mutable k = j

    while k < n && (text[k] = ' ' || text[k] = '\n' || text[k] = '\r' || text[k] = '\t') do
      k <- k + 1

    k

  let mutable i = 0

  while i < n do
    match text[i] with
    | '{' ->
      starts.Add i
      ids.Add ""
      kinds.Add false
      i <- i + 1
    | '}' ->
      if starts.Count > 0 then
        let last = starts.Count - 1
        let start = starts[last]
        let id = ids[last]
        let isNodeObj = kinds[last] && id <> ""
        starts.RemoveAt last
        ids.RemoveAt last
        kinds.RemoveAt last

        if isNodeObj then
          spans.Add
            { NodeId = id
              Start = start
              Length = i + 1 - start }

      i <- i + 1
    | '"' ->
      let token, afterToken = readString i
      let afterWs = skipWs afterToken

      if afterWs < n && text[afterWs] = ':' && starts.Count > 0 then
        let last = starts.Count - 1
        let valueAt = skipWs (afterWs + 1)

        if token = "kind" then
          kinds[last] <- true
        elif token = "id" && valueAt < n && text[valueAt] = '"' then
          ids[last] <- fst (readString valueAt)

      i <- afterToken
    | _ -> i <- i + 1

  List.ofSeq spans

/// A target's projection together with its id → span side map – what the
/// Navigator's projection panes render.
///
/// A tree whose own content carries a sentinel character projects normally with
/// an EMPTY map: the text stays byte-exact and the panes simply show no
/// highlight. The alternative – marking anyway – would hand back a wrong span
/// over text the strip had mangled, which is worse than no highlight in every
/// direction that matters.
let projectSpans (target: Target) (wireJson: string) : Projected =
  match target with
  | Target.Json ->
    // The brace scan reads the rendered text and is indifferent to what the
    // strings inside it contain, so JSON needs no guard and no marking.
    let text = toJson wireJson

    { Text = text; Spans = jsonSpans text }
  | _ when carriesSentinel wireJson ->
    { Text = projectTo target wireJson
      Spans = [] }
  | _ ->
    let marked =
      try
        marking <- true
        walkFor target wireJson
      finally
        marking <- false

    let text, spans = stripSpans marked

    { Text = text; Spans = spans }

/// The span a node id maps to. When an id is projected more than once (a nested
/// generic-sketch fallback, or a tree that repeats a node), the earliest and
/// widest occurrence wins – the one a reader would point at.
let spanFor (projected: Projected) (nodeId: string) : Span option =
  projected.Spans
  |> List.filter (fun s -> s.NodeId = nodeId)
  |> List.sortBy (fun s -> s.Start, -s.Length)
  |> List.tryHead

/// Nearest-enclosing resolution over a cursor id-path (root → focused, exactly
/// as the Navigator carries it): the focused node's own span where this language
/// projects it, else the closest ancestor that it does. A node folded into its
/// parent's construct – one sitting in a `state` slot the illustrative walkers
/// never visit, say – therefore highlights the construct that contains it rather
/// than nothing at all.
let spanForPath (projected: Projected) (idPath: string list) : Span option =
  idPath |> List.rev |> List.tryPick (spanFor projected)

/// The 1-based inclusive line range a span covers – what a pane reports beside
/// its language label without re-deriving offsets.
let lineRange (text: string) (span: Span) : int * int =
  let countNl (s: string) =
    s |> Seq.filter ((=) '\n') |> Seq.length

  let start = max 0 (min span.Start text.Length)
  let length = max 0 (min span.Length (text.Length - start))
  let startLine = 1 + countNl (text.Substring(0, start))

  startLine, startLine + countNl (text.Substring(start, length))

// ─── flat test surface (headless unit coverage) ──────────────────────────────

/// The `Target` a flat target name selects – the vitest surface's vocabulary.
let private targetNamed (targetName: string) : Target =
  match targetName with
  | "json" -> Target.Json
  | "typescript" -> Target.TypeScript
  | "python" -> Target.Python
  | "fsharp" -> Target.FSharp
  | "csharp" -> Target.CSharp
  | "vb" -> Target.VisualBasic
  | "go" -> Target.Go
  | "kotlin" -> Target.Kotlin
  | "rust" -> Target.Rust
  | "swift" -> Target.Swift
  | _ -> Target.Json

/// Project by target name ("json"/"typescript"/"python"/"fsharp"/"csharp"/"vb")
/// – a flat string surface assertable from vitest over the Fable output, so the
/// projector's never-crash + per-language shape are testable headlessly.
let projectByName (targetName: string) (wireJson: string) : string =
  projectTo (targetNamed targetName) wireJson

/// Every node id this language's projection maps, in document order – the side
/// map, flattened for the Fable boundary.
let spanIdsByName (targetName: string) (wireJson: string) : string array =
  let projected = projectSpans (targetNamed targetName) wireJson

  projected.Spans
  |> List.sortBy (fun s -> s.Start)
  |> List.map (fun s -> s.NodeId)
  |> Array.ofList

/// The projected source a node id maps to, or `""` when this language does not
/// project the node at all.
let spanTextByName (targetName: string) (wireJson: string) (nodeId: string) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanFor projected nodeId with
  | Some s -> projected.Text.Substring(s.Start, s.Length)
  | None -> ""

/// The projected source the nearest-enclosing resolution of a cursor id-path
/// (root → focused) lands on, or `""` when nothing on the path is projected.
let spanPathTextByName (targetName: string) (wireJson: string) (idPath: string array) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s -> projected.Text.Substring(s.Start, s.Length)
  | None -> ""

/// The id the nearest-enclosing resolution actually landed on – the focused node
/// when this language projects it, an ancestor when it does not, `""` when
/// nothing on the path is projected.
let spanPathIdByName (targetName: string) (wireJson: string) (idPath: string array) : string =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s -> s.NodeId
  | None -> ""

/// The 1-based inclusive `[start; end]` line range of the nearest-enclosing
/// span, or an empty array when nothing on the path is projected.
let spanPathLinesByName (targetName: string) (wireJson: string) (idPath: string array) : int array =
  let projected = projectSpans (targetNamed targetName) wireJson

  match spanForPath projected (List.ofArray idPath) with
  | Some s ->
    let a, b = lineRange projected.Text s
    [| a; b |]
  | None -> [||]

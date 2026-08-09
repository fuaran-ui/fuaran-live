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
//  **never crashes**: any value it does not specifically understand falls
//  through a generic object / array / literal path (the TS leg falls back to
//  the same sketch for a non-corpus kind), so an uncovered kind still projects
//  rather than throwing.
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

// ─── Python (fuaran_py.ui) ────────────────────────────────────────────────────

let private pySpec: LangSpec =
  { Node =
      fun kind id fields depth ->
        let ctor = "fuaran." + toSnake kind
        let idLit = "'" + escape '\'' id + "'"

        if List.isEmpty fields then
          ctor + "(" + idLit + ")"
        else
          let body =
            fields
            |> List.map (fun (k, v) -> pad (depth + 1) + toSnake k + "=" + v)
            |> String.concat ",\n"

          ctor + "(\n" + pad (depth + 1) + idLit + ",\n" + body + ",\n" + pad depth + ")"
    Obj =
      fun members depth ->
        if List.isEmpty members then
          "{}"
        else
          let body =
            members
            |> List.map (fun (k, v) -> pad (depth + 1) + "'" + escape '\'' k + "': " + v)
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
    Bool = fun b -> if b then "True" else "False"
    Null = "None"
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

// Wire-field accessors (total; the canonical corpus always carries the
// mandatory keys, so the defaults only fire on non-canonical input).
let private fieldD (name: string) (v: JsonValue) : JsonValue =
  v |> JsonValue.tryField name |> Option.defaultValue JNull

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
  | Some "lit" -> tsInline [ "kind", qs "lit"; "cell", tsCellLit (fieldD "cell" v) ]
  | Some "binary" ->
    tsInline
      [ "kind", qs "binary"
        "op", qs (strOf "op" v)
        "left", tsColExpr (fieldD "left" v)
        "right", tsColExpr (fieldD "right" v) ]
  | Some "not" -> tsInline [ "kind", qs "not"; "expr", tsColExpr (fieldD "expr" v) ]
  | Some "coalesce" ->
    tsInline
      [ "kind", qs "coalesce"
        "exprs", "[" + (arrOf "exprs" v |> List.map tsColExpr |> String.concat ", ") + "]" ]
  | Some "case" ->
    let cases =
      arrOf "cases" v
      |> List.map (fun c -> tsInline [ "when", tsColExpr (fieldD "when" c); "then", tsColExpr (fieldD "then" c) ])
      |> String.concat ", "

    tsInline
      [ "kind", qs "case"
        "cases", "[" + cases + "]"
        "else", tsColExpr (fieldD "else" v) ]
  | Some "cast" ->
    tsInline
      [ "kind", qs "cast"
        "type", qs (strOf "type" v)
        "expr", tsColExpr (fieldD "expr" v) ]
  | Some "apply" ->
    tsInline
      [ "kind", qs "apply"
        "fn", qs (strOf "fn" v)
        "args", "[" + (arrOf "args" v |> List.map tsColExpr |> String.concat ", ") + "]" ]
  | Some "param" -> tsInline [ "kind", qs "param"; "name", qs (strOf "name" v) ]
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
  | Some "filter" -> tsInline [ "kind", qs "filter"; "pred", tsColExpr (fieldD "pred" v) ]
  | Some "project" ->
    tsInline
      [ "kind", qs "project"
        "cols", "[" + (arrOf "cols" v |> List.map pair |> String.concat ", ") + "]" ]
  | Some "derive" ->
    tsInline
      [ "kind", qs "derive"
        "name", qs (strOf "name" v)
        "expr", tsColExpr (fieldD "expr" v) ]
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
        "source", tsDataSource (fieldD "source" v)
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
  | Some "union" -> tsInline [ "kind", qs "union"; "source", tsDataSource (fieldD "source" v) ]
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
    "binding.state("
    + qs (strOf "key" v)
    + ", "
    + tsStaticValue (fieldD "defaultValue" v)
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
    "{ kind: 'Now', project: (iso) => iso }"
  | Some "Local" ->
    "binding.local("
    + tsBinding Opq.Scalar (fieldD "initialFrom" v)
    + ", "
    + tsFlushTrigger (fieldD "flushOn" v)
    + ", () => action.chain([]), () => ({ ok: false, error: '' }))"
  | Some "Format" ->
    "binding.format("
    + tsBinding Opq.Scalar (fieldD "source" v)
    + ", "
    + tsFormatIntent (fieldD "format" v)
    + ", "
    + tsLocaleSource (fieldD "locale" v)
    + ")"
  | Some "Transform" ->
    // Raw `Binding.Transform` literal (not `binding.transform`, whose ctor takes
    // no `params`): `params` binds `ColExpr.Param` names to scalar sources (0.2.x
    // Phase 424), omitted-when-empty so a param-free Transform is byte-identical.
    let paramsArr = arrOf "params" v

    let paramsPart =
      if List.isEmpty paramsArr then
        ""
      else
        ", params: ["
        + (paramsArr
           |> List.map (fun p ->
             "{ from: "
             + tsBinding Opq.Scalar (fieldD "from" p)
             + ", name: "
             + qs (strOf "name" p)
             + " }")
           |> String.concat ", ")
        + "]"

    "{ kind: 'Transform', source: "
    + tsDataSource (fieldD "source" v)
    + ", pipeline: ["
    + (arrOf "pipeline" v |> List.map tsTransformStep |> String.concat ", ")
    + "]"
    + paramsPart
    + " }"
  | Some "Invoke" -> "binding.invoke(" + qs (strOf "capabilityId" v) + ", " + tsInvokeArgs v + ")"
  | _ -> "binding.static(undefined)"

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
    | Some "Bound" -> "{ kind: 'Bound', binding: " + tsBinding Opq.Scalar (fieldD "binding" v) + " }"
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
  tsInline [ "label", tsTextSourceLit (fieldD "label" o); "value", qs (strOf "value" o) ]

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
    + tsJson (fieldD "payload" v)
    + ")"
  | Some "Navigate" -> "action.navigate(" + qs (strOf "route" v) + ")"
  | Some "SetState" -> "action.setState(" + qs (strOf "key" v) + ", " + tsJson (fieldD "value" v) + ")"
  | Some "AiTool" ->
    "action.aiTool("
    + qs (strOf "toolName" v)
    + ", "
    + tsJson (fieldD "args" v)
    + ")"
  | Some "Chain" ->
    "action.chain(["
    + (arrOf "ops" v |> List.map tsAction |> String.concat ", ")
    + "])"
  | Some "CommitLocal" -> "action.commitLocal(" + qs (strOf "nodeId" v) + ")"
  | Some "WriteToClipboard" -> "action.writeToClipboard(" + qs (strOf "text" v) + ")"
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
  | "RangedNumber" -> "0"
  // Phase 766 — Toggle shares Checkbox's boolean value slot.
  | "Checkbox"
  | "Toggle" -> "false"
  | "Choice"
  | "SegmentedChoice" -> "undefined"
  | "Range" -> "[0, 0]"
  | "DateRange" -> "['', '']"
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
      let d = fieldD "defaultValue" valV

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
      @ [ "options", tsOptionsBinding (fieldD "options" v); "value", value ]
    )
  | "SegmentedChoice" ->
    tsInline (
      [ "kind", qs "SegmentedChoice" ]
      @ handler "onChange"
      @ [ "options", tsOptionsBinding (fieldD "options" v)
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
  | _ -> tsInline ([ "kind", qs "Text" ] @ handler "onChange" @ [ "value", value ])

let private tsFormField (depth: int) (v: JsonValue) : string =
  let id = strOf "id" v

  let fields =
    [ "id", qs id
      "label", tsTextSourceLit (fieldD "label" v)
      "kind", tsFieldKindLit (AutoBind.Form id) (fieldD "kind" v)
      "required", boolLit (boolOf "required" v) ]
    @ (match JsonValue.tryField "help" v with
       | Some h -> [ "help", tsTextSourceLit h ]
       | None -> [])

  tsObjLit fields depth

let private tsFilterSpec (depth: int) (v: JsonValue) : string =
  let name = strOf "name" v

  tsObjLit
    [ "name", qs name
      "label", tsTextSourceLit (fieldD "label" v)
      "field", tsFieldKindLit (AutoBind.Filter name) (fieldD "kind" v) ]
    depth

let private tsColumnWidth (v: JsonValue) : string =
  match dollarType v with
  | Some "Fixed" -> tsInline [ "kind", qs "Fixed"; "pixels", numLit (numOf "pixels" v) ]
  | Some "Flex" -> tsInline [ "kind", qs "Flex"; "weight", numLit (numOf "weight" v) ]
  | _ -> "{ kind: 'Auto' }"

let private tsGridColumn (v: JsonValue) : string =
  tsInline
    [ "label", qs (strOf "label" v)
      "value", "() => ({ kind: 'Empty' })"
      "format", tsCellFormat (fieldD "format" v)
      "kind", tsInline [ "kind", qs (dollarType (fieldD "kind" v) |> Option.defaultValue "Text") ]
      "width", tsColumnWidth (fieldD "width" v) ]

let private tsTabHeader (v: JsonValue) : string =
  tsInline (
    [ "label", tsTextSourceLit (fieldD "label" v) ]
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
        "countSpace", tsHoleSpace (fieldD "countSpace" v) ]
  | _ ->
    tsInline (
      [ "kind", qs "Value"
        "name", qs (strOf "name" v)
        "space", tsHoleSpace (fieldD "space" v) ]
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
  | None -> fieldD "source" k

/// A `DataGrid`'s erased column: `field` (declarative) and `value` (closure) are
/// sibling optional slots; `format`/`width` project their (default-omitted) form.
let private tsGridColumnErased (v: JsonValue) : string =
  let kindObj = fieldD "kind" v

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
      "format", tsCellFormat (fieldD "format" v)
      "width", tsColumnWidth (fieldD "width" v) ]
    @ (match JsonValue.tryField "value" v with
       | Some _ -> [ "value", "() => ({ kind: 'Text', value: '' })" ]
       | None -> [])
    @ (match optStr "field" v with
       | Some f -> [ "field", qs f ]
       | None -> [])
  )

/// `Drawing` (Phase 524) has no smart ctor, so it projects as the typed in-memory
/// spec literal: the wire tree is walked structurally (`$type` → `kind`; `Static`
/// bindings ride `{ kind: 'Static', value }`); the `text` / `description` /
/// `title` slots are `TextSource`, wrapped as `Literal` from their bare string.
let rec private tsDrawingConv (key: string) (v: JsonValue) : string =
  match v with
  | JString s when key = "text" || key = "description" || key = "title" -> "{ kind: 'Literal', value: " + qs s + " }"
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
  let kindObj = fieldD "kind" nodeV
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
  | Some "SlotArg" -> "{ kind: 'slot', tree: " + tsNodeExpr (depth + 1) (fieldD "tree" v) + " }"
  | _ -> "{ kind: 'value', value: " + tsFragScalar v + " }"

and private tsMountNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  // `@fuaran-ui/ui` ships no Mount smart ctor (Phase 265 landed wire parity
  // only), so Mount projects as the typed in-memory node literal directly.
  let channelV = fieldD "channel" k

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

and private tsBoxNode (depth: int) (id: string) (k: JsonValue) (nodeV: JsonValue) : string =
  // 0.2.0 — the consolidated `Box` layout kind (`role` + `layout`, absorbing
  // Dashboard / Stack / GridLayout / Card). Projected as the typed in-memory
  // node literal rather than a ctor, so a `Dashboard`-role Box can carry a
  // `heading` (the `dashboard` ctor takes none) and no ctor ARIA default leaks.
  let layoutV = fieldD "layout" k

  let layout =
    match dollarType layoutV with
    | Some "Flex" ->
      "{ kind: 'Flex', direction: "
      + qs (strOf "direction" layoutV)
      + ", wrap: "
      + boolLit (boolOf "wrap" layoutV)
      + " }"
    | Some "Grid" ->
      "{ kind: 'Grid', cols: "
      + numLit (numOf "cols" layoutV)
      + (match optStr "templateColumns" layoutV with
         | Some t -> ", templateColumns: " + qs t
         | None -> "")
      + " }"
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
      [ "label", tsTextSourceLit (fieldD "label" k)
        "value", tsTextSourceLit (fieldD "value" k)
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

      [ "staticRows", "{ headers: " + headers + ", rows: " + rows + " }" ]
    | None -> []

  let spec =
    tsInline (
      [ "columns", "[" + (cols |> List.map tsGridColumnErased |> String.concat ", ") + "]"
        "source", tsBinding Opq.Collection (fieldD "source" k) ]
      @ (if boolOf "editable" k then [ "editable", "true" ] else [])
      @ (match JsonValue.tryField "onRowClick" k with
         | Some _ -> [ "onRowClick", "() => action.chain([])" ]
         | None -> [])
      @ (match JsonValue.tryField "rowKey" k with
         | Some _ -> [ "rowKey", "() => ''" ]
         | None -> [])
      @ (match optStr "rowKeyField" k with
         | Some f -> [ "rowKeyField", qs f ]
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
    let layoutV = fieldD "layout" k
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
      @ [ "activeIndex", tsBinding Opq.Scalar (fieldD "activeIndex" k) ]
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
        "activeStep", tsBinding Opq.Scalar (fieldD "activeStep" k)
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
         "heading", tsTextInput (fieldD "heading" k)
         "open", tsBinding Opq.Scalar (fieldD "open" k)
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
       @ [ "open", tsBinding Opq.Scalar (fieldD "open" k)
           "dismissable", boolLit (boolOf "dismissable" k) ]
       @ (match JsonValue.tryField "onDismiss" k with
          | Some d -> [ "onDismiss", tsAction d ]
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
        "text", tsTextInput (fieldD "text" k)
        "level", numLit (numOf "level" k)
        "variant", qs (strOf "variant" k) ]
  | "Markdown" ->
    let t = fieldD "text" k

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
         "label", tsTextInput (fieldD "label" k)
         "value", tsBinding Opq.Scalar (valueOrSource k)
         "format", tsCellFormat (fieldD "format" k) ]
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
        "label", tsTextInput (fieldD "label" k)
        "variant", qs (strOf "variant" k) ]
  | "Sparkline" -> call "sparkline" [ idF; "source", tsBinding Opq.Collection (fieldD "source" k) ]
  | "Spacer" -> call "spacer" [ idF; "size", qs (strOf "size" k) ]
  | "Skeleton" -> Some("fuaran.skeleton(" + qs id + ", " + numLit (numOf "rows" k) + ")")
  | "Callout" ->
    call
      "callout"
      ([ idF
         "body", tsTextInput (fieldD "body" k)
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
         "fraction", tsBinding Opq.Scalar (fieldD "fraction" k)
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
         "label", tsTextInput (fieldD "label" k)
         "value", tsBinding Opq.Scalar (valueOrSource k)
         "format", tsCellFormat (fieldD "format" k)
         "emphasis", boolLit (boolOf "emphasis" k) ]
       @ (match JsonValue.tryField "help" k with
          | Some h -> [ "help", tsTextInput h ]
          | None -> []))
  | "Link" ->
    call
      "link"
      ([ idF
         "href", tsBinding Opq.Scalar (fieldD "href" k)
         "label", tsTextInput (fieldD "label" k)
         "download", boolLit (boolOf "download" k) ]
       @ (match optStr "rel" k with
          | Some r -> [ "rel", qs r ]
          | None -> [])
       @ (match optStr "target" k with
          | Some t -> [ "target", qs t ]
          | None -> []))
  | "Image" ->
    call
      "image"
      [ idF
        "src", tsBinding Opq.Scalar (fieldD "src" k)
        "alt", tsTextInput (fieldD "alt" k)
        "variant", qs (strOf "variant" k) ]
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
        "message", tsTextInput (fieldD "message" k)
        "tone", qs (strOf "tone" k)
        "open", tsBinding Opq.Scalar (fieldD "open" k)
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
         "label", tsTextInput (fieldD "label" k)
         "onClick", tsAction (fieldD "onClick" k)
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
         "label", tsTextInput (fieldD "label" k)
         "source", tsOptionsBinding (fieldD "source" k)
         "value", tsBinding Opq.Scalar (fieldD "value" k) ]
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
         "onSubmit", tsAction (fieldD "onSubmit" k)
         "submitLabel", tsTextInput (fieldD "submitLabel" k) ]
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
         "label", tsTextInput (fieldD "label" k)
         "accept", "[" + (arrOf "accept" k |> List.map strItem |> String.concat ", ") + "]"
         "multiple", boolLit (boolOf "multiple" k)
         "onSelect", "() => action.chain([])" ]
       @ (match JsonValue.tryField "disabled" k with
          | Some d -> [ "disabled", tsBinding Opq.Scalar d ]
          | None -> []))
  // ── Visualisation ─────────────────────────────────────────────────────────
  | "Chart" ->
    call
      "chart"
      ([ idF
         "source", tsBinding Opq.Collection (fieldD "source" k)
         "xField", qs (strOf "xField" k)
         "yFields", "[" + (arrOf "yFields" k |> List.map strItem |> String.concat ", ") + "]"
         "kind", qs (strOf "kind" k)
         "stacked", boolLit (boolOf "stacked" k) ]
       @ (match JsonValue.tryField "title" k with
          | Some t -> [ "title", tsTextInput t ]
          | None -> []))
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
        "source", tsMarkerBinding (fieldD "source" k)
        "centreLatitude", numLit (numOf "centreLatitude" k)
        "centreLongitude", numLit (numOf "centreLongitude" k)
        "zoom", numLit (numOf "zoom" k) ]
  | "DataGrid" ->
    call
      "grid"
      ([ idF
         "source", tsBinding Opq.Collection (fieldD "source" k)
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
         "props", tsJson (fieldD "props" k) ]
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
        "child", tsNodeExpr (depth + 1) (fieldD "child" k)
        "fallback", tsNodeExpr (depth + 1) (fieldD "fallback" k) ]
  | "FragmentDecl" ->
    call
      "fragmentDecl"
      ([ idF
         "name", qs (strOf "name" k)
         "body", tsNodeExpr (depth + 1) (fieldD "body" k) ]
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
    let cases =
      arrOf "cases" k
      |> List.map (fun c ->
        "{ match: "
        + qs (strOf "match" c)
        + ", child: "
        + tsNodeExpr (depth + 1) (fieldD "child" c)
        + " }")
      |> String.concat ", "

    // Phase 768 — the selector is any Binding. The State form keeps its compact
    // `stateKey` wire spelling and the smart-ctor expresses it directly; a
    // non-State `on` (Selection etc.) has NO ctor surface in the TS tier yet,
    // so the projection post-edits the built node's spec — exact, executable,
    // and honest about the ctor gap (a future SwitchOptions.on simplifies it).
    match JsonValue.tryField "on" k with
    | Some onV ->
      let built =
        "fuaran.switch("
        + tsObjLit
            [ idF
              "stateKey", "''"
              "cases", "[" + cases + "]"
              "default", tsNodeExpr (depth + 1) (fieldD "default" k) ]
            depth
        + ")"

      Some(
        "(() => { const n = "
        + built
        + "; return { ...n, kind: { ...n.kind, spec: { ...n.kind.spec, on: "
        + tsBinding Opq.Scalar onV
        + " } } }; })()"
      )
    | None ->
      call
        "switch"
        [ idF
          "stateKey", qs (strOf "stateKey" k)
          "cases", "[" + cases + "]"
          "default", tsNodeExpr (depth + 1) (fieldD "default" k) ]
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
  | Target.Python -> hashHeader "Python (fuaran_py.ui)" + project pySpec wireJson
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

module Fuaran.Showcase.Exhibit

// ============================================================================
//  The shared shell for the platform-baseline exhibit pages (Phase 1129).
//
//  Eleven pages land together, each showing one capability of the
//  platform-baseline wave in a real composition. They share four things and
//  nothing else: the page frame (title / lede / body / an honesty section), the
//  wire drawer, and the two render seams — a static render for a tree with no
//  live state, and a store-backed render for one that has.
//
//  A shared module rather than eleven copies. The showcase's older pages each
//  roll their own chrome, which was right when each was one page arriving on
//  its own; eleven arriving in one change-set is a different question, and
//  eleven copies of the same four helpers is residue by the time the second one
//  is written. Nothing page-specific lives here — every page still owns its own
//  tree, its own interaction and its own honesty claims, which are the parts
//  that are actually different.
//
//  The honesty section is deliberately part of the frame. Every page on this
//  site carries one, and the reason it is structural here is that these pages
//  are the promotion surface for capabilities that are easy to fake: a caption
//  track nobody reads, a sandbox nobody tests, an export button that downloads
//  a hard-coded file. A page that cannot say plainly what is real about it does
//  not belong in the set.
// ============================================================================

open Feliz
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Renderer

/// The dispatch runtime the exhibit pages render under. Permissive, because the
/// trees here are HAND-AUTHORED by this page rather than decoded from an
/// untrusted emission — the author is the trust boundary, which is the posture
/// `Sanitize.permissiveEgress` is named for one seam over. A page needing a
/// different gate (the embed exhibit, which is *about* the gate) builds its own.
let runtime: Runtime.IFuaranRuntime = BrowserRuntime.createPermissive ()

/// Render a tree that binds no state — the ordinary leg.
let renderStatic (n: Node<'msg>) : ReactElement =
  Render.renderWithSources BindingResolver.empty ignore n

/// Render a tree against the live `StateStore`, under a caller-supplied egress
/// policy. The caller subscribes with `StateStore.useStateKeys` so a write
/// re-renders; this function only supplies the snapshot the resolver reads.
let renderLiveWith (egress: Sanitize.EgressPolicy) (node: Node<obj>) : ReactElement =
  Render.render
    { Sources =
        { BindingResolver.empty with
            State = StateStore.snapshot () }
      Runtime = runtime
      VisAdapter = VisAdapter.noOp<obj>
      Dispatch = ignore
      TelemetrySink = None
      InErrorBoundary = false
      Fragments = Render.collectFragments Map.empty node
      ExpandingFragments = Set.empty
      Scope = None
      SessionContext = Map.empty
      EgressPolicy = egress
      UploadSink = None
      ActionSink = None
      CurrentNodeId = None }
    node

/// The common case: a hand-authored tree whose destinations are this origin's
/// own assets. `denyNonLocalEgress` permits same-origin references and refuses
/// everything that leaves — which is the whole of what these pages need, and
/// the honest posture for a site that claims to fetch nothing off-origin.
let renderLive (node: Node<obj>) : ReactElement =
  renderLiveWith Sanitize.denyNonLocalEgress node

/// One claim in a page's honesty section. `Verified` is a claim this page can
/// demonstrate on screen; `Limit` is something the page does NOT do, said
/// plainly. The distinction is rendered, not merely written — a page whose
/// limits are indistinguishable from its claims is not being honest, it is
/// being long.
[<RequireQualifiedAccess>]
type Claim =
  | Verified of string
  | Limit of string

let private claimItem (c: Claim) : ReactElement =
  match c with
  | Claim.Verified text ->
    Html.li
      [ prop.className "px-claim px-claim-verified"
        prop.children
          [ Html.span [ prop.className "px-claim-tag"; prop.text "shown" ]
            Html.span [ prop.text text ] ] ]
  | Claim.Limit text ->
    Html.li
      [ prop.className "px-claim px-claim-limit"
        prop.children
          [ Html.span [ prop.className "px-claim-tag"; prop.text "not shown" ]
            Html.span [ prop.text text ] ] ]

/// The collapsible wire drawer: the canonical bytes behind what the reader just
/// saw. Every exhibit page carries one, because "it is data" is the claim and a
/// claim you cannot open is a slogan.
///
/// `<details>` rather than a state-bearing component, matching the shell's own
/// footer drawer: the open/closed state is the element's, so the drawer works
/// with scripting off and costs no hook.
let wireDrawer (label: string) (json: string) : ReactElement =
  Html.details
    [ prop.className "px-wire"
      prop.children
        [ Html.summary [ prop.className "px-wire-toggle"; prop.text label ]
          Html.pre
            [ prop.className "px-wire-json"
              prop.children [ Html.code [ prop.text json ] ] ] ] ]

/// A titled panel — the unit every exhibit page builds its body out of.
let panel (title: string) (note: string) (children: ReactElement list) : ReactElement =
  Html.section
    [ prop.className "px-panel"
      prop.children
        [ Html.h3 [ prop.className "px-panel-title"; prop.text title ]
          (if note = "" then
             Html.none
           else
             Html.p [ prop.className "px-panel-note"; prop.text note ])
          Html.div [ prop.className "px-panel-body"; prop.children children ] ] ]

/// The page frame. `slug` becomes the root class so a page can style its own
/// interior without inventing a second wrapper.
let shell (slug: string) (title: string) (lede: string) (body: ReactElement list) (claims: Claim list) : ReactElement =
  Html.div
    [ prop.className ("px-page px-" + slug)
      prop.children
        [ Html.h1 [ prop.className "px-title"; prop.text title ]
          Html.p [ prop.className "px-lede"; prop.text lede ]
          Html.div [ prop.className "px-body"; prop.children body ]
          Html.div
            [ prop.className "px-honesty"
              prop.children
                [ Html.h3 [ prop.text "How honest is this?" ]
                  Html.ul [ prop.children [ for c in claims -> claimItem c ] ] ] ] ] ]

module Fuaran.Live.Voice

// ============================================================================
//  Client-only voice input (Phase 401) – "speak a UI into existence".
//
//  The browser's Web Speech API transcribes speech into the existing prompt box,
//  which drives the already-shipped prompt → LLM-emit → render loop UNCHANGED –
//  voice is purely an input adapter over the prompt textarea. No new server, no
//  account, no key beyond the user's own LLM key: the demo itself sends audio
//  nowhere; recognition is the browser's own. A capability check + a graceful
//  fallback keep the playground working where the API is absent, so it stays
//  statically hostable like the rest of fuaran-live.
//
//  The Web Speech surface is browser-only, so it lives behind a thin Emit seam;
//  the one piece of real logic (composing the live prompt from its three parts)
//  is a pure function, unit-testable headlessly.
// ============================================================================

open Fable.Core

// The raw capability probe. `[<Emit>]` functions are inlined at the call site
// and NOT exported, so it is wrapped by `isSupported` below to give a real,
// callable/exportable function (headlessly testable).
[<Emit("(typeof window !== 'undefined' && (('SpeechRecognition' in window) || ('webkitSpeechRecognition' in window)))")>]
let private hasSpeechApi () : bool = jsNative

/// Is a SpeechRecognition implementation available in this browser? Guards
/// against a missing `window` (headless / SSR) so it is safe to call anywhere –
/// a `false` here hides the mic and the rest of the app is unaffected.
let isSupported () : bool = hasSpeechApi ()

/// The live prompt during dictation: the text present when dictation began, then
/// the recognised final segments, then the in-progress interim guess. Pure – the
/// single place the three pieces compose, so the input adapter is unit-testable
/// even though the Web Speech API itself is browser-only.
let composePrompt (baseText: string) (finalText: string) (interimText: string) : string =
  let sep =
    if
      baseText <> ""
      && not (baseText.EndsWith " ")
      && (finalText <> "" || interimText <> "")
    then
      " "
    else
      ""

  baseText + sep + finalText + interimText

/// Start dictation. `onTranscript(finalText, interimText)` fires as results
/// stream – final segments accumulate, interim is the live guess; `onError`
/// carries a Web Speech error code; `onEnd` fires when recognition stops (user
/// stop, endpoint silence, or error). Returns the recognition handle for `stop`.
/// Delegates (not curried F# funcs) so the hand-written JS calls them uncurried.
[<Emit("""(function(onTranscript, onError, onEnd){
  var Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
  var rec = new Ctor();
  rec.continuous = true;
  rec.interimResults = true;
  rec.lang = (typeof navigator !== 'undefined' && navigator.language) || 'en-US';
  rec.onresult = function(ev){
    var fin = '', interim = '';
    for (var i = 0; i < ev.results.length; i++) {
      var seg = ev.results[i][0] ? ev.results[i][0].transcript : '';
      if (ev.results[i].isFinal) fin += seg; else interim += seg;
    }
    onTranscript.Invoke ? onTranscript.Invoke(fin, interim) : onTranscript(fin, interim);
  };
  rec.onerror = function(ev){
    var code = (ev && ev.error) ? String(ev.error) : 'speech-error';
    (onError.Invoke ? onError.Invoke(code) : onError(code));
  };
  rec.onend = function(){ (onEnd.Invoke ? onEnd.Invoke() : onEnd()); };
  try { rec.start(); } catch (e) { (onError.Invoke ? onError.Invoke('start-failed') : onError('start-failed')); }
  return rec;
})($0, $1, $2)""")>]
let start (onTranscript: System.Action<string, string>) (onError: System.Action<string>) (onEnd: System.Action) : obj =
  jsNative

/// Stop a recognition handle. Harmless if it is already stopped or null.
[<Emit("{ try { if ($0) $0.stop(); } catch (e) {} }")>]
let stop (handle: obj) : unit = jsNative

/// A friendly, user-facing message for a Web Speech error code.
let friendlyError (code: string) : string =
  match code with
  | "not-allowed"
  | "service-not-allowed" -> "Microphone permission was denied. Allow mic access in your browser to dictate."
  | "no-speech" -> "No speech was detected – tap Speak and try again."
  | "audio-capture" -> "No microphone was found."
  | "network" -> "Your browser's speech service is unreachable right now."
  | "aborted" -> "Dictation stopped."
  | other -> "Speech recognition error: " + other

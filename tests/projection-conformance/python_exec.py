# Codegen-conformance — the Python arm's executor.
#
# Reads one JSON batch of projected expressions on stdin, evaluates each against
# the real `fuaran_py` authoring surface IN THIS SINGLE PROCESS, re-encodes the
# reconstructed node with the canonical encoder, and writes one JSON result per
# fixture on stdout. One interpreter for the whole corpus rather than one spawn
# per fixture: the import cost is paid once, and the harness stays fast enough to
# ride the default `pnpm test`.
#
# It ALSO answers, in the same process and against the same installed host, the
# question the quarantine's reasons make: "does this host model construct X?"
# The quarantine is a set of claims ABOUT this interpreter, so the interpreter is
# the only honest place to resolve them — see `resolve_construct` below and the
# construct-token grammar in `python.test.ts`.
#
# And it REPORTS THE HOST ITSELF (Phase 1582): its version, and its published
# capability manifest when the installed release carries a generator for one
# (WIRE_FORMAT.md §27). Both come from the interpreter that executes the corpus,
# for the same reason the construct probes do — a manifest read from anywhere
# else would describe a host this arm is not running. A release with no generator
# reports `capabilityManifest: null` with the import error, which is a fact about
# the pin and not a failure.
#
# stdin :  {"cases": [{"id": "...", "expr": "..."}, ...],
#           "constructs": ["t.Drawing", "Column.field_name", ...]}
# stdout:  {"results": [{"id": "...", "ok": true, "encoded": "..."} |
#                       {"id": "...", "ok": false, "error": "..."}, ...],
#           "constructs": {"t.Drawing": {"models": false, "detail": "..."} |
#                          {"error": "..."}, ...},
#           "host": "fuaran-py", "hostVersion": "0.4.0",
#           "capabilityManifest": {...} | null, "capabilityError": "..." | null}
#
# Nothing here decides conformance — the comparison against the wire fixture, and
# the verdict on a quarantine entry, are the vitest arm's job. This process only
# executes, encodes, and reports what the installed host carries.

from __future__ import annotations

import dataclasses
import inspect
import json
import sys
import traceback
import types as pytypes
import typing


def _camel(snake: str) -> str:
    """`on_change` → `onChange` — a record field's name in the WIRE."""
    head, *rest = snake.split("_")
    return head + "".join(p[:1].upper() + p[1:] for p in rest)


def _union_cases(obj: object) -> list[str] | None:
    """The case-class names of a union alias, or None when `obj` is not one.

    Both spellings must be admitted: `typing.Union[...]` and PEP 604 `A | B`
    (whose `get_origin` is `types.UnionType`, NOT `typing.Union`). `fuaran_py`
    writes the second, and checking only the first silently reports every union
    as "neither union nor record" — a resolver error, not a verdict, so it fails
    loudly rather than passing an entry vacuously.
    """
    if typing.get_origin(obj) in (typing.Union, pytypes.UnionType):
        return [getattr(a, "__name__", str(a)) for a in typing.get_args(obj)]
    return None


def _admits_none(owner: type, field: dataclasses.Field) -> bool:
    hints = typing.get_type_hints(owner)
    return type(None) in typing.get_args(hints.get(field.name, field.type))


def resolve_construct(token: str, modules: dict[str, object]) -> dict[str, object]:
    """Resolve one construct token against the INSTALLED host.

    Returns `{"models": bool, "detail": str}` — whether this host carries the
    construct — or `{"error": str}` when the token names nothing resolvable,
    which the harness treats as a failure of the ENTRY rather than as a verdict.
    The grammar is documented where it is authored, in `python.test.ts`.
    """
    try:
        optional = token.startswith("optional:")
        parts = (token[len("optional:") :] if optional else token).split(".")
        module = modules["t"]
        if parts[0] in modules:
            module, parts = modules[parts[0]], parts[1:]

        if len(parts) == 1:
            if optional:
                return {"error": "`optional:` needs Owner.field, not a bare symbol"}
            return {"models": hasattr(module, parts[0]), "detail": "symbol"}
        if len(parts) != 2:
            return {"error": "expected Owner.member, got %d segments" % len(parts)}

        owner = getattr(module, parts[0], None)
        if owner is None:
            return {"error": "no such symbol '%s'" % parts[0]}

        if optional:
            # "the host can OMIT this slot" — the falsifier a closure-sentinel or
            # non-optional-default entry needs. Two shapes reach the same wire
            # outcome and both are covered: a record with NO field for the key
            # (`TextField`, whose `to_wire` writes `"onChange": CLOSURE`), and a
            # record whose field is present but cannot be None (`Modal.on_dismiss`,
            # defaulted to an empty `Chain`). Either way the key is unconditional.
            if not dataclasses.is_dataclass(owner):
                return {"error": "'%s' is not a record" % parts[0]}
            fields = {f.name: f for f in dataclasses.fields(owner)}
            if parts[1] in fields:
                return {
                    "models": _admits_none(owner, fields[parts[1]]),
                    "detail": "field present; %s"
                    % (
                        "admits None"
                        if _admits_none(owner, fields[parts[1]])
                        else "cannot be None, so the key is always written"
                    ),
                }
            # No field — so the slot must at least be REACHABLE in the wire, or
            # the token is a typo rather than a claim. Refusing here is what
            # stops a misspelled field name from holding an entry vacuously.
            key = '"%s"' % _camel(parts[1])
            source = inspect.getsource(owner.to_wire)
            if key not in source:
                return {
                    "error": "'%s' has no field '%s' and its to_wire writes no %s"
                    % (parts[0], parts[1], key)
                }
            return {"models": False, "detail": "no field; to_wire writes %s always" % key}

        cases = _union_cases(owner)
        if cases is not None:
            return {"models": parts[1] in cases, "detail": "union cases: %s" % ", ".join(cases)}
        if dataclasses.is_dataclass(owner):
            names = [f.name for f in dataclasses.fields(owner)]
            return {"models": parts[1] in names, "detail": "fields: %s" % ", ".join(names)}
        return {"error": "'%s' is neither a union nor a record" % parts[0]}
    except Exception as exc:  # pragma: no cover — reported, never raised
        return {"error": f"{type(exc).__name__}: {exc}"}


def host_declaration() -> dict[str, object]:
    """This interpreter's identity and its published capability manifest (WIRE_FORMAT §27).

    The version is read from the imported package rather than from the installer's metadata, so
    what is reported is the release whose code is about to author the corpus. A release that
    predates the generator is reported as `capabilityManifest: null` with the import error in
    `capabilityError` — the harness turns that into a named fallback, never into a failure.
    """
    declaration: dict[str, object] = {
        "host": "fuaran-py",
        "hostVersion": None,
        "capabilityManifest": None,
        "capabilityError": None,
    }
    try:
        import fuaran_py

        declaration["hostVersion"] = getattr(fuaran_py, "__version__", None)
    except Exception as exc:  # pragma: no cover — the fatal import above already reported it
        declaration["capabilityError"] = f"{type(exc).__name__}: {exc}"
        return declaration

    try:
        from fuaran_py.conformance import host_capability

        declaration["capabilityManifest"] = host_capability.build()
    except Exception as exc:
        declaration["capabilityError"] = f"{type(exc).__name__}: {exc}"
    return declaration


def main() -> int:
    # The corpus carries astral-plane text, and on Windows the default stdio
    # codec is the ANSI code page — which mangles it silently, in BOTH
    # directions, so a fixture fails as a byte mismatch that names the emitter
    # rather than the pipe. Pin both ends to UTF-8 before reading anything.
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")

    try:
        from fuaran_py.ui import (  # noqa: F401 — bound into the eval namespace
            accessibility,
            action,
            binding,
            encode,
            format,
            fuaran,
            invoke,
            node,
            rule,
        )
        from fuaran_py.schema import types as t  # noqa: F401
        from fuaran_py.ui import compute as cp  # noqa: F401
    except Exception:  # pragma: no cover — reported to the harness, not raised
        json.dump(
            {"fatal": "fuaran_py is not importable:\n" + traceback.format_exc(), **host_declaration()},
            sys.stdout,
        )
        return 0

    # The exact names the projector may emit. Kept explicit rather than
    # `import *`: a projected expression that reaches for a name outside this
    # list is a projector defect, and it must fail here rather than resolve to
    # something the surface does not actually export.
    #
    # `float` is the one BUILTIN admitted, and it is admitted for a reason the
    # rule above does not cover: §7's non-finite sentinels ride the wire as the
    # strings "NaN" / "Infinity" / "-Infinity" but are FLOATS in every typed slot
    # that carries them, and Python spells those three values only as
    # `float('nan')` / `float('inf')` / `float('-inf')` — there is no literal.
    # It resolves to a number rather than to any part of the host surface, so it
    # cannot stand in for a construct the model omits, which is the property the
    # explicit list exists to protect.
    namespace = {
        "float": float,
        "fuaran": fuaran,
        "binding": binding,
        "action": action,
        "format": format,
        "rule": rule,
        "node": node,
        "accessibility": accessibility,
        "invoke": invoke,
        "t": t,
        "cp": cp,
    }

    batch = json.load(sys.stdin)
    results = []

    for case in batch["cases"]:
        try:
            reconstructed = eval(case["expr"], {"__builtins__": {}}, namespace)  # noqa: S307
            results.append({"id": case["id"], "ok": True, "encoded": encode(reconstructed)})
        except Exception as exc:
            results.append(
                {
                    "id": case["id"],
                    "ok": False,
                    "error": f"{type(exc).__name__}: {exc}",
                }
            )

    constructs = {
        token: resolve_construct(token, namespace) for token in batch.get("constructs", [])
    }

    json.dump({"results": results, "constructs": constructs, **host_declaration()}, sys.stdout)
    return 0


if __name__ == "__main__":
    sys.exit(main())

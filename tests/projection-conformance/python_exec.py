# Codegen-conformance — the Python arm's executor.
#
# Reads one JSON batch of projected expressions on stdin, evaluates each against
# the real `fuaran_py` authoring surface IN THIS SINGLE PROCESS, re-encodes the
# reconstructed node with the canonical encoder, and writes one JSON result per
# fixture on stdout. One interpreter for the whole corpus rather than one spawn
# per fixture: the import cost is paid once, and the harness stays fast enough to
# ride the default `pnpm test`.
#
# stdin :  {"cases": [{"id": "...", "expr": "..."}, ...]}
# stdout:  {"results": [{"id": "...", "ok": true, "encoded": "..."} |
#                       {"id": "...", "ok": false, "error": "..."}, ...]}
#
# Nothing here decides conformance — the comparison against the wire fixture is
# the vitest arm's job. This process only executes and encodes.

from __future__ import annotations

import json
import sys
import traceback


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
        json.dump({"fatal": "fuaran_py is not importable:\n" + traceback.format_exc()}, sys.stdout)
        return 0

    # The exact names the projector may emit. Kept explicit rather than
    # `import *`: a projected expression that reaches for a name outside this
    # list is a projector defect, and it must fail here rather than resolve to
    # something the surface does not actually export.
    namespace = {
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

    json.dump({"results": results}, sys.stdout)
    return 0


if __name__ == "__main__":
    sys.exit(main())

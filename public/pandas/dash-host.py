# =============================================================================
#  Pandas Dashboard – the Python authoring host, executed in-browser via Pyodide.
#
#  `fuaran` is a small ergonomic authoring surface (mirroring the fuaran-py typed
#  per-kind model): the visitor's cell computes with real pandas over a bundled
#  CSV, then authors a Fuaran tree with a handful of constructors. The tree is
#  emitted as canonical wire JSON (WIRE_FORMAT.md section 2 – sorted keys,
#  shortest-round-trip floats), and the F# host decodes and renders it. Node ids
#  are STABLE across re-runs so the F# side can derive a tree-diff op script –
#  the "it patched, it did not re-render" beat.
# =============================================================================

import re


# ── canonical JSON (independent impl of the pinned rules) ────────────────────

def _fmt_double(n):
    if n == 0:
        return "0"
    neg = n < 0
    s = repr(abs(n))
    digits, exp = "", 0
    e_idx = s.find("e")
    if e_idx >= 0:
        mant = s[:e_idx]
        mant_exp = int(s[e_idx + 1:])
        dot = mant.find(".")
        if dot < 0:
            digits, exp = mant, mant_exp + (len(mant) - 1)
        else:
            digits, exp = mant[:dot] + mant[dot + 1:], mant_exp + (dot - 1)
    else:
        dot = s.find(".")
        if dot < 0:
            digits, exp = s, len(s) - 1
        else:
            int_part, frac_part = s[:dot], s[dot + 1:]
            if int_part == "0":
                trimmed = frac_part.lstrip("0")
                leading = len(frac_part) - len(trimmed)
                digits, exp = frac_part[leading:], -(leading + 1)
            else:
                digits, exp = int_part + frac_part, len(int_part) - 1
    digits = digits.rstrip("0") or "0"
    if -4 <= exp <= 16:
        if exp >= 0:
            if len(digits) <= exp + 1:
                out = digits + "0" * (exp + 1 - len(digits))
            else:
                out = digits[:exp + 1] + "." + digits[exp + 1:]
        else:
            out = "0." + "0" * (-exp - 1) + digits
    else:
        mant = digits if len(digits) == 1 else digits[0] + "." + digits[1:]
        sign = "+" if exp >= 0 else "-"
        out = mant + "E" + sign + str(abs(exp)).rjust(2, "0")
    return ("-" + out) if neg else out


def _quote(s):
    parts = ['"']
    for ch in s:
        o = ord(ch)
        if ch == '"':
            parts.append('\\"')
        elif ch == "\\":
            parts.append("\\\\")
        elif o < 0x20:
            parts.append("\\u%04x" % o)
        else:
            parts.append(ch)
    parts.append('"')
    return "".join(parts)


def _canon(v):
    if v is None:
        return "null"
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, str):
        return _quote(v)
    if isinstance(v, (int, float)):
        return _fmt_double(float(v))
    if isinstance(v, list):
        return "[" + ",".join(_canon(x) for x in v) + "]"
    keys = sorted(v.keys())
    return "{" + ",".join(_quote(k) + ":" + _canon(v[k]) for k in keys) + "}"


def _slug(s):
    return re.sub(r"[^a-z0-9]+", "-", str(s).lower()).strip("-") or "x"


# ── the ergonomic authoring surface ──────────────────────────────────────────

class _Fuaran:
    # Current canonical text form: a plain-literal TextSource is a bare JSON
    # string on the wire (the Literal envelope collapses); defaults are omitted.
    def _lit(self, s):
        return str(s)

    def metric(self, label, value):
        # The current canonical Metric: `value` (the 0.2.0 value/source law),
        # a bare-string label, and every default omitted (emphasis, format,
        # tone, weight) - omit-when-default is part of the byte contract.
        return {
            "id": "m-" + _slug(label),
            "kind": {
                "$type": "Metric",
                "label": self._lit(label),
                "value": {"$type": "Static", "value": float(value)},
            },
        }

    def metric_strip(self, pairs):
        return {
            "id": "strip",
            "kind": {
                "$type": "Box",
                "children": [self.metric(l, v) for l, v in pairs],
                "layout": {"$type": "Flex", "direction": "Horizontal", "wrap": True},
                "role": "Group",
            },
        }

    def markdown(self, text, node_id="insight"):
        return {"id": node_id, "kind": {"$type": "Markdown", "text": self._lit(text)}}

    def _cell(self, v):
        # pandas records carry numpy scalars (int64/float64) - normalise to
        # plain Python values so the canonical encoder sees real ints/floats.
        if hasattr(v, "item"):
            v = v.item()
        if isinstance(v, bool) or v is None:
            return v
        if isinstance(v, (int, float)):
            return v
        return str(v)

    def grid(self, records, node_id="grid"):
        # A REAL DataGrid over an embedded columnar frame - the certified
        # decoded wire shape (field-named columns + rowKeyField + a Transform
        # source with an empty pipeline over the frame). A hidden `_row` column
        # provides a unique row key without appearing in the display columns.
        records = list(records)
        if not records:
            return self.markdown("(no rows)", node_id)
        keys = [str(k) for k in records[0].keys()]
        n = len(records)
        cells = {k: [self._cell(r.get(k)) for r in records] for k in keys}

        def col_type(vs):
            non_null = [v for v in vs if v is not None and not isinstance(v, bool)]
            if non_null and all(isinstance(v, int) for v in non_null):
                return "int"
            if non_null and all(isinstance(v, (int, float)) for v in non_null):
                return "float"
            return "string"

        frame_cols = {"_row": {"validity": [True] * n, "values": list(range(n))}}
        schema = [{"name": "_row", "type": "int"}]
        for k in keys:
            vs = cells[k]
            t = col_type(vs)
            if t == "string":
                vs = ["" if v is None else str(v) for v in vs]
            frame_cols[k] = {"validity": [v is not None for v in cells[k]], "values": vs}
            schema.append({"name": k, "type": t})

        return {
            "id": node_id,
            "kind": {
                "$type": "DataGrid",
                "columns": [
                    {"field": k, "kind": {"$type": "Text"}, "label": k.replace("_", " ").title()}
                    for k in keys
                ],
                "rowKeyField": "_row",
                "source": {
                    "$type": "Transform",
                    "pipeline": [],
                    "source": {"columns": frame_cols, "schema": schema},
                },
            },
        }

    def dashboard(self, title, *children):
        return {
            "id": "dash-root",
            "kind": {
                "$type": "Box",
                "heading": self._lit(title),
                "children": list(children),
                "layout": {"$type": "Flex", "direction": "Vertical", "wrap": False},
                "role": "Dashboard",
            },
        }


fuaran = _Fuaran()


# ── the cell runner ───────────────────────────────────────────────────────────

def run_cell(code):
    ns = {"fuaran": fuaran}
    try:
        import pandas as pd
        ns["pd"] = pd
    except Exception:
        pass
    exec(code, ns)
    app = ns.get("app")
    if app is None:
        raise ValueError("Your cell must end by assigning `app = fuaran.dashboard(...)`.")
    return _canon(app)

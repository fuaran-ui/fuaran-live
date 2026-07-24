# =============================================================================
#  Rosetta – the Python authoring host, executed in-browser via Pyodide.
#
#  An independent implementation of the pinned canonical-JSON rules
#  (WIRE_FORMAT.md section 2): sorted keys, shortest-round-trip floats,
#  deterministic escaping. It shares no code with the F# or TypeScript hosts –
#  only the six typed holes cross the boundary as JSON. It builds its own tree,
#  encodes the canonical wire, and hashes it with the CPython standard library.
#  Three languages, three SHA-256 implementations, one set of bytes.
# =============================================================================

import json
import hashlib


def _fmt_double(n):
    # The shortest round-tripping decimal, laid out in .NET "R" notation – the
    # same layout the F# and TypeScript hosts produce. repr(float) is Python's
    # shortest-round-trip form (since 3.1), matching JS Number.toString().
    if n == 0:
        return "0"
    neg = n < 0
    s = repr(abs(n))

    digits = ""
    exp = 0
    e_idx = s.find("e")

    if e_idx >= 0:
        mant = s[:e_idx]
        mant_exp = int(s[e_idx + 1:])
        dot = mant.find(".")
        if dot < 0:
            digits = mant
            exp = mant_exp + (len(mant) - 1)
        else:
            digits = mant[:dot] + mant[dot + 1:]
            exp = mant_exp + (dot - 1)
    else:
        dot = s.find(".")
        if dot < 0:
            digits = s
            exp = len(s) - 1
        else:
            int_part = s[:dot]
            frac_part = s[dot + 1:]
            if int_part == "0":
                trimmed = frac_part.lstrip("0")
                leading = len(frac_part) - len(trimmed)
                digits = frac_part[leading:]
                exp = -(leading + 1)
            else:
                digits = int_part + frac_part
                exp = len(int_part) - 1

    digits = digits.rstrip("0")
    if digits == "":
        digits = "0"

    if -4 <= exp <= 16:
        if exp >= 0:
            if len(digits) <= exp + 1:
                out = digits + "0" * (exp + 1 - len(digits))
            else:
                out = digits[:exp + 1] + "." + digits[exp + 1:]
        else:
            out = "0." + "0" * (-exp - 1) + digits
    else:
        mantissa = digits if len(digits) == 1 else digits[0] + "." + digits[1:]
        sign = "+" if exp >= 0 else "-"
        out = mantissa + "E" + sign + str(abs(exp)).rjust(2, "0")

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
    if isinstance(v, bool):          # bool before int – bool is an int subclass
        return "true" if v else "false"
    if isinstance(v, str):
        return _quote(v)
    if isinstance(v, (int, float)):
        return _fmt_double(float(v))
    if isinstance(v, list):
        return "[" + ",".join(_canon(x) for x in v) + "]"
    keys = sorted(v.keys())
    return "{" + ",".join(_quote(k) + ":" + _canon(v[k]) for k in keys) + "}"


def _metric(node_id, label, value):
    # The current canonical Metric: `value` (the 0.2.0 value/source law), a
    # bare-string label (canonical Literal collapse), and every default omitted
    # (emphasis, format, tone, weight) - omit-when-default is part of the
    # canonical byte contract.
    return {
        "id": node_id,
        "kind": {
            "$type": "Metric",
            "label": label,
            "value": {"$type": "Static", "value": value},
        },
    }


def _model(h):
    return {
        "id": "rosetta-root",
        "kind": {
            "$type": "Box",
            "children": [
                {
                    "id": "rosetta-strip",
                    "kind": {
                        "$type": "Box",
                        "children": [
                            _metric("rosetta-m-a", h["labelA"], h["valueA"]),
                            _metric("rosetta-m-b", h["labelB"], h["valueB"]),
                            _metric("rosetta-m-c", h["labelC"], h["valueC"]),
                        ],
                        "layout": {"$type": "Flex", "direction": "Horizontal", "wrap": True},
                        "role": "Group",
                    },
                }
            ],
            "heading": "Revenue snapshot",
            "layout": {"$type": "Flex", "direction": "Vertical", "wrap": False},
            "role": "Dashboard",
        },
    }


def rosetta_encode(holes_json):
    h = json.loads(holes_json)
    wire = _canon(_model(h))
    digest = hashlib.sha256(wire.encode("utf-8")).hexdigest()
    return json.dumps({"wire": wire, "hash": digest})

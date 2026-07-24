# ============================================================================
#  The Relay – Python (CPython on WebAssembly, via Pyodide) as an independent
#  hasher for the cross-host parity seal.
#
#  It receives a station's canonical wire bytes and returns hashlib's SHA-256 –
#  a third, genuinely-independent implementation that must agree with the F#
#  managed digest and TypeScript's Web Crypto, byte for byte. No shared code
#  crosses the boundary, only the wire string.
# ============================================================================
import hashlib


def relay_sha256(wire: str) -> str:
    return hashlib.sha256(wire.encode("utf-8")).hexdigest()

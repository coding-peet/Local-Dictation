"""Lossless immutable text alternatives, with no cross-language offset arithmetic."""
from difflib import SequenceMatcher
import hashlib
import re

def compare(primary, verification):
    # Words, whitespace, and punctuation remain separate exact original strings.
    token = r"\w+|\s+|[^\w\s]"
    a, b = re.findall(token, primary), re.findall(token, verification)
    pieces = []
    for index, (kind, i, j, k, end) in enumerate(SequenceMatcher(None, a, b, autojunk=False).get_opcodes()):
        left, right = "".join(a[i:j]), "".join(b[k:end])
        digest = hashlib.sha256((str(index) + "\0" + left + "\0" + right).encode()).hexdigest()[:16]
        pieces.append({"id": digest, "primary": left, "verification": right,
                       "different": kind != "equal"})
    return pieces

def compose(pieces, choices):
    result = []
    for piece in pieces:
        choice = choices.get(piece["id"], "primary")
        if isinstance(choice, dict) and "custom" in choice:
            result.append(choice["custom"])
        else:
            if choice not in ("primary", "verification"):
                raise ValueError("Unknown comparison choice.")
            result.append(piece[choice])
    return "".join(result)

"""Small text helpers. See README.md for what each one promises."""

SMALL_WORDS = {"a", "an", "and", "of", "the"}


def slugify(s):
    out = []
    pending_dash = False
    for ch in s.lower():
        if ch.isascii() and ch.isalnum():
            if pending_dash and out:
                out.append("-")
            pending_dash = False
            out.append(ch)
        else:
            pending_dash = True
    return "".join(out)


def truncate(s, n):
    if len(s) < n:
        return s
    return s[: n - 1] + "…"


def wrap(s, width):
    lines = []
    current = ""
    for word in s.split():
        if current == "":
            current = word
        elif len(current) + 1 + len(word) <= width:
            current = current + " " + word
        else:
            lines.append(current)
            current = word
    if current != "":
        lines.append(current)
    return lines


def title_case(s):
    words = s.split()
    out = []
    for i, w in enumerate(words):
        lw = w.lower()
        if i > 0 and lw in SMALL_WORDS:
            out.append(lw)
        else:
            out.append(lw[:1].upper() + lw[1:])
    return " ".join(out)


def count_words(s):
    return len(s.split())

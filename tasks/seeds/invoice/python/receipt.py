"""Command line: python3 receipt.py ORDER.json"""
import sys

import invoice


def main(argv):
    if len(argv) != 1:
        print("Usage: receipt ORDER.json", file=sys.stderr)
        return 2
    try:
        with open(argv[0]) as f:
            text = f.read()
    except OSError as e:
        print(f"Cannot read {argv[0]}: {e.strerror}", file=sys.stderr)
        return 1
    try:
        order = invoice.parse_order(text)
    except (ValueError, KeyError, TypeError):
        print(f"{argv[0]} is not a valid order", file=sys.stderr)
        return 1
    try:
        lines = invoice.receipt(order)
    except invoice.UnknownCode as e:
        print(f"Unknown discount code: {e}", file=sys.stderr)
        return 2
    for line in lines:
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

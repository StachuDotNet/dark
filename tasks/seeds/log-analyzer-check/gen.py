"""Generate fixed-seed access logs and the expected report for the log-analyzer task.

Usage: python3 gen.py OUTDIR
Writes OUTDIR/access.log, OUTDIR/small.log and OUTDIR/expected.json.
Percentiles use the nearest-rank method: the value at rank ceil(p/100 * n), 1-based, of the sorted times.
"""
import datetime
import json
import math
import os
import random
import sys

PATHS = ["/", "/api/items", "/api/items/7", "/login", "/static/app.js", "/static/app.css",
         "/api/search", "/about", "/api/cart", "/checkout", "/api/users/me", "/favicon.ico",
         "/blog", "/blog/post-1", "/blog/post-2"]
WEIGHTS = [30, 25, 8, 6, 20, 20, 12, 3, 10, 4, 9, 15, 5, 2, 2]
STATUSES = [200] * 80 + [201, 204, 301, 302, 304] * 2 + [400, 401, 403, 404, 404, 404] + [500, 502, 503]
METHODS = ["GET"] * 8 + ["POST", "PUT"]
START = datetime.datetime(2026, 3, 10, 0, 0, 0, tzinfo=datetime.timezone.utc)


def make(rng, n, malformed):
    lines, recs = [], []
    bad_at = set(rng.sample(range(n), malformed))
    t = START
    for i in range(n):
        t = t + datetime.timedelta(seconds=rng.randint(0, 17))
        if i in bad_at:
            lines.append(rng.choice(["garbage line", '1.2.3.4 - - [bad date] "GET / HTTP/1.1" 200 1 1',
                                     '1.2.3.4 - - [10/Mar/2026:01:00:00 +0000] "GET /" 200', ""]) or "-")
            continue
        path = rng.choices(PATHS, WEIGHTS)[0]
        q = f"?id={rng.randint(1, 99)}" if rng.random() < 0.3 else ""
        status = rng.choice(STATUSES)
        ms = int(rng.lognormvariate(3.5, 0.8))
        ip = f"203.0.113.{rng.randint(1, 254)}"
        stamp = t.strftime("%d/%b/%Y:%H:%M:%S +0000")
        lines.append(f'{ip} - - [{stamp}] "{rng.choice(METHODS)} {path}{q} HTTP/1.1" {status} {rng.randint(100, 9000)} {ms}')
        recs.append({"t": t, "path": path, "status": status, "ms": ms})
    return lines, recs


def pct(values, p):
    s = sorted(values)
    return s[max(1, math.ceil(p / 100 * len(s))) - 1] if s else None


def report(recs, malformed):
    paths = {}
    for r in recs:
        paths.setdefault(r["path"], []).append(r["ms"])
    top = sorted(paths.items(), key=lambda kv: (-len(kv[1]), kv[0]))
    hours = {}
    for r in recs:
        hours[r["t"].hour] = hours.get(r["t"].hour, 0) + 1
    classes = {}
    for r in recs:
        k = f"{r['status'] // 100}xx"
        classes[k] = classes.get(k, 0) + 1
    allms = [r["ms"] for r in recs]
    return {
        "requests": len(recs), "malformed": malformed, "status_classes": classes,
        "top_paths": [[p, len(v)] for p, v in top[:10]],
        "per_hour": {str(h): c for h, c in sorted(hours.items())},
        "median_ms": pct(allms, 50), "p95_ms": pct(allms, 95),
        "top5_latency": {p: {"median_ms": pct(v, 50), "p95_ms": pct(v, 95)} for p, v in top[:5]},
    }


def main(out):
    os.makedirs(out, exist_ok=True)
    rng = random.Random(4021)
    big, big_recs = make(rng, 10000, 20)
    small, small_recs = make(rng, 200, 0)
    for name, lines in (("access.log", big), ("small.log", small)):
        with open(os.path.join(out, name), "w") as f:
            f.write("\n".join(lines) + "\n")
    lo = datetime.datetime(2026, 3, 10, 14, 0, 0, tzinfo=datetime.timezone.utc)
    hi = datetime.datetime(2026, 3, 10, 15, 0, 0, tzinfo=datetime.timezone.utc)
    window = [r for r in big_recs if lo <= r["t"] <= hi]
    expected = {
        "access.log": report(big_recs, 20),
        "access.log --since 2026-03-10T14:00:00 --until 2026-03-10T15:00:00 (malformed not counted)": report(window, 0),
        "access.log + small.log": report(big_recs + small_recs, 20),
    }
    with open(os.path.join(out, "expected.json"), "w") as f:
        json.dump(expected, f, indent=2)


if __name__ == "__main__":
    main(sys.argv[1])

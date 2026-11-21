"""The lsp gate's checks: `dark lsp` driven over stdio the way an editor drives it.

    python3 scripts/testing/_lsp-gate.py <cli>

Run by `scripts/testing/gates lsp`, which gives it a throwaway home with its own store.
"""

import json
import os
import sys
import queue
import subprocess
import threading
import time


class Server:
    def __init__(self, cli, env=None):
        self.proc = subprocess.Popen(
            [cli, "lsp", "--stdio"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=env or os.environ.copy(),
        )
        self.messages = queue.Queue()
        self.seen = []
        self.next_id = 1
        self.stray = bytearray()
        self.log = bytearray()
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()

    def _read_stdout(self):
        out = self.proc.stdout
        while True:
            header = out.readline()
            if not header:
                return
            if not header.startswith(b"Content-Length:"):
                self.stray += header
                continue
            length = int(header.split(b":")[1])
            if out.readline() not in (b"\r\n", b"\n"):
                self.stray += b"<no blank line after the header>"
            self.messages.put(json.loads(out.read(length)))

    def _read_stderr(self):
        for line in self.proc.stderr:
            self.log += line

    def send(self, message):
        message = {"jsonrpc": "2.0", **message}
        body = json.dumps(message).encode()
        self.proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(body) + body)
        self.proc.stdin.flush()

    def notify(self, method, params=None):
        self.send({"method": method, "params": params or {}})

    def request(self, method, params=None, timeout=60):
        rid = self.next_id
        self.next_id += 1
        self.send({"id": rid, "method": method, "params": params or {}})
        return self.wait(lambda m: m.get("id") == rid and "method" not in m, timeout, f"a reply to {method}")

    def wait(self, pred, timeout, what):
        for m in self.seen:
            if pred(m):
                self.seen.remove(m)
                return m
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                m = self.messages.get(timeout=max(0.01, deadline - time.time()))
            except queue.Empty:
                break
            if pred(m):
                return m
            self.seen.append(m)
        raise Failure(f"no {what} within {timeout}s")

    def diagnostics(self, uri, timeout=60):
        m = self.wait(
            lambda m: m.get("method") == "textDocument/publishDiagnostics" and m["params"]["uri"] == uri,
            timeout,
            f"diagnostics for {uri}",
        )
        return m["params"]["diagnostics"]

    def quiet(self, seconds):
        """Every message that arrives in the next <seconds>."""
        time.sleep(seconds)
        got = list(self.seen)
        self.seen.clear()
        while not self.messages.empty():
            got.append(self.messages.get())
        return got

    def open(self, uri, text):
        self.notify(
            "textDocument/didOpen",
            {"textDocument": {"uri": uri, "languageId": "darklang", "version": 1, "text": text}},
        )

    def change(self, uri, version, text):
        self.notify(
            "textDocument/didChange",
            {"textDocument": {"uri": uri, "version": version}, "contentChanges": [{"text": text}]},
        )

    def close_stdin(self):
        self.proc.stdin.close()

    def exit_code(self, timeout=30):
        try:
            return self.proc.wait(timeout)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            raise Failure(f"the server was still running {timeout}s after it should have stopped")

    def kill(self):
        if self.proc.poll() is None:
            self.proc.kill()


class Failure(Exception):
    pass


# --- the checks ---


def check(ok, message):
    if not ok:
        raise Failure(message)


def messages_of(diagnostics):
    return [d["message"] for d in diagnostics]


def run(cli):
    checked = []

    # The default policy a fresh install has: no grants, and the server must still answer.
    s = Server(cli)
    try:
        init = s.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
        caps = init["result"]["capabilities"]
        check(caps["textDocumentSync"]["change"] == 1, f"unexpected sync kind: {caps}")
        s.notify("initialized")

        # A syntax error is an error on its line.
        uri = "file:///tmp/lsp-gate/syntax.dark"
        s.open(uri, "let f(x: Int64): Int64 =\n  x +\n")
        d = s.diagnostics(uri)
        check(any(x["severity"] == 1 for x in d), f"no error for a syntax error: {d}")
        checked.append("a syntax error is reported as an error")

        # A name that resolves to nothing is a warning at the name.
        uri = "file:///tmp/lsp-gate/names.dark"
        s.open(uri, "let f(): Int64 = LspGate.T.later()\n")
        d = s.diagnostics(uri)
        check(
            any(x["severity"] == 2 and "LspGate.T.later" in x["message"] for x in d),
            f"no warning for an unresolved name: {d}",
        )
        checked.append("an unresolved name is a warning")

        # Hot reload: another process defines the name, and the open document's warning goes,
        # with no edit to the document.
        defined = subprocess.run(
            [cli, "fn", "/LspGate.T.later", "(): Int64 = 1"], capture_output=True, text=True
        )
        check(defined.returncode == 0, f"could not define LspGate.T.later: {defined.stdout}{defined.stderr}")
        d = s.diagnostics(uri, timeout=60)
        check(
            not any("LspGate.T.later" in m for m in messages_of(d)),
            f"the warning stayed after the name was defined elsewhere: {d}",
        )
        checked.append("a name defined in another process clears its warning (store change)")

        # A type error comes from the at-rest checker.
        uri = "file:///tmp/lsp-gate/types.dark"
        s.open(uri, 'let f(x: Int64): String = x + 1\n')
        d = s.diagnostics(uri)
        check(any(x["severity"] == 1 and x.get("code") for x in d), f"no checker error: {d}")
        checked.append("a type error is reported with the checker's code")

        # Typing: ten keystrokes in a burst are one analysis, not ten.
        uri = "file:///tmp/lsp-gate/typing.dark"
        s.open(uri, "let double(x: Int64): Int64 = x * 2\n")
        s.diagnostics(uri)
        for v in range(2, 12):
            s.change(uri, v, "let double(x: Int64): Int64 = x * 2" + " " * v + "\n")
        publishes = [
            m
            for m in s.quiet(5)
            if m.get("method") == "textDocument/publishDiagnostics" and m["params"]["uri"] == uri
        ]
        check(len(publishes) == 1, f"ten keystrokes gave {len(publishes)} analyses, not one")
        checked.append("ten keystrokes, one analysis")

        # Non-ASCII text: Content-Length counts bytes, so the message after it must still parse.
        uri = "file:///tmp/lsp-gate/utf8.dark"
        s.open(uri, "// café -> naïve über\nlet x(): Int64 = 1\n")
        s.diagnostics(uri)
        reply = s.request("dark/noSuchMethod")
        check(reply.get("error", {}).get("code") == -32601, f"an unknown request was not refused: {reply}")
        checked.append("non-ASCII text keeps the stream in step; an unknown request gets -32601")

        # Closing clears the squiggles.
        s.notify("textDocument/didClose", {"textDocument": {"uri": uri}})
        check(s.diagnostics(uri) == [], "closing a document did not clear its diagnostics")

        reply = s.request("shutdown")
        check("result" in reply and reply["result"] is None, f"shutdown: {reply}")
        s.notify("exit")
        check(s.exit_code() == 0, "exit after shutdown did not exit 0")
        check(not s.stray, f"stdout carried something other than the protocol: {bytes(s.stray)[:200]!r}")
        checked.append("shutdown then exit stops with 0; stdout carries only the protocol")
    finally:
        s.kill()

    # The editor going away without a word: stdin closes, and the server stops.
    s = Server(cli)
    try:
        s.request("initialize", {"capabilities": {}})
        s.close_stdin()
        check(s.exit_code() == 1, "closing stdin without shutdown did not stop the server with 1")
        checked.append("stdin closing stops the server")
    finally:
        s.kill()

    return checked


if __name__ == "__main__":
    try:
        done = run(sys.argv[1])
    except Failure as e:
        print(f"FAIL: {e}", file=sys.stderr)
        sys.exit(1)
    print(f"lsp: {len(done)} checks: " + "; ".join(done) + ".")

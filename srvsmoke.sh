#!/bin/bash
# smoke: run --allow covers a server's bind and its handler's file read; serve binds its own port
B=${1:-/home/stachu/code/dark/bench-loop.bench/bins/w1/darklang}
d=$(mktemp -d); export HOME=$d DARK_CONFIG_RUNDIR=$d/.darklang; cd $d
$B version > /dev/null 2>&1
echo hello > $d/data.txt
$B import - /Me.Srv <<D > /dev/null
let fileFn(req: Stdlib.Http.Request): Stdlib.Http.Response =
  match Stdlib.Cli.FileSystem.readFileAsString("$d/data.txt") with
  | Ok(s) -> Stdlib.Http.responseWithText(200, s)
  | Error(e) -> Stdlib.Http.responseWithText(500, Stdlib.inspect(e))

val handlers = [ Stdlib.HttpServer.Handler { route = "/f", method = "GET", handler = fileFn } ]

let router(req: Stdlib.Http.Request): Stdlib.Http.Response =
  Stdlib.HttpServer.routeRequest(handlers, req)

let main(): Int64 =
  match Stdlib.Net.Port.fromInt64(18771) with
  | Error(_) -> 1
  | Ok(port) ->
    match Stdlib.HttpServer.serve(Stdlib.HttpServer.Config.defaults(port), router, fun () -> Stdlib.printLine("up")) with
    | Ok(_) -> 0
    | Error(e) ->
      Stdlib.printErrorLine(e)
      1
D
$B commit "srv" -y > /dev/null 2>&1
timeout 8 $B run --allow "http-server 127.0.0.1 18771; file read $d" Me.Srv.main > $d/run.log 2>&1 &
sleep 5; echo "run --allow: $(curl -s --noproxy '*' http://127.0.0.1:18771/f)"; wait; head -3 $d/run.log
timeout 8 $B serve Me.Srv.router --port 18772 > $d/serve.log 2>&1 &
sleep 5; echo "serve: $(curl -s --noproxy '*' -o /dev/null -w '%{http_code}' http://127.0.0.1:18772/f)"; wait; grep -iE "listening|denied|not allowed" $d/serve.log | head -3
rm -rf $d

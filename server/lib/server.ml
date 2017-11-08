open Core
open Lwt

module Clu = Cohttp_lwt_unix
module C = Cohttp
module S = Clu.Server
module Request = Clu.Request
module Header = C.Header
module G = Graph
module RT = Runtime

let server =
  let stop,stopper = Lwt.wait () in

  let callback _ req req_body =

    let admin_rpc_handler body (host: string) (save: bool) : string =
      let time = Unix.gettimeofday () in
      let body = Log.pp "request body" body ~f:ident in
      let g = G.load host [] in
      try
        let ops = Api.to_ops body in
        g := !(G.load host ops);
        let result = Graph.to_frontend_string !g in
        let total = string_of_float (1000.0 *. (Unix.gettimeofday () -. time)) in
        Log.pP ~stop:2000 ~f:ident ("response (" ^ total ^ "ms):") result;
        (* work out the result before we save it, incase it has a stackoverflow
         * or other crashing bug *)
        if save then G.save !g;
        result
      with
      | e ->
        let bt = Exn.backtrace () in
        let msg = Exn.to_string e in
        print_endline (G.show_graph !g);
        print_endline ("Exception: " ^ msg);
        print_endline bt;
        raise e
    in

    let admin_ui_handler () =
      let template = Util.readfile_lwt "templates/ui.html" in
      template >|= Util.string_replace "ALLFUNCTIONS" (Api.functions)
    in

    let static_handler uri =
      let fname = S.resolve_file ~docroot:"." ~uri in
      S.respond_file ~fname ()
    in

    let save_test_handler host =
      let g = G.load host [] in
      let filename = G.save_test !g in
      S.respond_string ~status:`OK ~body:("Saved as: " ^ filename) ()
    in

    let form_parser form =
      form |> Uri.query_of_encoded |> RT.query_to_dval
    in

    let user_page_handler (host: string) (verb: C.Code.meth) (body: string) (uri: Uri.t) (ctype: string) =
      let g = G.load host [] in
      let gfns = G.gfns !g in
      let is_get = C.Code.method_of_string "GET" = verb in
      let body_parser =
        match ctype with
        | "application/json" -> RT.parse
        | "application/x-www-form-urlencoded" -> form_parser
        | _ -> RT.parse in
      let pages = Http.pages_matching_route ~uri:uri !g in
      match pages with
      | [] ->
        S.respond_string ~status:`Not_found ~body:"404: No page matches" ()
      | [page] ->
        (* TODO: there's a bunch of intermingled concerns in here, which
         * is probably a smell of of how hacky our Page/DB features are. *)
        let route = Http.url_for_exn !g page in
        let body =
          let body_dval =
            if body = ""
            then RT.DNull
            else body_parser body in
          let uri_dval = RT.query_to_dval (Uri.query uri) in
          let scope_dval = RT.obj_merge body_dval uri_dval in
          let scope = RT.Scope.singleton page#id scope_dval in
          let result =
            if is_get
            then
              if Http.has_route_variables route
              then
                let (model, rpm) = Http.bind_route_params_exn ~uri:uri ~route:route in
                let id =
                  match Map.find rpm "id" with
                  | Some s -> s
                  | None -> Exception.internal "We only support :id url params rn" in
                Libdb.kv_fetch model id
              else
                G.run_output !g page
            (* Posts have values, I guess we should be getting the result from it *)
            else (G.run_input !g scope page; DStr "") in
          RT.to_url_string result
        in

        if is_get
        then S.respond_string ~status:`OK ~body:body ()
        else let redir = page#get_arg_value gfns "redir" in
          (match redir with
          | DStr "" | DNull -> S.respond_string ~status:`OK ~body:body ()
          | DStr s  -> S.respond_redirect (Uri.of_string s) ()
          | _       -> S.respond_string ~status:`Internal_server_error ~body:"500: Type error in `redir` of Page::POST" ())
      | _ ->
        S.respond_string ~status:`Internal_server_error ~body:"500: More than one page matches" ()
    in

    (* let auth_handler handler *)
    (*   = match auth with *)
    (*   | (Some `Basic ("dark", "eapnsdc")) *)
    (*     -> handler *)
    (*   | _ *)
    (*     -> Cohttp_lwt_unix.Server.respond_need_auth ~auth:(`Basic "dark") () *)
    (* in *)
    (*  *)
    let route_handler _ =
      req_body |> Cohttp_lwt_body.to_string >>=
      (fun req_body ->
         try
           let uri = req |> Request.uri in
           let verb = req |> Request.meth in
           let headers = req |> Request.headers in
           let content_type =
             match Header.get headers "content-type" with
             | None -> "unknown"
             | Some v -> v
           in
           (* let auth = req |> Request.headers |> Header.get_authorization in *)

           let domain = Uri.host uri |> Option.value ~default:"" in
           let domain = match String.split domain '.' with
           | ["localhost"] -> "localhost"
           | a :: rest -> a
           | _ -> failwith @@ "Unsupported domain: " ^ domain in

           Log.pP "req: " (domain, C.Code.string_of_method verb, uri);

           match (Uri.path uri) with
           | "/admin/api/rpc" ->
             S.respond_string ~status:`OK
                              ~body:(admin_rpc_handler req_body domain true) ()
           | "/admin/api/phantom" ->
             S.respond_string ~status:`OK
                              ~body:(admin_rpc_handler req_body domain false) ()
           | "/sitemap.xml" ->
             S.respond_string ~status:`OK ~body:"" ()
           | "/favicon.ico" ->
             S.respond_string ~status:`OK ~body:"" ()
           | "/admin/api/shutdown" ->
             Lwt.wakeup stopper ();
             S.respond_string ~status:`OK ~body:"Disembowelment" ()
           | "/admin/ui" ->
             admin_ui_handler () >>= fun body -> S.respond_string ~status:`OK ~body ()
           | "/admin/test" ->
             static_handler (Uri.of_string "/templates/test.html")
           | "/admin/api/save_test" ->
             save_test_handler domain
           | p when (String.length p) < 8 ->
             user_page_handler domain verb req_body uri content_type
           | p when (String.equal (String.sub p ~pos:0 ~len:8) "/static/") ->
             static_handler uri
           | _ ->
             user_page_handler domain verb req_body uri content_type
         with
         | e ->
           let backtrace = Exn.backtrace () in
           let body = match e with
             | Exception.DarkException e ->
                 Exception.exception_data_to_yojson e |> Yojson.Safe.pretty_to_string
             | Yojson.Json_error msg -> "Not a value: " ^ msg
             | _ -> "Dark Internal Error: " ^ Exn.to_string e
           in
           Lwt_io.printl ("Error: " ^ body) >>= fun () ->
           Lwt_io.printl backtrace >>= fun () ->
           S.respond_string ~status:`Internal_server_error ~body ())
    in
    ()
    |> route_handler
    (* |> auth_handler *)
  in
  S.create ~stop ~mode:(`TCP (`Port 8000)) (S.make ~callback ())

let run () = ignore (Lwt_main.run server)

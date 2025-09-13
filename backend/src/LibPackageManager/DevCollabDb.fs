/// Database operations for developer collaboration
module LibPackageManager.DevCollabDb

open System.Threading.Tasks
open FSharp.Control.Tasks
open Microsoft.Data.Sqlite
open Fumble
open LibDB.Db

open Prelude
open LibPackageManager.DevCollab

// Database schema initialization
let initSchema () : Task<unit> =
  task {
    do!
      Sql.query """
        CREATE TABLE IF NOT EXISTS collab_users (
          id TEXT PRIMARY KEY,
          username TEXT UNIQUE NOT NULL,
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        );
        
        INSERT OR IGNORE INTO collab_users (id, username) VALUES 
          ('1', 'stachu'),
          ('2', 'ocean');
          
        CREATE TABLE IF NOT EXISTS collab_patches (
          id TEXT PRIMARY KEY,
          author_id TEXT NOT NULL REFERENCES collab_users(id),
          intent TEXT NOT NULL,
          ops_json TEXT NOT NULL,
          dependencies_json TEXT DEFAULT '[]',
          created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
          updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
          status TEXT NOT NULL CHECK(status IN ('draft', 'ready', 'applied', 'rejected')),
          todos_json TEXT DEFAULT '[]',
          validation_errors_json TEXT DEFAULT '[]'
        );
        
        CREATE TABLE IF NOT EXISTS collab_sessions (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          intent TEXT NOT NULL,
          owner_id TEXT NOT NULL REFERENCES collab_users(id),
          patches_json TEXT DEFAULT '[]',
          current_patch_id TEXT,
          started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
          last_active_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
          state TEXT NOT NULL CHECK(state IN ('active', 'suspended', 'completed')),
          context_json TEXT DEFAULT '{}'
        );
        
        CREATE TABLE IF NOT EXISTS collab_sync_state (
          instance_id TEXT PRIMARY KEY,
          user_id TEXT NOT NULL REFERENCES collab_users(id),
          last_sync_at TIMESTAMP,
          pending_patches_json TEXT DEFAULT '[]',
          server_url TEXT NOT NULL DEFAULT 'dev.darklang.com'
        );
        
        INSERT OR IGNORE INTO collab_sync_state (instance_id, user_id, server_url) VALUES 
          ('dev-instance-1', '1', 'dev.darklang.com');
      """
      |> Sql.executeNonQueryAsync
      |> Task.map ignore<int>
  }

// User operations
let getUserByUsername (username: string) : Task<UserId option> =
  task {
    let! users =
      Sql.query "SELECT id FROM collab_users WHERE username = @username"
      |> Sql.parameters [ "@username", Sql.string username ]
      |> Sql.executeAsync (fun read -> read.string "id")
    
    match users with
    | Ok [user] -> return Some user
    | _ -> return None
  }

let getCurrentUser () : Task<UserId option> =
  // For now, return hardcoded user - in production this would check auth state
  Task.FromResult(Some "1")

// Patch operations
let savePatch (patch: Patch) : Task<unit> =
  task {
    let opsJson = 
      patch.ops 
      |> List.map (fun op ->
        match op with
        | AddFunction(id, name, impl, sig_) -> 
          $"""{{ "type": "AddFunction", "id": "{id}", "name": "{name}", "impl": "...", "signature": "..." }}"""
        | UpdateFunction(id, impl, version) ->
          $"""{{ "type": "UpdateFunction", "id": "{id}", "impl": "...", "version": {version} }}"""
        | _ -> """{ "type": "Other" }""")
      |> String.concat ","
      |> fun ops -> $"[{ops}]"
    
    let dependenciesJson = 
      patch.dependencies 
      |> Set.toList 
      |> List.map (fun id -> $"\"{id}\"")
      |> String.concat ","
      |> fun deps -> $"[{deps}]"
      
    let todosJson = 
      patch.todos
      |> List.map (fun todo -> $"\"{todo}\"")
      |> String.concat ","
      |> fun todos -> $"[{todos}]"
      
    let errorsJson = 
      patch.validationErrors
      |> List.map (fun err -> $"\"{err}\"")
      |> String.concat ","
      |> fun errs -> $"[{errs}]"
    
    do!
      Sql.query """
        INSERT OR REPLACE INTO collab_patches 
        (id, author_id, intent, ops_json, dependencies_json, created_at, updated_at, status, todos_json, validation_errors_json)
        VALUES (@id, @author_id, @intent, @ops_json, @dependencies_json, @created_at, @updated_at, @status, @todos_json, @validation_errors_json)
      """
      |> Sql.parameters [
          "@id", Sql.string (patch.id.ToString())
          "@author_id", Sql.string patch.author
          "@intent", Sql.string patch.intent
          "@ops_json", Sql.string opsJson
          "@dependencies_json", Sql.string dependenciesJson
          "@created_at", Sql.timestamp patch.createdAt
          "@updated_at", Sql.timestamp patch.updatedAt
          "@status", Sql.string (match patch.status with
                                | Draft -> "draft"
                                | Ready -> "ready" 
                                | Applied -> "applied"
                                | Rejected -> "rejected")
          "@todos_json", Sql.string todosJson
          "@validation_errors_json", Sql.string errorsJson
        ]
      |> Sql.executeNonQueryAsync
      |> Task.map ignore<int>
  }

let loadPatches () : Task<List<Patch>> =
  task {
    let! patches =
      Sql.query """
        SELECT id, author_id, intent, ops_json, dependencies_json, created_at, updated_at, status, todos_json, validation_errors_json
        FROM collab_patches 
        ORDER BY created_at DESC
      """
      |> Sql.executeAsync (fun read -> 
        { id = System.Guid.Parse(read.string "id")
          author = read.string "author_id"
          intent = read.string "intent"
          ops = [] // TODO: Parse ops_json
          dependencies = Set.empty // TODO: Parse dependencies_json
          createdAt = read.dateTime "created_at"
          updatedAt = read.dateTime "updated_at"
          status = match read.string "status" with
                   | "draft" -> Draft
                   | "ready" -> Ready
                   | "applied" -> Applied
                   | "rejected" -> Rejected
                   | _ -> Draft
          todos = [] // TODO: Parse todos_json
          validationErrors = [] }) // TODO: Parse validation_errors_json
    
    match patches with
    | Ok patchList -> return patchList
    | Error _ -> return []
  }

let loadPatchById (patchId: PatchId) : Task<Patch option> =
  task {
    let! patches =
      Sql.query """
        SELECT id, author_id, intent, ops_json, dependencies_json, created_at, updated_at, status, todos_json, validation_errors_json
        FROM collab_patches 
        WHERE id = @id
      """
      |> Sql.parameters [ "@id", Sql.string (patchId.ToString()) ]
      |> Sql.executeAsync (fun read -> 
        { id = System.Guid.Parse(read.string "id")
          author = read.string "author_id"
          intent = read.string "intent"
          ops = [] // TODO: Parse ops_json
          dependencies = Set.empty // TODO: Parse dependencies_json
          createdAt = read.dateTime "created_at"
          updatedAt = read.dateTime "updated_at"
          status = match read.string "status" with
                   | "draft" -> Draft
                   | "ready" -> Ready
                   | "applied" -> Applied
                   | "rejected" -> Rejected
                   | _ -> Draft
          todos = [] // TODO: Parse todos_json
          validationErrors = [] }) // TODO: Parse validation_errors_json
    
    match patches with
    | Ok [patch] -> return Some patch
    | _ -> return None
  }

// Session operations  
let saveSession (session: Session) : Task<unit> =
  task {
    let patchesJson = 
      session.patches
      |> List.map (fun id -> $"\"{id}\"")
      |> String.concat ","
      |> fun patches -> $"[{patches}]"
      
    let contextJson = 
      $"""{{ "currentLocation": "{session.context.currentLocation |> Option.defaultValue ""}", "openFiles": [], "notes": "{session.context.notes}" }}"""
    
    do!
      Sql.query """
        INSERT OR REPLACE INTO collab_sessions 
        (id, name, intent, owner_id, patches_json, current_patch_id, started_at, last_active_at, state, context_json)
        VALUES (@id, @name, @intent, @owner_id, @patches_json, @current_patch_id, @started_at, @last_active_at, @state, @context_json)
      """
      |> Sql.parameters [
          "@id", Sql.string (session.id.ToString())
          "@name", Sql.string session.name
          "@intent", Sql.string session.intent
          "@owner_id", Sql.string session.owner
          "@patches_json", Sql.string patchesJson
          "@current_patch_id", Sql.stringOrNone (session.currentPatch |> Option.map (fun id -> id.ToString()))
          "@started_at", Sql.timestamp session.startedAt
          "@last_active_at", Sql.timestamp session.lastActiveAt
          "@state", Sql.string (match session.state with
                                | Active -> "active"
                                | Suspended -> "suspended"
                                | Completed -> "completed")
          "@context_json", Sql.string contextJson
        ]
      |> Sql.executeNonQueryAsync
      |> Task.map ignore<int>
  }

let loadCurrentSession (userId: UserId) : Task<Session option> =
  task {
    let! sessions =
      Sql.query """
        SELECT id, name, intent, owner_id, patches_json, current_patch_id, started_at, last_active_at, state, context_json
        FROM collab_sessions 
        WHERE owner_id = @owner_id AND state = 'active'
        ORDER BY last_active_at DESC
        LIMIT 1
      """
      |> Sql.parameters [ "@owner_id", Sql.string userId ]
      |> Sql.executeAsync (fun read -> 
        { id = System.Guid.Parse(read.string "id")
          name = read.string "name"
          intent = read.string "intent"
          owner = read.string "owner_id"
          patches = [] // TODO: Parse patches_json
          currentPatch = read.stringOrNone "current_patch_id" |> Option.map System.Guid.Parse
          startedAt = read.dateTime "started_at"
          lastActiveAt = read.dateTime "last_active_at"
          state = match read.string "state" with
                  | "active" -> Active
                  | "suspended" -> Suspended
                  | "completed" -> Completed
                  | _ -> Active
          context = { currentLocation = None; openFiles = []; notes = "" } }) // TODO: Parse context_json
    
    match sessions with
    | Ok [session] -> return Some session
    | _ -> return None
  }
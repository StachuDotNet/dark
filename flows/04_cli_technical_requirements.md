# CLI Technical Requirements to Support Developer Flows

Based on the developer flows we've designed, here are the specific technical changes needed to support them in the CLI:

---

## 1. Matter Integration Layer

### Core Matter Operations API
**Current State**: Types exist in ProgramTypes.fs, but no implementation  
**Needed**: Complete implementation of Matter operations

```fsharp
// In LibMatter.fs (new module)
module LibMatter

// Content hashing
let hashContent (content: string) (contentType: ContentType) : string
let validateHash (content: string) (hash: string) : bool

// Op execution  
let executeOp (op: Op.T) : Ply<Result<unit, string>>
let executeOps (ops: List<Op.T>) : Ply<Result<unit, string>>

// Session management
let createSession (name: string) (basePatch: uuid option) : Ply<Result<Session.T, string>>
let switchSession (sessionId: uuid) : Ply<Result<Session.T, string>>
let getCurrentSession () : Ply<Option<Session.T>>
let listSessions () : Ply<List<Session.T>>

// Patch operations
let createPatch (ops: List<Op.T>) (metadata: Patch.Metadata) : Ply<Result<Patch.T, string>>
let applyPatch (patchId: uuid) : Ply<Result<unit, string>>
let validatePatch (patch: Patch.T) : Ply<Result<unit, ValidationError>>

// Content resolution
let resolveLocation (location: PackageLocation.T) : Ply<Option<string * ContentType>>
let getContentByHash (hash: string) : Ply<Option<Content>>
```

### Database Integration
**Current State**: Schema exists, but no queries  
**Needed**: Complete CRUD operations for all Matter tables

```fsharp
// In LibMatter.Database.fs
module LibMatter.Database

// Content operations
let insertContent (hash: string) (contentType: ContentType) (content: bytes) : Ply<Result<unit, string>>
let getContent (hash: string) : Ply<Option<Content>>
let contentExists (hash: string) : Ply<bool>

// Name resolution operations  
let createName (location: PackageLocation.T) (hash: string) : Ply<Result<unit, string>>
let updateNamePointer (location: PackageLocation.T) (newHash: string) : Ply<Result<unit, string>>
let resolveName (location: PackageLocation.T) : Ply<Option<string>>
let deleteName (location: PackageLocation.T) : Ply<Result<unit, string>>

// Session operations
let insertSession (session: Session.T) : Ply<Result<unit, string>>
let updateSession (session: Session.T) : Ply<Result<unit, string>>
let getSession (sessionId: uuid) : Ply<Option<Session.T>>
let listSessionsByStatus (status: Session.Status) : Ply<List<Session.T>>

// Patch operations
let insertPatch (patch: Patch.T) : Ply<Result<unit, string>>
let getPatch (patchId: uuid) : Ply<Option<Patch.T>>
let listPatches () : Ply<List<Patch.T>>
let insertPatchOp (patchId: uuid) (sequenceNum: int) (op: Op.T) : Ply<Result<unit, string>>
```

---

## 2. Enhanced CLI Commands

### Session Management Commands
**Current State**: No session commands  
**Needed**: Complete session workflow

```fsharp
// In packages/darklang/cli/matter/sessions.dark
module Darklang.Cli.Matter.Sessions

let execute (state: AppState) (args: List<String>) : AppState =
  match args with
  | ["new"; name] ->
    // dark session new feature-auth
    let result = LibMatter.createSession name None
    match result with
    | Ok session ->
      Stdlib.printLine $"✓ Created session: {name}"
      Stdlib.printLine $"✓ Session ID: {session.id}"
      // Update CLI state to track current session
      { state with currentSession = Some session }
    | Error err ->
      Stdlib.printLine $"Error creating session: {err}"
      state
      
  | ["list"] ->
    // dark session list
    let sessions = LibMatter.listSessions ()
    Stdlib.printLine "Available sessions:"
    sessions
    |> Stdlib.List.iter (fun s ->
      let marker = if Some s.id == state.currentSession.map(fun cs -> cs.id) then " ← current" else ""
      let status = Session.statusToString s.status
      Stdlib.printLine $"  {s.name} ({status}){marker}")
    state
    
  | ["switch"; name] ->
    // dark session switch feature-auth
    // TODO: Find session by name, switch to it
    state
    
  | ["status"] ->
    // dark session status  
    match state.currentSession with
    | Some session ->
      Stdlib.printLine $"Current session: {session.name}"
      Stdlib.printLine $"Status: {Session.statusToString session.status}"
      Stdlib.printLine $"Created: {session.createdAt}"
      // TODO: Show change count, last activity
    | None ->
      Stdlib.printLine "No active session"
    state
    
  | _ ->
    Stdlib.printLine "Usage: session [new|list|switch|status] [args...]"
    state
```

### Matter-Aware Function Creation
**Current State**: No function creation commands  
**Needed**: Commands that create both content and name pointers

```fsharp
// In packages/darklang/cli/matter/functions.dark
module Darklang.Cli.Matter.Functions

let execute (state: AppState) (args: List<String>) : AppState =
  match args with
  | ["create"; owner; module_; name; description] ->
    // dark fn create MyApp Auth login "User login handler"
    let location = PackageLocation.T { owner = owner; modules = [module_]; name = name }
    
    // Create empty function template
    let template = createFunctionTemplate description
    let hash = LibMatter.hashContent template ContentType.Function
    
    // Create Matter operations
    let ops = [
      Op.AddFunctionContent(hash, template)
      Op.CreateName(location, hash)
    ]
    
    match LibMatter.executeOps ops with
    | Ok _ ->
      Stdlib.printLine $"✓ Function created: {owner}.{module_}.{name}"
      Stdlib.printLine $"✓ Content hash: {hash}"
      Stdlib.printLine $"✓ Location: {formatLocation location}"
      
      // Open editor if available
      Stdlib.printLine "Opening in editor..."
      // TODO: Launch editor with content
      
    | Error err ->
      Stdlib.printLine $"Error creating function: {err}"
    
    state
    
  | ["hash"; name] ->
    // dark fn hash MyApp.Auth.login
    let location = parseLocation name
    match LibMatter.resolveName location with
    | Some hash ->
      Stdlib.printLine $"Function: {name}"
      Stdlib.printLine $"Hash: {hash}"
    | None ->
      Stdlib.printLine $"Function not found: {name}"
    state
    
  | ["edit"; name] ->
    // dark fn edit MyApp.Auth.login
    let location = parseLocation name
    match LibMatter.resolveName location with
    | Some hash ->
      match LibMatter.getContentByHash hash with
      | Some content ->
        // Launch editor with content
        launchEditor location content
      | None ->
        Stdlib.printLine $"Content not found for hash: {hash}"
    | None ->
      Stdlib.printLine $"Function not found: {name}"
    state
```

### Project Scaffolding
**Current State**: No project creation  
**Needed**: Template-based project creation

```fsharp
// In packages/darklang/cli/project/new.dark
module Darklang.Cli.Project.New

type ProjectTemplate = {
  name: String
  description: String  
  files: List<TemplateFile>
  dependencies: List<String>
}

type TemplateFile = {
  virtualPath: String  // e.g., "src/handlers/auth.dark"
  location: PackageLocation.T
  content: String
}

let webappTemplate : ProjectTemplate = {
  name = "webapp"
  description = "Web application with HTTP handlers"
  files = [
    { virtualPath = "src/handlers/main.dark"
      location = { owner = "{{PROJECT_NAME}}"; modules = ["Handlers"]; name = "main" }
      content = "// Main HTTP handler\nlet handler (req: HttpRequest) : HttpResponse = ..." }
    // ... more template files
  ]
  dependencies = ["Darklang.HTTP"; "Darklang.JSON"]
}

let execute (state: AppState) (args: List<String>) : AppState =
  match args with
  | [templateName; projectName] ->
    // dark new webapp my-blog
    let template = getTemplate templateName
    
    // Create new session for project
    match LibMatter.createSession $"{projectName}-main" None with
    | Ok session ->
      // Create all template files as Matter content
      let ops = 
        template.files
        |> Stdlib.List.map (fun file ->
          let content = substituteTemplateVars file.content projectName
          let hash = LibMatter.hashContent content ContentType.Function
          [
            Op.AddFunctionContent(hash, content)
            Op.CreateName(file.location, hash)
          ])
        |> Stdlib.List.flatten
      
      match LibMatter.executeOps ops with
      | Ok _ ->
        Stdlib.printLine $"✓ Created {templateName} project: {projectName}"
        Stdlib.printLine $"✓ Session: {session.name}"
        Stdlib.printLine $"✓ Files created: {Stdlib.List.length template.files}"
        
        // Import dependencies
        template.dependencies
        |> Stdlib.List.iter (fun dep ->
          Stdlib.printLine $"✓ Importing {dep}...")
        
        { state with currentSession = Some session }
      | Error err ->
        Stdlib.printLine $"Error creating project: {err}"
        state
    | Error err ->
      Stdlib.printLine $"Error creating session: {err}"
      state
```

---

## 3. Enhanced Package Navigation

### Matter-Aware Package Browsing
**Current State**: Package navigation exists but doesn't understand sessions  
**Needed**: Session-aware package viewing

```fsharp
// Enhanced packages/darklang/cli/packages/nav.dark
let buildSessionState (location: PackageLocation) (session: Option<Session.T>) : State =
  let results = 
    match session with
    | Some s ->
      // Get packages visible in this session (includes session changes + base)
      Search.searchContentsInSession s.id location
    | None ->
      // Get packages in current global state
      Search.searchContents location
  
  // Mark items that are modified in current session
  let markedResults = 
    match session with
    | Some s ->
      markSessionModifications results s.id
    | None ->
      results
  
  // Build navigation state with session awareness
  buildNavigationState location markedResults

// Show session modifications in package view
let displaySessionAwareItem (item: NavItem) (session: Option<Session.T>) : String =
  let baseDisplay = formatNavItem item
  match session, item.modificationStatus with
  | Some _, Modified hash -> $"{baseDisplay} ✏️ (modified in session)"
  | Some _, New -> $"{baseDisplay} ✨ (new in session)"
  | Some _, Deleted -> $"{baseDisplay} 🗑️ (deleted in session)"
  | _, Unchanged -> baseDisplay
```

### Enhanced Search with Session Context
**Current State**: Basic package search  
**Needed**: Session-aware search that includes local changes

```fsharp
// Enhanced packages/darklang/cli/packages/search.dark
let searchWithSession (query: String) (session: Option<Session.T>) : SearchResults =
  let globalResults = Search.searchGlobal query
  
  match session with
  | Some s ->
    let sessionResults = Search.searchSession s.id query
    // Merge results, prioritizing session changes
    Search.mergeResults globalResults sessionResults
  | None ->
    globalResults

// Show search results with session context
let displaySearchResults (results: SearchResults) (session: Option<Session.T>) : Unit =
  match session with
  | Some s ->
    Stdlib.printLine $"Search results (session: {s.name}):"
    Stdlib.printLine ""
    
    if not (Stdlib.List.isEmpty results.sessionResults) then
      Stdlib.printLine "📝 In your session:"
      displayResults results.sessionResults
      Stdlib.printLine ""
    
    Stdlib.printLine "🌐 Global packages:"
    displayResults results.globalResults
  | None ->
    Stdlib.printLine "Search results (global):"
    displayResults results.globalResults
```

---

## 4. Development Server Integration

### Hot Reload with Matter
**Current State**: No development server  
**Needed**: File watching that triggers Matter operations

```fsharp
// In packages/darklang/cli/dev/server.dark
module Darklang.Cli.Dev.Server

type DevServer = {
  httpServer: HttpServer
  fileWatcher: FileWatcher
  traceCollector: TraceCollector
  session: Session.T
}

let startDevServer (session: Session.T) (port: Int64) : DevServer =
  let httpServer = HttpServer.start port
  
  // Watch for file changes in virtual workspace
  let fileWatcher = FileWatcher.create (onFileChange session)
  
  // Collect traces for debugging
  let traceCollector = TraceCollector.start session.id
  
  Stdlib.printLine $"🚀 Development server started"
  Stdlib.printLine $"   Session: {session.name}"
  Stdlib.printLine $"   HTTP: http://localhost:{port}"
  Stdlib.printLine $"   Traces: enabled"
  
  { httpServer; fileWatcher; traceCollector; session }

let onFileChange (session: Session.T) (filePath: String) (newContent: String) : Unit =
  // Convert file change to Matter operations
  let location = virtualPathToLocation filePath
  let newHash = LibMatter.hashContent newContent ContentType.Function
  
  let ops = [
    Op.AddFunctionContent(newHash, newContent)
    Op.UpdateNamePointer(location, newHash)
  ]
  
  match LibMatter.executeOps ops with
  | Ok _ ->
    Stdlib.printLine $"✓ Hot reload: {location}"
    // Trigger any dependent recompilation
    triggerRecompilation location
  | Error err ->
    Stdlib.printLine $"⚠️ Hot reload failed: {err}"
```

### Trace Integration
**Current State**: No trace viewing  
**Needed**: Real-time trace collection and viewing

```fsharp
// In packages/darklang/cli/dev/traces.dark
module Darklang.Cli.Dev.Traces

let startTraceViewer (session: Session.T) : Unit =
  Stdlib.printLine "📊 Starting trace viewer..."
  
  // Set up trace collection for session
  let traceRule = TraceRule.T {
    sessionId = session.id
    condition = TraceCondition.All
    maxTraces = 1000L
  }
  
  LibTracing.addTraceRule traceRule
  
  // Start interactive trace viewer
  enterTraceViewerMode session.id

let enterTraceViewerMode (sessionId: uuid) : Unit =
  // Switch CLI to trace viewing mode
  Stdlib.printLine "Trace Viewer Mode - Commands:"
  Stdlib.printLine "  l  - List recent traces"
  Stdlib.printLine "  f  - Filter traces"
  Stdlib.printLine "  d  - Trace details"
  Stdlib.printLine "  q  - Quit trace viewer"
  
  // Handle trace viewer commands
  traceViewerLoop sessionId

let displayTrace (trace: ExecutionTrace) : Unit =
  Stdlib.printLine $"📍 {trace.functionName} at {trace.timestamp}"
  Stdlib.printLine $"   Input: {trace.input}"
  Stdlib.printLine $"   Output: {trace.output}"
  Stdlib.printLine $"   Duration: {trace.duration}ms"
  
  if not (Stdlib.List.isEmpty trace.steps) then
    Stdlib.printLine "   Steps:"
    trace.steps
    |> Stdlib.List.iter (fun step ->
      Stdlib.printLine $"     {step.stepNumber}. {step.description}")
```

---

## 5. AI Integration Infrastructure

### AI Command Processing
**Current State**: No AI integration  
**Needed**: Natural language command processing

```fsharp
// In packages/darklang/cli/ai/assistant.dark
module Darklang.Cli.AI.Assistant

type AIRequest = {
  input: String
  context: CommandContext  
  session: Option<Session.T>
}

type CommandContext = {
  currentLocation: PackageLocation
  recentCommands: List<String>
  openFiles: List<String>
}

let processAICommand (request: AIRequest) : AIResponse =
  // Send to AI service for processing
  let prompt = buildPromptFromContext request
  let response = AIService.complete prompt
  
  // Parse AI response into executable commands
  parseAIResponse response request.context

let buildPromptFromContext (request: AIRequest) : String =
  let contextInfo = 
    match request.session with
    | Some session ->
      $"Current session: {session.name}\n" +
      $"Location: {formatLocation request.context.currentLocation}\n" +
      $"Recent commands: {Stdlib.String.join request.context.recentCommands \", \"}"
    | None ->
      "No active session"
  
  $"Darklang CLI Assistant\n" +
  $"Context: {contextInfo}\n" +
  $"User request: {request.input}\n" +
  $"Generate appropriate CLI commands:"

// Handle AI suggestions
let executeAISuggestion (suggestion: AISuggestion) (state: AppState) : AppState =
  match suggestion with
  | CreateProject(name, template) ->
    // Execute: dark new {template} {name}
    Project.New.execute state [template; name]
  | ImportPackage(packageName) ->
    // Execute: dark import {packageName}
    Packages.Import.execute state [packageName]
  | CreateFunction(location, description) ->
    // Execute: dark fn create {location} "{description}"
    Functions.execute state ["create"; location; description]
  | ExplainCommand(command) ->
    // Provide detailed explanation
    AI.explainCommand command state
```

### Package Recommendation Engine
**Current State**: Basic package search  
**Needed**: AI-powered package suggestions

```fsharp
// In packages/darklang/cli/ai/recommendations.dark
module Darklang.Cli.AI.Recommendations

let getPackageRecommendations (query: String) (context: ProjectContext) : List<PackageRecommendation> =
  // Analyze user's query and project context
  let analysis = AI.analyzePackageNeeds query context
  
  // Search packages using AI-enhanced relevance
  let candidates = Search.searchPackagesWithAI query
  
  // Rank by relevance to user's actual needs
  let ranked = AI.rankPackagesByRelevance candidates analysis context
  
  // Return top recommendations with explanations
  ranked
  |> Stdlib.List.take 5L
  |> Stdlib.List.map (fun pkg ->
    PackageRecommendation.T {
      package = pkg
      relevanceScore = pkg.score
      reasoning = AI.explainRecommendation pkg context
      usageExamples = AI.generateUsageExamples pkg context
    })

let displayRecommendations (recommendations: List<PackageRecommendation>) : Unit =
  Stdlib.printLine "🤖 AI Package Recommendations:"
  Stdlib.printLine ""
  
  recommendations
  |> Stdlib.List.iteri (fun i rec ->
    Stdlib.printLine $"{i + 1}. 📦 {rec.package.name} ({rec.relevanceScore}%)"
    Stdlib.printLine $"   {rec.reasoning}"
    Stdlib.printLine $"   Example: {Stdlib.List.head rec.usageExamples}"
    Stdlib.printLine "")
```

---

## Summary of Technical Implementation Priorities

### Phase 1 (Foundation - 2-4 weeks)
1. **LibMatter implementation** - Core content hashing and op execution
2. **Database operations** - CRUD for all Matter tables  
3. **Basic session commands** - create, list, switch, status
4. **Enhanced package navigation** - session-aware browsing

### Phase 2 (Development Workflow - 4-6 weeks)  
5. **Project scaffolding** - template-based project creation
6. **Function creation** - Matter-aware function commands
7. **Development server** - hot reload with Matter integration
8. **Trace viewing** - real-time trace collection and display

### Phase 3 (AI Integration - 6-8 weeks)
9. **AI command processing** - natural language to CLI commands
10. **Package recommendations** - AI-powered package discovery
11. **Smart completion** - context-aware suggestions
12. **Workflow automation** - AI-generated command sequences

These technical changes transform the CLI from a basic package browser into a complete development environment that works naturally with Matter's content-addressable, session-based model while providing the developer experience users expect from modern tools.
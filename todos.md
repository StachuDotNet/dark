# Dark Classic UI Revival - TODO List

## Summary of Progress (Current Session)

### Completed
✅ Created LibBackend package structure with SQLite support
✅ Implemented SQLite builtins in F# for raw database access
✅ Created ApiServer canvas with basic endpoints:
  - `/check-apiserver` - Health check
  - `/api/{canvas}/v1/initial_load` - Returns initial canvas data
  - `/api/{canvas}/v1/add_op` - Accepts operations (stub)
  - `/api/{canvas}/v1/packages` - Returns package list
  - CORS support for browser compatibility
✅ Added `dark ui` command to CLI
✅ Created script to run ApiServer locally

### Architecture Decisions
- Using canvases/ directory for ApiServer (not packages/)
- Direct SQLite access via data.db (no connection management)
- HttpHandler attributes for endpoint definitions
- Minimal MVP approach (no secrets, workers, crons initially)

## Phase 1: Understanding & Planning
- [x] Read API_SPECIFICATION.md from classic-dark repo
- [ ] Analyze classic ApiServer implementation structure
- [ ] Review old LibBackend implementation
- [ ] Understand current Dark-next CLI structure
- [ ] Determine integration approach for old UI with new backend

## Phase 2: Core Infrastructure

### 2.1 Database & Storage Layer
- [ ] Implement raw SQLite access in Darklang
  - [ ] Create LibBackend package directory
  - [ ] Design SQLite builtins for raw access
  - [ ] Implement basic CRUD operations
  - [ ] Test database connectivity

### 2.2 Static Asset Hosting
- [ ] Create static asset storage system
  - [ ] Design asset storage schema
  - [ ] Implement asset serving endpoints
  - [ ] Set up UI file hosting
  - [ ] Seed initial static assets in DB

## Phase 3: ApiServer Implementation in Darklang

### 3.1 Core ApiServer Structure
- [ ] Create ApiServer canvas/package
- [ ] Implement basic HTTP server structure
- [ ] Set up routing framework
- [ ] Add CORS and middleware support

### 3.2 Authentication & Authorization
- [ ] Implement session management (simplified)
- [ ] Add CSRF token handling
- [ ] Create permission system (Read/ReadWrite)
- [ ] Skip complex auth for MVP

### 3.3 Essential API Endpoints
- [ ] `/api/{canvasName}/v1/initial_load` - Load canvas data
- [ ] `/api/{canvasName}/v1/add_op` - Apply operations
- [ ] `/api/{canvasName}/v1/execute_function` - Execute functions
- [ ] `/api/{canvasName}/v1/packages` - List packages
- [ ] `/api/{canvasName}/all_traces` - Basic trace support
- [ ] `/api/{canvasName}/v1/get_db_stats` - Database stats

### 3.4 Operations System
- [ ] Implement Op types in Darklang
- [ ] Create Op processing logic
- [ ] Build canvas state management
- [ ] Handle op counters and versioning

## Phase 4: Editor Migration

### 4.1 Remove Fluid Editor
- [ ] Analyze current Fluid implementation in dark-client-fork
- [ ] Remove Fluid-specific code
- [ ] Clean up unnecessary client code

### 4.2 Integrate New Editor
- [ ] Research Monaco or similar editors with LSP support
- [ ] Implement text-based editing
- [ ] Create encoding/decoding for text <-> AST
- [ ] Set up LSP integration (if possible)

### 4.3 Client-Server Communication
- [ ] Update client to work with new ApiServer
- [ ] Adjust data encoding/decoding
- [ ] Handle non-WASM backend differences
- [ ] Test round-trip communication

## Phase 5: CLI Integration

### 5.1 Add UI Command
- [ ] Create `dark ui` command in CLI
- [ ] Implement static asset serving
- [ ] Set up browser launching
- [ ] Configure local development server

### 5.2 Package Management
- [ ] Ensure packages reload properly during development
- [ ] Hook up package system to UI
- [ ] Test package loading and execution

## Phase 6: Simplifications & Deferrals

### 6.1 Features to Skip Initially
- [ ] Secrets management (defer)
- [ ] Workers (defer)
- [ ] Crons (defer)
- [ ] Complex traces (minimal implementation only)
- [ ] Source control integration (defer)
- [ ] Tunneling (defer)
- [ ] 404 handling (defer)

### 6.2 Simplifications
- [ ] Use local storage instead of complex DB where possible
- [ ] Single-user mode (no multi-tenancy initially)
- [ ] No Pusher/real-time updates initially
- [ ] Simplified permission model

## Next Steps (Immediate)

### 1. Complete SQLite Implementation
- [ ] Add Microsoft.Data.Sqlite package reference to paket
- [ ] Test SQLite builtins actually work
- [ ] Implement initializeTables function properly

### 2. Canvas Execution
- [ ] Verify canvas can be loaded and executed
- [ ] Test HTTP handlers work correctly
- [ ] Ensure ApiServer responds on correct port

### 3. Client Integration
- [ ] Configure dark-client-fork to point to localhost:9000
- [ ] Remove/bypass authentication requirements
- [ ] Test initial_load endpoint returns proper JSON

### 4. Editor Migration (Critical)
- [ ] Replace Fluid editor with Monaco or CodeMirror
- [ ] Handle text <-> AST conversion
- [ ] Update encoding/decoding for non-Fluid format

## Phase 7: Testing & Polish

### 7.1 Basic Testing
- [ ] Test canvas creation
- [ ] Test function creation and execution
- [ ] Test basic data persistence
- [ ] Verify package loading

### 7.2 Development Experience
- [ ] Ensure smooth local development
- [ ] Document setup process
- [ ] Create basic troubleshooting guide

## Implementation Notes

### Priority Order
1. Get basic ApiServer running with minimal endpoints
2. Connect old UI to new backend
3. Replace Fluid with simpler editor
4. Add CLI integration
5. Polish and test

### Key Decisions Needed
- How to handle ProgramTypes mismatch between old and new
- Whether to merge repos or keep separate
- Editor choice (Monaco vs alternatives)
- How much to simplify vs preserve original functionality

### Success Criteria
- User can download CLI and run `dark ui` command
- Browser opens with familiar Dark interface
- Can create and execute basic functions
- Packages load and work correctly
- Changes persist between sessions
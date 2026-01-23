# Darklang AI Support

AI integration packages for Darklang, providing type-safe interfaces for LLM operations, agents, and AI-powered workflows.

## Packages

### Darklang.AI.Common
Core AI primitives and types for building AI applications in Darklang.

**Key Types:**
- `Prompt` - Template-based prompt construction with variable interpolation
- `Session` - Conversation management with history and token tracking
- `Agent` - Autonomous agent execution with tool calling
- `Message` - Conversation messages (System, User, Assistant, Tool)
- `Tool` - Tool definitions for function calling

**Example Usage:**

```darklang
// Create a prompt template
let template =
  Darklang.AI.Common.PromptTemplate.create
    "Analyze this {{language}} code: {{code}}"
    (Stdlib.Option.Option.Some "You are a helpful code reviewer")

// Add variables
let withVars =
  Darklang.AI.Common.PromptTemplate.addVariable template "language" "Darklang"

let withCode =
  Darklang.AI.Common.PromptTemplate.addVariable withVars "code" "let x = 5"

// Render to prompt
let prompt = Darklang.AI.Common.PromptTemplate.toPrompt withCode

// Create a session
let config = Darklang.AI.Common.Session.defaultConfig ()
let session = Darklang.AI.Common.Session.create "my-session" config

// Add messages
let userMsg = Darklang.AI.Common.Message.user "Hello!"
let session2 = Darklang.AI.Common.Session.addMessage session userMsg

// Create an agent with tools
let tool =
  Darklang.AI.Common.Tool.create
    "get_weather"
    "Get the current weather"
    "weather-handler"

let toolWithParam =
  Darklang.AI.Common.Tool.addParameter
    tool
    "location"
    "City name"
    "string"
    true

let agent =
  Darklang.AI.Common.Agent.create
    "WeatherBot"
    session
    [ toolWithParam ]
```

### Darklang.AI.Anthropic
Client for Anthropic's Claude API with support for latest models.

**Supported Models:**
- `Models.opus4_5` - Claude Opus 4.5 (premium intelligence)
- `Models.sonnet4_5` - Claude Sonnet 4.5 (best for agents/coding)
- `Models.haiku3_5` - Claude 3.5 Haiku (fastest)
- `Models.haiku4_5` - Claude Haiku 4.5 (with extended thinking)

**Example Usage:**

```darklang
// Simple completion
let result =
  Darklang.AI.Anthropic.simpleCompletion
    apiKey
    Darklang.AI.Anthropic.Models.sonnet4_5
    "What is the capital of France?"

match result with
| Ok text -> "Response: " ++ text
| Error err -> "Error: " ++ err

// With system prompt
let result2 =
  Darklang.AI.Anthropic.completionWithSystem
    apiKey
    Darklang.AI.Anthropic.Models.sonnet4_5
    "You are a helpful coding assistant"
    "How do I write a function in Darklang?"
```

## Features

### Type-Safe AI Operations
All AI operations use Darklang's strong type system to catch errors at compile time.

### Template-Based Prompts
Build reusable prompt templates with variable interpolation:
- Supports `{{variable}}` syntax
- Few-shot example management
- System message configuration
- Stop sequence control

### Session Management
Track conversation history with automatic token counting:
- Configurable context window management
- Message history tracking
- Token usage monitoring
- Summarization support (planned)

### Agent Framework
Build autonomous agents that can:
- Call tools/functions
- Manage state
- Execute in loops
- Handle errors gracefully

### Vendor Support
- **Anthropic Claude**: Full API support (Messages, Tools, Vision planned)
- **OpenAI**: Existing package enhanced (planned)
- **Vendor-agnostic abstractions**: Swap vendors easily

## Architecture

### Design Principles
1. **Type Safety**: Leverage Darklang's type system for AI operations
2. **Async-First**: All operations return `Result` types
3. **Vendor Agnostic**: Common abstractions, vendor-specific implementations
4. **MCP Integration**: AI agents can be MCP servers
5. **Distributed Ready**: Run agents as Darklang workers

### Integration with Darklang
- Uses `Stdlib.HttpClient` for API calls
- Leverages package system for distribution
- Compatible with async runtime
- Type-safe JSON serialization with `Builtin.jsonSerialize`

## Roadmap

### Phase 1: Core Primitives ✓
- [x] Prompt, Session, Agent types
- [x] Basic Anthropic client
- [ ] Tests

### Phase 2: Enhanced Features (In Progress)
- [ ] Tool use/function calling
- [ ] Streaming support
- [ ] Vision/multimodal support
- [ ] Token counting API
- [ ] Response parsing improvements

### Phase 3: Advanced Features (Planned)
- [ ] Langchain-style chains and memory
- [ ] ReAct agents
- [ ] Multi-vendor support
- [ ] Agent orchestration
- [ ] Cost tracking

### Phase 4: Distribution & CLI (Planned)
- [ ] Beautiful CLI TUI for agents
- [ ] Distributed agent execution
- [ ] Agent deployment as services
- [ ] VS Code integration

## Development Status

**Current Status**: Early development / Spike phase

This is an experimental implementation for evaluating AI support in Darklang. APIs may change as we refine the design.

## Contributing

This package is part of the main Darklang repository. See the main README for contribution guidelines.

## References

- [Anthropic API Documentation](https://docs.anthropic.com/)
- [Darklang MCP Implementation](/packages/darklang/modelContextProtocol/)
- [Existing OpenAI Package](/packages/darklang/openai.dark)

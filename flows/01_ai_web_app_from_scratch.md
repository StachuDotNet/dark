# Flow 01: AI-Assisted Web App Development from Scratch

*Scenario: Developer wants to build a personal finance tracking app with AI assistance throughout the entire process*

---

## Session Start: Discovery and Planning

```bash
$ dark
[Darklang logo and welcome message]

Darklang.Stdlib> 
```

**Developer**: "I want to build a personal finance tracking app. Help me plan and build it."

**AI Integration**: CLI detects natural language intent and switches to AI-assisted mode:

```bash
🤖 AI Assistant activated for project planning.

Analyzing request: "personal finance tracking app"

Key features typically needed:
- User accounts and authentication  
- Transaction logging and categorization
- Spending analytics and budgets
- Data visualization
- Bank/credit card import

Would you like me to:
1. Create a new project with these features
2. Explore existing packages for finance apps  
3. Start with a simpler MVP version
4. Show examples of similar apps

Darklang.Stdlib> 1
```

## Project Creation with AI Scaffolding

```bash
🤖 Creating finance app project...

✓ Analyzing finance app patterns
✓ Selecting optimal project structure  
✓ Setting up development session

dark session new "finance-app-v1" --ai-assisted
dark new webapp personal-finance --template financial

Creating project structure:
✓ /handlers - HTTP endpoints
✓ /models - Data types  
✓ /auth - Authentication logic
✓ /analytics - Spending analysis
✓ /ui - Frontend components
✓ /tests - Test suites

Session: finance-app-v1 (active)
Project: /home/user/personal-finance

Next steps:
> dark dev                    # Start development server
> dark explore packages      # Browse finance-related packages  
> dark ai chat              # Continue AI conversation

personal-finance> dark dev
```

## AI-Enhanced Development Server

```bash
🚀 Darklang development server starting...

✓ Watching files for changes
✓ AI assistant connected  
✓ HTTP server on http://localhost:8000
✓ Package search index loaded
✓ Trace collection enabled

🤖 AI Assistant ready for development help.
    Type 'ai <message>' for assistance or 'ai help' for commands.

Development commands:
  t  - View traces          r  - Restart server
  l  - View logs           ai - AI assistance  
  p  - Browse packages     q  - Quit
  
http://localhost:8000 → Browser opening with basic finance app template

personal-finance> 
```

**Developer types**: `ai help me design the data model`

## AI-Assisted Data Modeling

```bash
🤖 Analyzing finance app data requirements...

Based on personal finance best practices, I recommend this data model:

┌─ Core Entities ─────────────────────────────────────┐
│ User        → { id, email, name, settings }         │
│ Account     → { id, userId, name, type, balance }   │
│ Transaction → { id, accountId, amount, category,    │
│                 date, description, tags }           │
│ Category    → { id, name, type, budget, color }     │
│ Budget      → { id, userId, period, limits }        │
└─────────────────────────────────────────────────────┘

Shall I:
1. Generate these types in models/ 
2. Show you existing finance packages to reuse
3. Create the database schema
4. All of the above

personal-finance> 4
```

**AI generates and shows code**:

```bash
🤖 Generating data models...

Creating /models/user.dark:
```

The AI switches to **interactive package navigation mode** to show related packages:

```bash
📦 Exploring related packages...

[Interactive navigation mode activates - full screen]

┌─ Finance-Related Packages ──────────────────────────────┐
│                                                         │
│  📁 Darklang.Finance/                                  │
│  │  💰 Currency        - Multi-currency support        │
│  │  📊 Analytics       - Financial calculations         │
│  │  🏦 BankIntegration  - Connect to banks             │
│  │  📋 Transactions    - Transaction utilities          │
│  │                                                      │
│  📁 Community.FinTech/                                 │  
│  │  🔐 PlaidAPI        - Bank account integration       │
│  │  📈 ChartHelpers    - Financial visualization        │
│  │  💳 CreditCard      - Credit card processing        │
│                                                         │
│  📁 Acme.PersonalBudget/                              │
│  │  🏷️  Categories      - Expense categorization        │
│  │  📊 Reports         - Budget reporting               │
│                                                         │
│  ▶ 💡 Press 'v' to preview, 'i' to import selected    │
│    Press 'Esc' to return to command mode              │
└─────────────────────────────────────────────────────────┘
```

**Developer navigates to `Darklang.Finance.Transactions`** and presses `v` for preview:

```bash
┌─ Preview: Darklang.Finance.Transactions ────────────────┐
│                                                         │
│ module Darklang.Finance.Transactions                    │
│                                                         │
│ type Transaction = {                                    │
│   id: UUID                                              │
│   amount: Money                                         │
│   date: DateTime                                        │
│   description: String                                   │
│   category: Category                                    │
│   tags: List<String>                                    │
│ }                                                       │
│                                                         │
│ Functions:                                              │
│ • categorize: Transaction -> Category                   │
│ • analyze: List<Transaction> -> Analytics               │
│ • export: List<Transaction> -> CSV                      │
│                                                         │
│ ⭐ Used by 1,247 projects | 👍 98% positive reviews     │
│ 📝 Try: dark try Darklang.Finance.Transactions.categorize │
└─────────────────────────────────────────────────────────┘
```

**Developer presses `i` to import**:

```bash
🤖 Importing Darklang.Finance.Transactions...

✓ Package imported successfully
✓ Types available in your project  
✓ Functions available for use
✓ Documentation updated

Updated project structure:
✓ /models/transaction.dark - Using Darklang.Finance.Transaction
✓ /handlers/transactions.dark - Generated CRUD endpoints
✓ /tests/transaction_tests.dark - Generated test suite

personal-finance> ai now help me create the API endpoints
```

## AI-Assisted API Development

```bash
🤖 Analyzing finance app API requirements...

I'll create REST endpoints for your finance app. Based on the Transaction model:

Recommended API structure:
┌─ Core Endpoints ────────────────────────────┐
│ GET    /api/accounts           - List accounts      │
│ POST   /api/accounts           - Create account     │
│ GET    /api/accounts/:id/transactions - List transactions │
│ POST   /api/transactions       - Add transaction    │
│ PUT    /api/transactions/:id   - Update transaction │
│ DELETE /api/transactions/:id   - Delete transaction │
│ GET    /api/analytics          - Spending analytics │
│ GET    /api/categories         - List categories    │
└─────────────────────────────────────────────────────┘

Shall I generate these handlers? (y/n)

personal-finance> y
```

**AI generates handlers and shows them being created in real-time**:

```bash
🤖 Generating API handlers...

Creating /handlers/transactions.dark:

type TransactionRequest = {
  amount: Float
  description: String  
  categoryId: String
  date: DateTime
}

let addTransaction (req: TransactionRequest) : HttpResponse =
  // AI-generated implementation
  let transaction = Transaction {
    id = UUID.generate()
    amount = Money.fromFloat req.amount
    date = req.date
    description = req.description
    category = Categories.getById req.categoryId
    tags = []
  }
  
  match Transactions.save transaction with
  | Ok saved -> HttpResponse.ok saved
  | Error err -> HttpResponse.badRequest err

✓ Handler created
✓ Added to routing table
✓ Test cases generated

Test the endpoint:
> curl -X POST localhost:8000/api/transactions \
  -d '{"amount": 23.50, "description": "Coffee", "categoryId": "food"}'

personal-finance> 
```

**Developer wants to test this immediately**:

```bash
personal-finance> eval 
```

**CLI switches to evaluation mode with AI assistance**:

```bash
🧪 Interactive evaluation mode - AI enhanced

You can test functions, API calls, and see traces immediately.
AI will help explain results and suggest improvements.

eval> @addTransaction { amount = 23.50, description = "Coffee shop", categoryId = "food", date = DateTime.now() }
```

**AI helps with the evaluation**:

```bash
🤖 Evaluating transaction creation...

Input: addTransaction with coffee purchase data
⚠️  Warning: categoryId "food" doesn't exist yet

Shall I:
1. Create the "food" category automatically
2. Show you how to create categories first  
3. Mock the response for testing

eval> 1

🤖 Creating default expense categories...

✓ Created category: food (🍕 Food & Dining)
✓ Created category: transport (🚗 Transportation)  
✓ Created category: bills (💸 Bills & Utilities)
✓ Created category: entertainment (🎬 Entertainment)
✓ Created category: shopping (🛍️ Shopping)

Now retrying transaction creation...

Result: Ok(Transaction {
  id: "uuid-abc123",
  amount: Money(23.50, USD),
  date: 2024-09-12T16:45:00Z,
  description: "Coffee shop", 
  category: Category { name: "food", icon: "🍕" },
  tags: []
})

✓ Transaction saved to database
✓ Account balance updated  
✓ Analytics updated

View traces: 'traces' | Continue testing: 'eval' | Exit: 'q'

eval> traces
```

## AI-Enhanced Trace Viewing

```bash
📊 Transaction Traces - AI Analysis

┌─ addTransaction Call Trace ─────────────────────────┐
│ 16:45:01.234 → addTransaction started              │
│ 16:45:01.235   ✓ Input validation passed           │
│ 16:45:01.236   → Categories.getById("food")        │
│ 16:45:01.237   ✓ Category found: food              │
│ 16:45:01.238   → UUID.generate()                   │
│ 16:45:01.239   ✓ Generated: uuid-abc123            │
│ 16:45:01.240   → Transactions.save(transaction)    │
│ 16:45:01.245   ✓ Database write successful         │
│ 16:45:01.246   → Accounts.updateBalance(userId)    │
│ 16:45:01.248   ✓ Balance updated: $1,234.56        │
│ 16:45:01.249 ✓ addTransaction completed (15ms)     │
└─────────────────────────────────────────────────────┘

🤖 AI Analysis:
✅ Function executed successfully  
✅ Good performance (15ms total)
✅ All side effects completed correctly

💡 Suggestions:
- Consider batch operations for multiple transactions
- Add currency conversion handling
- Transaction validation could be more robust

Type 'ai explain <step>' for details on any step.

traces> q
personal-finance> 
```

## Session Switching and Collaboration

**Developer realizes they want to work on authentication in parallel**:

```bash
personal-finance> dark session list

Development sessions:
  finance-app-v1    (active) ← current - 5 changes  
  
personal-finance> dark session new "auth-system" --branch-from finance-app-v1

🤖 Creating new development session...

✓ Session "auth-system" created
✓ Based on current state of finance-app-v1
✓ Ready for parallel development

Switch to new session? (y/n) y

✓ Switched to session: auth-system
✓ Working directory: same project, different context

auth-system> ai help me implement user authentication
```

**AI in new session context**:

```bash
🤖 Analyzing authentication needs for finance app...

Security is critical for financial applications. I recommend:

🔐 Authentication Strategy:
- JWT tokens with refresh mechanism
- Email/password + optional 2FA
- Password strength requirements  
- Session management
- Rate limiting on login attempts

🛡️ Security Features:
- Encrypted user data
- Audit logging  
- Secure password storage (bcrypt)
- HTTPS enforcement

Shall I:
1. Implement a complete auth system
2. Use an existing Darklang auth package
3. Show you both options to compare

auth-system> 2
```

**AI switches to package exploration mode for auth packages**:

```bash
📦 Exploring authentication packages...

[Interactive navigation mode]

┌─ Authentication Packages ───────────────────────────┐
│                                                     │
│  🔐 Darklang.Auth/                     ⭐⭐⭐⭐⭐    │
│  │  🔑 JWT           - Token management             │
│  │  👤 Users         - User account system         │  
│  │  🛡️  Security      - Security utilities          │
│  │  🔐 TwoFactor     - 2FA implementation          │
│                                                     │
│  🏢 Enterprise.SSO/                   ⭐⭐⭐⭐      │
│  │  🌐 OAuth2        - OAuth2 provider             │
│  │  🏛️  SAML          - SAML integration            │
│                                                     │
│  🛡️  Community.Secure/                ⭐⭐⭐       │
│  │  🔒 Encryption    - Data encryption             │
│  │  📝 Audit         - Security auditing          │
│                                                     │
│  ▶ 'v' preview | 'i' import | 't' try | Esc exit  │
└─────────────────────────────────────────────────────┘

Selected: Darklang.Auth.JWT
```

**Developer presses `t` to try the package interactively**:

```bash
🧪 Interactive package testing - Darklang.Auth.JWT

Available functions:
• generateToken(userId: String, claims: Dict) -> Token
• validateToken(token: String) -> Result<Claims, Error>  
• refreshToken(oldToken: String) -> Result<Token, Error>

Try functions with real data:

try> generateToken("user123", { "role": "user", "email": "alice@example.com" })

Result: Token {
  value: "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  expires: 2024-09-13T16:45:00Z,
  claims: { "role": "user", "email": "alice@example.com", "userId": "user123" }
}

try> validateToken("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...")

Result: Ok({ "role": "user", "email": "alice@example.com", "userId": "user123" })

🤖 This package looks perfect for your finance app!
   - Clean API design
   - Good security practices
   - Handles token refresh automatically

Import this package? (y/n) y
```

## AI-Assisted Integration and Testing

```bash
✓ Darklang.Auth imported successfully

🤖 Now I'll integrate authentication with your finance app...

Creating /auth/handlers.dark:

let login (email: String, password: String) : HttpResponse =
  match Users.authenticate email password with  
  | Ok user ->
    let token = JWT.generateToken user.id { "email": user.email }
    HttpResponse.ok { "token": token.value, "user": user }
  | Error _ ->
    HttpResponse.unauthorized "Invalid credentials"

✓ Login handler created
✓ Registration handler created  
✓ Password reset handler created
✓ Middleware for protected routes added

Test the auth system:

auth-system> eval
eval> @login "test@example.com" "password123"

🤖 First, let me create a test user for you...

eval> @Users.create { email = "test@example.com", password = "password123", name = "Test User" }

Result: Ok(User {
  id: "user-xyz789",
  email: "test@example.com", 
  name: "Test User",
  createdAt: 2024-09-12T16:45:00Z
})

eval> @login "test@example.com" "password123"

Result: Ok({
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": { "id": "user-xyz789", "name": "Test User", "email": "test@example.com" }
})

🤖 Perfect! Authentication is working. 

Suggestions for next steps:
1. Add authentication middleware to transaction endpoints
2. Test protected routes  
3. Merge this session back to main development
4. Add frontend login form

auth-system> 
```

## Session Merging with AI Assistance

```bash
auth-system> ai I want to merge this authentication work back to the main session

🤖 Analyzing session merge...

Comparing sessions:
📊 auth-system (current): 8 new files, 12 changes
📊 finance-app-v1: 3 new changes since branch

Potential conflicts detected:
⚠️  /handlers/routes.dark - Both sessions modified

Auto-merge strategy:
✅ /auth/* files - No conflicts (new files)
✅ /models/user.dark - Compatible changes
⚠️  /handlers/routes.dark - Needs manual resolution

Show conflict resolution? (y/n) y

┌─ Conflict Resolution ──────────────────────────────┐
│                                                    │
│ File: /handlers/routes.dark                        │
│                                                    │
│ <<<<<<< auth-system                               │
│ let routes = [                                     │
│   ("/api/auth/login", Auth.login),                │
│   ("/api/auth/register", Auth.register),          │
│   ("/api/transactions", Auth.protect Transactions.list), │
│ =======                                            │
│ let routes = [                                     │
│   ("/api/transactions", Transactions.list),       │
│   ("/api/accounts", Accounts.list),               │
│ >>>>>>> finance-app-v1                            │
│                                                    │
│ 🤖 Suggested resolution:                           │
│ let routes = [                                     │
│   ("/api/auth/login", Auth.login),                │
│   ("/api/auth/register", Auth.register),          │
│   ("/api/transactions", Auth.protect Transactions.list), │
│   ("/api/accounts", Auth.protect Accounts.list),  │
│ ]                                                  │
│                                                    │
│ Apply suggestion? (y/n/e for edit)                │
└────────────────────────────────────────────────────┘

auth-system> y

🤖 Applying merge resolution...

✓ Merged authentication system
✓ Protected transaction endpoints  
✓ Protected account endpoints
✓ All tests updated
✓ Documentation updated

Merge successful! Switching to updated finance-app-v1...

finance-app-v1> dark test --ai-explain

🧪 Running test suite with AI analysis...

✓ Auth tests: 8/8 passed
✓ Transaction tests: 12/12 passed  
✓ Account tests: 6/6 passed
✓ Integration tests: 4/4 passed

🤖 Test Analysis:
✅ All authentication flows working correctly
✅ Protected endpoints properly secured
✅ Token validation working
✅ User session management functional

No issues detected! Your finance app now has:
- Secure user authentication
- Protected financial endpoints  
- Token-based sessions
- Comprehensive test coverage

Ready for frontend development or deployment!

finance-app-v1> 
```

This flow demonstrates how AI assistance integrates deeply with the existing Darklang CLI's sophisticated interactive features, enabling natural collaborative development with intelligent automation and suggestions throughout the entire development process.

# Flow 05: AI-Assisted Debugging and Investigation

*Scenario: Developer's finance app has a bug where transaction amounts are sometimes calculated incorrectly. They use AI assistance to investigate and fix the issue using Darklang's trace-driven debugging capabilities.*

---

## Problem Discovery: "Something's wrong with the numbers"

```bash
$ dark
finance-app-main> 
```

**Developer**: "Users are reporting that their transaction totals don't match up. Some amounts seem off. Help me debug this."

```bash
🤖 I'll help you investigate transaction calculation issues.

Let's start by examining recent traces to identify the problem.

Switching to investigation mode...

finance-app-main> traces --filter "transaction" --recent 1h
```

## AI-Enhanced Trace Analysis

```bash
📊 Transaction Traces (Last 1 hour) - AI Analysis Active

🤖 Analyzing 47 transaction traces for anomalies...

⚠️ ANOMALIES DETECTED:

┌─ Suspicious Transaction Patterns ──────────────────────────┐
│                                                            │
│ 🔍 Pattern 1: Amount Discrepancies (8 traces)            │
│   Input amount: 23.45 → Stored amount: 2345               │
│   Input amount: 156.78 → Stored amount: 15678             │
│   🤖 Analysis: Amounts missing decimal points             │
│                                                            │
│ 🔍 Pattern 2: Currency Conversion Issues (3 traces)       │
│   USD $100.00 → Stored as EUR €100.00                     │
│   🤖 Analysis: Currency type not being converted          │
│                                                            │
│ 🔍 Pattern 3: Negative Amount Handling (2 traces)         │
│   Refund: -$50.00 → Stored as: $50.00                     │
│   🤖 Analysis: Sign lost during processing                │
│                                                            │
│ 📈 Error rate: 13/47 traces (27.7%)                       │
│ 🎯 Primary issue: Decimal point handling                  │
│                                                            │
│ Investigate details? [1] [2] [3] or 'all'                 │
└────────────────────────────────────────────────────────────┘

finance-app-main> 1
```

**AI starts deep investigation of the decimal point issue**:

```bash
🤖 Investigating decimal point handling issue...

Analyzing Pattern 1 traces in detail:

📍 Trace #1: addTransaction(amount=23.45, description="Coffee")
   ├─ 16:42:01.234 → validateAmount(23.45)
   │  └─ Result: Ok(23.45) ✓
   ├─ 16:42:01.235 → convertToMoney(23.45, "USD")  
   │  └─ Result: Money(2345, USD) ⚠️ ISSUE HERE
   ├─ 16:42:01.236 → saveTransaction(Money(2345, USD))
   │  └─ Result: Ok(saved) ✓
   └─ Final stored amount: $23.45 → $2,345.00 ❌

🤖 Found the bug! The convertToMoney function is treating 
   cents as dollars. Let's examine that function.

View convertToMoney implementation? (y/n) y

finance-app-main> nav Money convertToMoney
```

**CLI switches to interactive navigation mode to examine the problematic function**:

```bash
┌─ Function: Money.convertToMoney ────────────────────────────┐
│                                                            │
│ 📍 Location: Finance.Utils.Money.convertToMoney           │
│ 🔗 Content Hash: abc123def456                             │
│ ⚠️ Flagged by AI as source of decimal point bug           │
│                                                            │
│ let convertToMoney (amount: Float) (currency: String) =    │
│   // BUG: Converting float dollars to integer cents       │
│   let cents = Stdlib.Float.round (amount * 100.0)         │
│   Money { cents = Stdlib.Int64.fromFloat cents            │
│          currency = currency }                             │
│                                                            │
│ 🤖 AI Analysis:                                           │
│ ❌ Function assumes amount is in dollars                   │
│ ❌ Multiplies by 100 to convert to cents                  │
│ ❌ But input 23.45 is already user-intended dollars       │
│ ❌ So 23.45 * 100 = 2345 cents = $23.45 stored as $2345  │
│                                                            │
│ 💡 Fix suggestion:                                        │
│ Remove the * 100.0 multiplication                         │
│                                                            │
│ [f] Fix automatically | [t] Test fix | [h] History       │
└────────────────────────────────────────────────────────────┘

finance-app-main> t
```

**AI helps test the fix before applying it**:

```bash
🧪 Testing fix for convertToMoney function...

🤖 I'll create a test session to validate the fix without affecting your main code.

dark session new "debug-money-conversion" --branch-from finance-app-main

✓ Created test session for debugging
✓ Switched to: debug-money-conversion  

debug-money-conversion> 

🤖 Now I'll apply the suggested fix and test it:

Proposed fix:
let convertToMoney (amount: Float) (currency: String) =
  let cents = Stdlib.Float.round amount  // Removed * 100.0
  Money { cents = Stdlib.Int64.fromFloat cents
          currency = currency }

Apply fix? (y/n) y

✓ Fix applied to test session

Testing with problem cases:

debug-money-conversion> eval
eval> @convertToMoney 23.45 "USD"

🤖 Testing original problem case...

Result: Money { cents = 23, currency = "USD" }

⚠️ New issue detected! Now we're losing cents entirely.
   23.45 should become 2345 cents, not 23 cents.

🤖 I see the real problem now. Let me analyze the Money type definition...

eval> quit
debug-money-conversion> nav Money --type
```

**AI discovers the deeper issue by examining the Money type**:

```bash
┌─ Type Definition: Money ────────────────────────────────────┐
│                                                            │
│ type Money = {                                             │
│   cents: Int64    // Stored in cents (1/100 of currency)  │
│   currency: String                                         │
│ }                                                          │
│                                                            │
│ 🤖 AI Analysis of the complete problem:                   │
│                                                            │
│ ✅ Money type correctly stores amounts in cents           │
│ ✅ convertToMoney should convert dollars to cents         │
│ ❌ BUT: Input validation is inconsistent                  │
│                                                            │
│ 💡 Real issue discovered:                                 │
│ The API receives amount=23.45 (dollars)                   │
│ convertToMoney correctly converts to 2345 cents           │
│ BUT: Display functions assume cents are dollars!          │
│                                                            │
│ Let's check the display functions...                      │
└────────────────────────────────────────────────────────────┘

debug-money-conversion> nav Money formatAmount
```

**AI continues the investigation to find the display bug**:

```bash
┌─ Function: Money.formatAmount ──────────────────────────────┐
│                                                            │
│ let formatAmount (money: Money) : String =                 │
│   $"${money.cents}.00 {money.currency}"                   │
│                                                            │
│ 🤖 FOUND THE REAL BUG!                                    │
│                                                            │
│ ❌ formatAmount treats cents field as dollars             │
│ ✅ Should divide cents by 100 to get dollar amount        │
│                                                            │
│ Example:                                                   │
│ Money(2345, USD) should display as "$23.45 USD"           │
│ Currently displays as: "$2345.00 USD"                     │
│                                                            │
│ 💡 Correct implementation:                                │
│ let formatAmount (money: Money) : String =                 │
│   let dollars = money.cents / 100                         │
│   let cents = money.cents % 100                           │
│   $"${dollars}.{cents:D2} {money.currency}"               │
│                                                            │
│ [f] Fix automatically | [t] Test fix | [r] Related bugs  │
└────────────────────────────────────────────────────────────┘

debug-money-conversion> r
```

**AI searches for related bugs across the codebase**:

```bash
🤖 Searching for related Money formatting bugs...

Analyzing all functions that use Money type...

┌─ Related Issues Found ──────────────────────────────────────┐
│                                                            │
│ 🔍 Scanning 23 functions that handle Money...             │
│                                                            │
│ ❌ calculateTotal (in Analytics.Reports)                  │
│    Sums money.cents values but displays as dollars        │
│                                                            │
│ ❌ compareAmounts (in Utils.Validation)                   │
│    Compares cents directly without conversion             │
│                                                            │
│ ❌ applyDiscountPercentage (in Pricing.Discounts)         │
│    Applies percentage to cents, treating as dollars       │
│                                                            │
│ ✅ convertCurrency (in Exchange.Rates) ✓                 │
│    Correctly handles cents                                 │
│                                                            │
│ ✅ saveToDatabase (in Persistence.Transactions) ✓        │
│    Correctly stores cents values                          │
│                                                            │
│ 📊 Total affected functions: 4 out of 23                  │
│ 🎯 All related to display/calculation logic               │
│                                                            │
│ Fix all related issues? [y] or review individually [r]    │
└────────────────────────────────────────────────────────────┘

debug-money-conversion> y
```

**AI applies comprehensive fix across all related functions**:

```bash
🤖 Applying comprehensive Money formatting fixes...

🔧 Fixing formatAmount function:
let formatAmount (money: Money) : String =
  let dollars = money.cents / 100L
  let remainingCents = money.cents % 100L
  $"${dollars}.{remainingCents:D2} {money.currency}"

🔧 Fixing calculateTotal function:
let calculateTotal (transactions: List<Transaction>) : Money =
  let totalCents = 
    transactions
    |> Stdlib.List.map (fun t -> t.amount.cents)
    |> Stdlib.List.fold (+) 0L
  Money { cents = totalCents, currency = "USD" }

🔧 Fixing compareAmounts function:
let compareAmounts (money1: Money) (money2: Money) : Comparison =
  Stdlib.Int64.compare money1.cents money2.cents

🔧 Fixing applyDiscountPercentage function:
let applyDiscountPercentage (money: Money) (percentage: Float) : Money =
  let discountCents = Stdlib.Float.round (Stdlib.Int64.toFloat money.cents * percentage / 100.0)
  let newCents = money.cents - Stdlib.Int64.fromFloat discountCents
  Money { cents = newCents, currency = money.currency }

✅ All fixes applied to debug session

Testing comprehensive fix...

debug-money-conversion> eval
eval> 
// Test the original problem case
let testMoney = convertToMoney 23.45 "USD"
let formatted = formatAmount testMoney
let _ = Stdlib.printLine $"$23.45 input → {formatted}"

// Test edge cases
let smallAmount = convertToMoney 0.05 "USD"
let largeAmount = convertToMoney 1234.56 "USD"
let _ = Stdlib.printLine $"$0.05 → {formatAmount smallAmount}"
let _ = Stdlib.printLine $"$1234.56 → {formatAmount largeAmount}"

// Test calculations
let total = calculateTotal [
  Transaction { amount = convertToMoney 23.45 "USD", ... },
  Transaction { amount = convertToMoney 156.78 "USD", ... }
]
let _ = Stdlib.printLine $"Total: {formatAmount total}"
```

**AI validates the comprehensive fix**:

```bash
🤖 Validating comprehensive fix...

Results:
$23.45 input → $23.45 USD ✅
$0.05 → $0.05 USD ✅  
$1234.56 → $1234.56 USD ✅
Total: $180.23 USD ✅

🎉 All Money formatting issues resolved!

Running automated tests...

✅ Unit tests: 15/15 passed
✅ Integration tests: 8/8 passed  
✅ No regressions detected

🤖 The fix is ready to apply to your main session.

Summary of changes:
- Fixed formatAmount to properly convert cents to dollars
- Fixed calculateTotal to handle cents arithmetic correctly
- Fixed compareAmounts to work with cents values
- Fixed applyDiscountPercentage to apply percentages correctly

Apply these fixes to finance-app-main session? (y/n) y

debug-money-conversion> 
```

## Merging the Fix Back to Main

```bash
🤖 Merging Money formatting fixes back to main session...

dark session switch finance-app-main

✓ Switched to: finance-app-main

Merging changes from debug-money-conversion session:

finance-app-main> ai merge debug-money-conversion --auto-resolve

🤖 Analyzing merge requirements...

Changes to merge:
📝 Money.formatAmount - Display bug fix
📝 Analytics.Reports.calculateTotal - Calculation bug fix  
📝 Utils.Validation.compareAmounts - Comparison bug fix
📝 Pricing.Discounts.applyDiscountPercentage - Percentage bug fix

No conflicts detected (all changes are in test session only)

✅ Merged successfully
✅ All Money formatting fixes applied
✅ Test suite ran: 31/31 tests passed

finance-app-main> 
```

## Validation with Real User Data

```bash
finance-app-main> ai let's verify this fixes the real user issues

🤖 Great idea! Let's test with the actual problematic transactions 
   that users reported.

Retrieving user reported transactions...

📊 Testing against user-reported issues:

User Issue #1: "My $23.45 coffee shows as $2,345.00"
Test: @addTransaction(23.45, "Coffee", "food")
Result: Stored as $23.45, displays as $23.45 ✅

User Issue #2: "Total of $50 + $30 shows as $8,000"  
Test: @calculateTotal([Transaction($50), Transaction($30)])
Result: Total displays as $80.00 (was $8,000.00) ✅

User Issue #3: "5% discount on $100 gave me $495 off"
Test: @applyDiscountPercentage(Money($100), 5.0)  
Result: $5.00 discount (was $495.00 discount) ✅

🎉 All user-reported issues resolved!

Want to:
1. Deploy the fix immediately  
2. Add monitoring to catch similar issues
3. Create regression tests
4. All of the above

finance-app-main> 4
```

**AI helps with comprehensive validation and monitoring**:

```bash
🤖 Setting up comprehensive Money bug prevention...

1. 🚀 Deploying fix to production...
   ✅ Build successful
   ✅ All tests passed
   ✅ Deployed to staging
   ✅ Staging tests passed
   ✅ Deployed to production
   
2. 📊 Adding Money calculation monitoring...
   ✅ Money.formatAmount trace rules added
   ✅ calculateTotal validation checks added  
   ✅ Anomaly detection for amount discrepancies
   ✅ Alert threshold: >5% calculation errors
   
3. 🧪 Creating regression test suite...
   ✅ Money formatting test cases
   ✅ Edge case tests (zero, negative, large amounts)
   ✅ Integration tests for user workflows
   ✅ Added to CI pipeline
   
4. 📈 Setting up dashboards...
   ✅ Money calculation accuracy metrics
   ✅ User transaction success rates
   ✅ Error pattern detection

finance-app-main> traces --live --filter "Money"
```

**Real-time monitoring shows the fix working**:

```bash
📊 Live Money Traces - Post-Fix Monitoring

🟢 Live transaction processing (last 5 minutes):
   
16:58:23 addTransaction($15.67) → Stored: $15.67 ✅
16:58:45 addTransaction($234.89) → Stored: $234.89 ✅  
16:59:12 calculateTotal([$15.67, $234.89]) → $250.56 ✅
16:59:34 applyDiscount($250.56, 10%) → $225.50 ✅

📈 Success rate: 100% (was 72.3% before fix)
⚡ Average processing time: 12ms  
🎯 Zero anomalies detected

🤖 Perfect! The Money calculation bug is completely resolved.
   
Users should now see correct amounts in all transactions.
The monitoring will alert us if similar issues arise.

Type 'q' to exit live monitoring.

live-traces> q
finance-app-main> 
```

## Post-Investigation Analysis

```bash
finance-app-main> ai summarize what we learned from this investigation

🤖 Investigation Summary - Money Calculation Bug

🔍 PROBLEM IDENTIFIED:
Inconsistent handling of Money type between storage and display layers
- Money stored cents correctly but displayed them as dollars
- Affected 4 critical functions across transaction processing

🧠 INVESTIGATION TECHNIQUE:
1. Started with user symptoms (wrong amounts)
2. Used AI trace analysis to identify patterns  
3. Followed data flow from input to storage to display
4. Created isolated test session for safe experimentation
5. Comprehensive codebase scan for related issues

🛠️ SOLUTION APPROACH:
- Fixed root cause (display formatting) not just symptoms
- Applied systematic fix across all related functions
- Added monitoring and regression tests
- Validated with real user data

💡 KEY LEARNINGS:
1. Type consistency crucial in financial calculations
2. Trace-driven debugging much faster than traditional debugging
3. AI pattern recognition caught issues human review missed
4. Test sessions allow safe experimentation without risk
5. Comprehensive fixes prevent related bugs

🎯 PREVENTIVE MEASURES:
- Money calculation monitoring active
- Regression test suite prevents recurrence  
- Type safety improvements for financial data
- Regular Money-related code reviews scheduled

This investigation demonstrates Darklang's trace-driven debugging
superiority over traditional debugging approaches.

finance-app-main> 
```

This flow showcases how Darklang's sophisticated trace system, combined with AI assistance, enables developers to quickly identify, investigate, and fix complex bugs through guided analysis rather than traditional debugging approaches. The AI helps connect symptoms to root causes and ensures comprehensive fixes that prevent related issues.
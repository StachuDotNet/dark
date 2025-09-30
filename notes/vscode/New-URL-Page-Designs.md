# New URL/Page Designs - Extended Navigation

## Overview
Beyond the core package management URLs, the system needs additional pages for **instance management**, **session coordination**, **user accounts**, and **package search**. These provide the broader context and management capabilities around the core development workflow.

### New URLs
```
dark://instances                                 # Instance browser & management
dark://instances/instance-id                     # Specific instance details
dark://session/session-id                        # Session overview & management
dark://sessions                                  # All sessions browser
dark://user                                      # User account & settings
dark://user/profile                              # User profile management
dark://search                                    # Package search interface
dark://search?query=term&type=function           # Search with parameters
```

## 1. Instances - `dark://instances`

### Purpose
Manage multiple Darklang instances (production, staging, local development) with sync control and package browsing.

### URL Patterns
```
dark://instances                                 # Instance browser
dark://instances/prod-instance                   # Specific instance details
```

## 2. Sessions - `dark://sessions`

### Purpose
Browse and manage all development sessions across projects and time periods.

### URL Patterns
```
dark://sessions                                  # All sessions browser
dark://sessions/active                           # Currently active sessions
dark://sessions/recent                           # Recent sessions
dark://sessions/archived                         # Archived sessions
dark://session/session-id                        # Specific session details
dark://session/session-id/patches                # Session patches view
```

### Sessions Browser Content
```html
<!DOCTYPE html>
<html>
<head><title>Development Sessions</title></head>
<body>
  <div class="sessions-page">
    <header>
      <h1>🔄 Development Sessions</h1>
      <p>Manage your development workflows and collaborative sessions</p>
      <div class="session-actions">
        <button onclick="newSession()">+ New Session</button>
        <button onclick="resumeLastSession()">🔄 Resume Last</button>
        <button onclick="importSession()">📥 Import Session</button>
      </div>
    </header>

    <div class="session-filters">
      <div class="filter-tabs">
        <button class="tab active" onclick="filterSessions('active')">Active (2)</button>
        <button class="tab" onclick="filterSessions('recent')">Recent (8)</button>
        <button class="tab" onclick="filterSessions('archived')">Archived (25)</button>
        <button class="tab" onclick="filterSessions('shared')">Shared (3)</button>
      </div>

      <div class="search-filter">
        <input type="text" placeholder="Search sessions..." />
        <select>
          <option>All projects</option>
          <option>MyApp</option>
          <option>Internal Tools</option>
        </select>
      </div>
    </div>

    <div class="sessions-list">
      <div class="session-card active">
        <div class="session-header">
          <h3>🔄 user-improvements</h3>
          <span class="status active">🟢 Active</span>
          <span class="time">Started 2 hours ago</span>
        </div>

        <div class="session-description">
          <p>Enhancing user validation and profile management features</p>
        </div>

        <div class="session-stats">
          <div class="stat">
            <span class="label">Patches</span>
            <span class="value">3</span>
          </div>
          <div class="stat">
            <span class="label">Files</span>
            <span class="value">12</span>
          </div>
          <div class="stat">
            <span class="label">Conflicts</span>
            <span class="value">2</span>
          </div>
          <div class="stat">
            <span class="label">Contributors</span>
            <span class="value">1</span>
          </div>
        </div>

        <div class="session-patches-preview">
          <div class="patch ready">✅ validation-enhancements (ready)</div>
          <div class="patch conflicts">⚠️ user-profile-features (2 conflicts)</div>
          <div class="patch draft">📝 performance-optimizations (draft)</div>
        </div>

        <div class="session-actions">
          <button onclick="resumeSession('user-improvements')" class="primary">Resume</button>
          <button onclick="viewSession('user-improvements')">View Details</button>
          <button onclick="shareSession('user-improvements')">Share</button>
          <button onclick="archiveSession('user-improvements')">Archive</button>
        </div>
      </div>

      <div class="session-card">
        <div class="session-header">
          <h3>🚀 api-redesign</h3>
          <span class="status recent">🔵 Recent</span>
          <span class="time">Closed 1 day ago</span>
        </div>

        <div class="session-description">
          <p>Complete redesign of REST API endpoints for better performance</p>
        </div>

        <div class="session-stats">
          <div class="stat">
            <span class="label">Patches</span>
            <span class="value">7</span>
          </div>
          <div class="stat">
            <span class="label">Files</span>
            <span class="value">35</span>
          </div>
          <div class="stat">
            <span class="label">Merged</span>
            <span class="value">100%</span>
          </div>
          <div class="stat">
            <span class="label">Contributors</span>
            <span class="value">3</span>
          </div>
        </div>

        <div class="session-completion">
          <span class="completion-status">✅ Successfully completed</span>
          <div class="completion-stats">
            <span>7/7 patches merged</span>
            <span>0 conflicts</span>
            <span>35 functions updated</span>
          </div>
        </div>

        <div class="session-actions">
          <button onclick="viewSession('api-redesign')">View Results</button>
          <button onclick="createSimilar('api-redesign')">Create Similar</button>
          <button onclick="exportSession('api-redesign')">Export</button>
        </div>
      </div>

      <div class="session-card collaborative">
        <div class="session-header">
          <h3>👥 team-refactoring</h3>
          <span class="status shared">🟠 Shared</span>
          <span class="time">Started 3 hours ago</span>
        </div>

        <div class="session-description">
          <p>Cross-team effort to refactor authentication and authorization</p>
        </div>

        <div class="session-collaborators">
          <div class="collaborator">
            <span class="avatar">👤</span>
            <span class="name">alice</span>
            <span class="role">Owner</span>
          </div>
          <div class="collaborator">
            <span class="avatar">👤</span>
            <span class="name">bob</span>
            <span class="role">Contributor</span>
          </div>
          <div class="collaborator">
            <span class="avatar">👤</span>
            <span class="name">charlie</span>
            <span class="role">Reviewer</span>
          </div>
        </div>

        <div class="session-stats">
          <div class="stat">
            <span class="label">Total Patches</span>
            <span class="value">12</span>
          </div>
          <div class="stat">
            <span class="label">My Patches</span>
            <span class="value">3</span>
          </div>
          <div class="stat">
            <span class="label">Pending Review</span>
            <span class="value">4</span>
          </div>
        </div>

        <div class="session-actions">
          <button onclick="joinSession('team-refactoring')" class="primary">Join Session</button>
          <button onclick="viewSession('team-refactoring')">View Progress</button>
          <button onclick="leaveSession('team-refactoring')">Leave</button>
        </div>
      </div>
    </div>

    <div class="session-analytics">
      <h3>📊 Session Analytics</h3>
      <div class="analytics-grid">
        <div class="analytic">
          <h4>🕐 Average Session Duration</h4>
          <span class="value">4.2 hours</span>
        </div>
        <div class="analytic">
          <h4>📈 Completion Rate</h4>
          <span class="value">87%</span>
        </div>
        <div class="analytic">
          <h4>🔄 Sessions This Week</h4>
          <span class="value">5</span>
        </div>
        <div class="analytic">
          <h4>👥 Collaboration Score</h4>
          <span class="value">92%</span>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
```

## 3. User Account - `dark://user`

### Purpose
Manage user profile, preferences, authentication, and personal development statistics.

### URL Patterns
```
dark://user                                      # User account overview
dark://user/profile                              # Profile management
dark://user/preferences                          # IDE and workflow preferences
dark://user/auth                                 # Authentication settings
dark://user/statistics                           # Personal development stats
dark://user/instances                            # User's instance access
```

### User Account Content
```html
<!DOCTYPE html>
<html>
<head><title>User Account</title></head>
<body>
  <div class="user-page">
    <header>
      <div class="user-header">
        <div class="avatar">👤</div>
        <div class="user-info">
          <h1>Alice Johnson</h1>
          <p>alice@mycompany.com</p>
          <span class="role">Senior Developer</span>
        </div>
      </div>
      <div class="account-status">
        <span class="status">✅ Active</span>
        <span class="plan">Professional Plan</span>
        <span class="member-since">Member since Jan 2023</span>
      </div>
    </header>

    <div class="tabs">
      <button class="tab active" onclick="showTab('overview')">Overview</button>
      <button class="tab" onclick="showTab('profile')">Profile</button>
      <button class="tab" onclick="showTab('preferences')">Preferences</button>
      <button class="tab" onclick="showTab('instances')">Instances</button>
      <button class="tab" onclick="showTab('statistics')">Statistics</button>
      <button class="tab" onclick="showTab('auth')">Security</button>
    </div>

    <div id="overview-tab" class="tab-content">
      <div class="overview-grid">
        <div class="recent-activity">
          <h3>📊 Recent Activity</h3>
          <div class="activity-timeline">
            <div class="activity">
              <span class="time">2 hours ago</span>
              <span class="action">🔄 Started session: user-improvements</span>
            </div>
            <div class="activity">
              <span class="time">1 day ago</span>
              <span class="action">✅ Merged patch: api-fixes</span>
            </div>
            <div class="activity">
              <span class="time">2 days ago</span>
              <span class="action">📦 Published 5 new packages</span>
            </div>
            <div class="activity">
              <span class="time">3 days ago</span>
              <span class="action">👥 Joined collaborative session: team-refactoring</span>
            </div>
          </div>
        </div>

        <div class="quick-stats">
          <h3>🏆 This Week</h3>
          <div class="stats-grid">
            <div class="stat">
              <span class="value">23</span>
              <span class="label">Functions Created</span>
            </div>
            <div class="stat">
              <span class="value">8</span>
              <span class="label">Patches Merged</span>
            </div>
            <div class="stat">
              <span class="value">3</span>
              <span class="label">Sessions Completed</span>
            </div>
            <div class="stat">
              <span class="value">12</span>
              <span class="label">Conflicts Resolved</span>
            </div>
          </div>
        </div>

        <div class="current-work">
          <h3>🔄 Current Work</h3>
          <div class="work-items">
            <div class="work-item">
              <h4>user-improvements session</h4>
              <p>3 patches in progress, 2 conflicts to resolve</p>
              <button onclick="resumeWork('user-improvements')">Resume</button>
            </div>
            <div class="work-item">
              <h4>team-refactoring collaboration</h4>
              <p>4 patches pending your review</p>
              <button onclick="reviewWork('team-refactoring')">Review</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div id="preferences-tab" class="tab-content hidden">
      <div class="preferences-sections">
        <section class="preference-section">
          <h3>🎨 Editor Preferences</h3>
          <div class="preference">
            <label>Theme</label>
            <select>
              <option>Dark</option>
              <option selected>Light</option>
              <option>Auto (system)</option>
            </select>
          </div>
          <div class="preference">
            <label>Font Size</label>
            <input type="range" min="12" max="20" value="14" />
            <span class="value">14px</span>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" checked /> Show line numbers
            </label>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" checked /> Enable IntelliSense
            </label>
          </div>
        </section>

        <section class="preference-section">
          <h3>🔄 Workflow Preferences</h3>
          <div class="preference">
            <label>Auto-save interval</label>
            <select>
              <option>Never</option>
              <option>30 seconds</option>
              <option selected>1 minute</option>
              <option>5 minutes</option>
            </select>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" checked /> Auto-create patches on first edit
            </label>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" /> Show deprecated function warnings
            </label>
          </div>
        </section>

        <section class="preference-section">
          <h3>🔔 Notifications</h3>
          <div class="preference">
            <label>
              <input type="checkbox" checked /> Sync conflict notifications
            </label>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" checked /> Patch merge notifications
            </label>
          </div>
          <div class="preference">
            <label>
              <input type="checkbox" /> Weekly activity summary
            </label>
          </div>
        </section>
      </div>
    </div>

    <div id="statistics-tab" class="tab-content hidden">
      <div class="statistics-dashboard">
        <div class="stat-period">
          <button class="period-btn active" onclick="showPeriod('week')">This Week</button>
          <button class="period-btn" onclick="showPeriod('month')">This Month</button>
          <button class="period-btn" onclick="showPeriod('quarter')">This Quarter</button>
          <button class="period-btn" onclick="showPeriod('year')">This Year</button>
        </div>

        <div class="stats-overview">
          <div class="stat-card">
            <h4>📦 Packages Created</h4>
            <span class="stat-value">47</span>
            <span class="stat-change">+12% from last week</span>
          </div>
          <div class="stat-card">
            <h4>🔄 Sessions Completed</h4>
            <span class="stat-value">8</span>
            <span class="stat-change">+25% from last week</span>
          </div>
          <div class="stat-card">
            <h4>⚡ Lines of Code</h4>
            <span class="stat-value">1,247</span>
            <span class="stat-change">+8% from last week</span>
          </div>
          <div class="stat-card">
            <h4>🤝 Collaborations</h4>
            <span class="stat-value">5</span>
            <span class="stat-change">Same as last week</span>
          </div>
        </div>

        <div class="charts-section">
          <div class="chart">
            <h4>📈 Development Activity</h4>
            <!-- Chart visualization would go here -->
            <div class="chart-placeholder">Activity chart over time</div>
          </div>
          <div class="chart">
            <h4>🎯 Package Categories</h4>
            <!-- Pie chart of package types -->
            <div class="chart-placeholder">Package type distribution</div>
          </div>
        </div>

        <div class="achievements">
          <h4>🏆 Recent Achievements</h4>
          <div class="achievement-list">
            <div class="achievement">
              <span class="badge">🔥</span>
              <span class="title">Streak Master</span>
              <span class="description">7 days of continuous development</span>
            </div>
            <div class="achievement">
              <span class="badge">🤝</span>
              <span class="title">Team Player</span>
              <span class="description">Resolved 10 conflicts this week</span>
            </div>
            <div class="achievement">
              <span class="badge">🚀</span>
              <span class="title">Speed Demon</span>
              <span class="description">Completed session in record time</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
```

## 4. Package Search - `dark://search`

### Purpose
Comprehensive search across all packages with filtering, faceted search, and intelligent recommendations.

### URL Patterns
```
dark://search                                    # Search interface
dark://search?query=validate                     # Search with query
dark://search?query=validate&type=function       # Filtered search
dark://search?query=User&type=type               # Type search
dark://search?query=email&instance=prod          # Instance-specific search
dark://search/advanced                           # Advanced search interface
```

### Search Interface Content
```html
<!DOCTYPE html>
<html>
<head><title>Package Search</title></head>
<body>
  <div class="search-page">
    <header>
      <h1>🔍 Package Search</h1>
      <p>Find functions, types, and values across all your instances</p>
    </header>

    <div class="search-interface">
      <div class="search-input-section">
        <div class="search-box">
          <input type="text"
                 placeholder="Search for functions, types, values..."
                 value="validate"
                 id="search-input" />
          <button onclick="performSearch()" class="search-btn">🔍</button>
        </div>

        <div class="search-suggestions">
          <div class="suggestion">validate</div>
          <div class="suggestion">validateEmail</div>
          <div class="suggestion">validateUser</div>
          <div class="suggestion">ValidationError</div>
        </div>
      </div>

      <div class="search-filters">
        <div class="filter-group">
          <label>Type</label>
          <div class="filter-options">
            <label><input type="checkbox" checked /> Functions</label>
            <label><input type="checkbox" /> Types</label>
            <label><input type="checkbox" /> Values</label>
          </div>
        </div>

        <div class="filter-group">
          <label>Instance</label>
          <select>
            <option>All instances</option>
            <option selected>Production</option>
            <option>Staging</option>
            <option>Local Development</option>
          </select>
        </div>

        <div class="filter-group">
          <label>Visibility</label>
          <div class="filter-options">
            <label><input type="checkbox" checked /> Public</label>
            <label><input type="checkbox" checked /> Organization</label>
            <label><input type="checkbox" /> Private</label>
          </div>
        </div>

        <div class="filter-group">
          <label>Status</label>
          <div class="filter-options">
            <label><input type="checkbox" checked /> Current</label>
            <label><input type="checkbox" /> Deprecated</label>
            <label><input type="checkbox" /> Experimental</label>
          </div>
        </div>
      </div>
    </div>

    <div class="search-results">
      <div class="results-header">
        <h3>🎯 Search Results</h3>
        <span class="result-count">47 results for "validate" (functions)</span>
        <div class="sort-options">
          <label>Sort by:</label>
          <select>
            <option>Relevance</option>
            <option>Name</option>
            <option>Recent</option>
            <option>Usage count</option>
          </select>
        </div>
      </div>

      <div class="results-list">
        <div class="result-item featured">
          <div class="result-header">
            <h4>MyApp.User.validate</h4>
            <span class="result-type">Function</span>
            <span class="relevance-score">100%</span>
          </div>

          <div class="result-signature">
            <code>(User) → Result&lt;Bool, ValidationError&gt;</code>
          </div>

          <div class="result-description">
            Validates user data including email format, name length, and age range.
            Enhanced with detailed error reporting and comprehensive validation rules.
          </div>

          <div class="result-metadata">
            <span class="instance">🏭 Production</span>
            <span class="updated">Updated 2 hours ago</span>
            <span class="usage">Used by 15 functions</span>
            <span class="author">by alice@mycompany.com</span>
          </div>

          <div class="result-actions">
            <button onclick="viewItem('MyApp.User.validate')" class="primary">View</button>
            <button onclick="editItem('MyApp.User.validate')">Edit</button>
            <button onclick="copyReference('MyApp.User.validate')">Copy Reference</button>
            <button onclick="viewUsage('MyApp.User.validate')">Show Usage</button>
          </div>
        </div>

        <div class="result-item">
          <div class="result-header">
            <h4>Stdlib.Email.validate</h4>
            <span class="result-type">Function</span>
            <span class="relevance-score">95%</span>
          </div>

          <div class="result-signature">
            <code>(String) → Bool</code>
          </div>

          <div class="result-description">
            Standard library email validation using RFC 5322 specification.
            Production-ready implementation with comprehensive test coverage.
          </div>

          <div class="result-metadata">
            <span class="instance">📚 Standard Library</span>
            <span class="updated">Stable</span>
            <span class="usage">Used by 342 functions</span>
            <span class="author">by Darklang Team</span>
          </div>

          <div class="result-actions">
            <button onclick="viewItem('Stdlib.Email.validate')" class="primary">View</button>
            <button onclick="viewDocs('Stdlib.Email.validate')">Documentation</button>
            <button onclick="copyReference('Stdlib.Email.validate')">Copy Reference</button>
            <button onclick="viewExamples('Stdlib.Email.validate')">Examples</button>
          </div>
        </div>

        <div class="result-item">
          <div class="result-header">
            <h4>MyApp.User.ValidationError</h4>
            <span class="result-type">Type</span>
            <span class="relevance-score">87%</span>
          </div>

          <div class="result-signature">
            <code>Enum | InvalidEmail | InvalidName | InvalidAge | ...</code>
          </div>

          <div class="result-description">
            Comprehensive error type for user validation operations.
            Supports detailed error messages and error code classification.
          </div>

          <div class="result-metadata">
            <span class="instance">🏭 Production</span>
            <span class="updated">Updated 1 day ago</span>
            <span class="usage">Used by 8 functions</span>
            <span class="author">by alice@mycompany.com</span>
          </div>

          <div class="result-actions">
            <button onclick="viewItem('MyApp.User.ValidationError')" class="primary">View</button>
            <button onclick="editItem('MyApp.User.ValidationError')">Edit</button>
            <button onclick="viewUsage('MyApp.User.ValidationError')">Show Usage</button>
          </div>
        </div>
      </div>

      <div class="results-pagination">
        <button onclick="previousPage()" disabled>‹ Previous</button>
        <span class="page-info">Page 1 of 5</span>
        <button onclick="nextPage()">Next ›</button>
      </div>
    </div>

    <div class="search-suggestions-panel">
      <div class="related-searches">
        <h4>🔗 Related Searches</h4>
        <div class="suggestion-list">
          <button onclick="searchFor('email validation')">email validation</button>
          <button onclick="searchFor('user creation')">user creation</button>
          <button onclick="searchFor('input validation')">input validation</button>
          <button onclick="searchFor('error handling')">error handling</button>
        </div>
      </div>

      <div class="recommended-packages">
        <h4>💡 Recommended</h4>
        <div class="recommendation-list">
          <div class="recommendation">
            <span class="name">MyApp.Utils.Email</span>
            <span class="reason">Often used with validation functions</span>
          </div>
          <div class="recommendation">
            <span class="name">Stdlib.Result</span>
            <span class="reason">Common return type for validation</span>
          </div>
          <div class="recommendation">
            <span class="name">MyApp.User.create</span>
            <span class="reason">Uses validation functions</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</body>
</html>
```

## Integration with VS Code

### Tree View Integration
All these pages are accessible through the Darklang ViewContainer tree view:

```
🏢 Darklang
├── 📦 Packages
├── 🔄 Sessions
│   ├── 📊 All Sessions
│   ├── ⚡ Active Sessions
│   └── 📋 Recent Sessions
├── 🏢 Instances
│   ├── 🏭 Production
│   ├── 🧪 Staging
│   └── 💻 Local Development
├── 🔍 Search
├── 👤 Account
│   ├── 📊 Overview
│   ├── ⚙️ Preferences
│   └── 📈 Statistics
└── 🔧 Tools
```

### Command Palette Integration
```typescript
// VS Code commands for quick access
const commands = [
  'darklang.search.packages',          // Ctrl+Shift+P: Search Packages
  'darklang.sessions.new',             // Create New Session
  'darklang.sessions.resume',          // Resume Last Session
  'darklang.instances.sync',           // Sync All Instances
  'darklang.user.preferences',         // Open User Preferences
  'darklang.instances.browse'          // Browse Instances
]
```

These new URL/page designs provide comprehensive management and discovery capabilities that extend beyond the core package editing workflow, enabling users to effectively coordinate development across instances, sessions, and teams.

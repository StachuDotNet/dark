```bash
# Discovery & Setup
curl -sSL get.darklang.com | sh           # Install CLI
dark --help                               # See what's possible
dark examples list                        # Browse starter templates
dark new webapp my-blog --template simple-blog
cd my-blog

# Development Loop  
dark dev                                  # Start dev server with hot reload
# Opens editor, shows running app, traces, logs
# Edit handlers/api.dark in VS Code
# See immediate feedback in browser
# Traces show exactly what happened for each request

# Add functionality 
dark add auth                             # Add authentication system
dark add database posts                   # Add database with posts table
dark generate handler "GET /api/posts"    # Generate API endpoint
dark generate page "POST /admin/posts"    # Generate admin page

# Package Discovery
dark search "send email"                  # Find email packages
dark view Darklang.Email.send             # See function documentation
dark import Darklang.Email                # Add to project

# Testing & Debugging
dark test                                 # Run all tests
dark test --watch                         # Continuous testing
dark trace --filter "/api/posts"          # See traces for specific endpoint
dark logs --tail                          # Watch live logs

# Deployment
dark deploy                               # One-command deploy
dark status                               # Check deployment status
dark url                                  # Get live URL




# Package as binary
dark build --target binary               # Create standalone executable
dark publish                             # Share with community


dark fork Acme.JSON                     # Create your own version



# Team setup
dark team create my-startup
dark team invite alice@startup.com
dark clone team://my-startup/main-app   # Get team's main project

# Code review
dark review alice/user-profiles         # Review teammate's work
dark comment "line 42: consider error handling"
dark approve alice/user-profiles

# Deployment coordination
dark deploy {instance-name}



# Interactive learning
dark tutorial start                    # Interactive tutorial in CLI
dark examples run "hello-world"        # Run examples locally

# Documentation
dark docs Stdlib.List.map              # See function documentation
dark examples List.map                 # See usage examples

# Building understanding
dark explain "List.map fn list"        # AI explanation of code
dark why "type error on line 23"       # Get help with errors
```

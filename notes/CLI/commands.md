# CLI Commands

## Session
```bash
dark session list                    
dark session switch <name>           
dark session create <name>           
dark session create status           
```

## Patch
```bash
dark patch list                      
dark patch create <intent>           
dark patch validate        
dark patch show
dark patch show <id>
dark patch apply <id>
```


## Packages
```bash
dark package search <query>
dark package search <query> --return-type=Result<User, String>
dark package search <query> --depends-on=User
```

## Other
```bash
# likely should be updated
status
```

## Sync
(these still need a lot of work - these are just directional)
```bash
dark sync push
dark sync pull
dark sync status
```
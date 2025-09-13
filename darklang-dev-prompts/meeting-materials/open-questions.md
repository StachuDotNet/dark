# Open Questions for Discussion

## For Tomorrow's Coworker Meeting

### Architecture & Design
1. **Patch Mutability**: Should patches be immutable once created, or can we add ops until marked "ready"?
2. **Op Granularity**: One op per logical change vs one op per keystroke?
3. **Database Choice**: SQLite locally + PostgreSQL server, or just PostgreSQL everywhere?
4. **Sync Protocol**: HTTP polling vs WebSockets vs both?

### Developer Experience
1. **Default Behavior**: Should sync be automatic or manual by default?
2. **Conflict Resolution**: How much automation vs manual review?
3. **Branch Model**: Do we need branches or are patches enough?
4. **Testing Integration**: Should patches include tests or reference them?

### Implementation Priority
1. **CLI vs VS Code**: Which should we prioritize first?
2. **Online vs Offline**: Start online-only or build offline-first from beginning?
3. **Server Architecture**: Central server vs P2P vs hybrid?
4. **Package Manager Integration**: How tightly coupled with existing PM?

### Work Division
1. Who takes CLI implementation vs server?
2. Who handles VS Code extension updates?
3. Who writes tests and documentation?
4. How do we coordinate our own patches while building the patch system?

## For Sunday's Advisor Meeting

### Strategic Direction
1. **Scope**: Is this the right size problem to solve first?
2. **Differentiation**: How does this compare to other collaborative coding tools?
3. **Market Fit**: Does this address real developer pain points?
4. **Timeline**: Is 2-4 weeks realistic for MVP?

### Technical Approach
1. **CRDT Integration**: Should we use CRDTs now or add later?
2. **Type System**: How do patches interact with Darklang's type system?
3. **Performance**: What scale should we design for initially?
4. **Migration Path**: How do we migrate existing packages to patch-based model?

### Business Considerations
1. **Open Source**: What parts should be open vs closed?
2. **Community Involvement**: When/how to involve community developers?
3. **Documentation**: What level of docs needed for launch?
4. **Support Model**: How will we handle user issues?

### Future Vision
1. **AI Integration**: How does this enable AI pair programming?
2. **Enterprise Features**: What would enterprise customers need?
3. **Mobile/Tablet**: Should we consider non-desktop platforms?
4. **Education**: Could this simplify teaching programming?

## Technical Decisions Needed

### Immediate (Before Implementation)
- [ ] Serialization format for ops (JSON vs MessagePack vs custom)
- [ ] Authentication mechanism (tokens vs sessions)
- [ ] Patch ID format (UUID vs hash vs sequential)
- [ ] Validation timing (on create vs on apply)

### This Week
- [ ] Conflict resolution strategies
- [ ] Test integration approach
- [ ] Performance requirements
- [ ] Security model

### Next Phase
- [ ] Community features (comments, reviews)
- [ ] Integration with CI/CD
- [ ] Metrics and analytics
- [ ] Backup and recovery

## Risk Assessment Questions

1. **What if sync fails repeatedly?** Local queue with retry?
2. **What if patches corrupt package state?** Rollback mechanism?
3. **What if two devs claim same username?** Namespace per server?
4. **What if patch is too large?** Size limits or streaming?
5. **What if server goes down?** Local-only mode?

## Success Metrics to Define

1. **Performance**: Max time for patch apply? Sync latency?
2. **Reliability**: Acceptable failure rate?
3. **Usability**: How many commands to share code?
4. **Scale**: Number of concurrent developers?
5. **Quality**: Test coverage requirements?

## Demo Feedback Questions

### For Coworker
1. Does the flow make sense?
2. What's missing for you to use this daily?
3. Which conflicts worry you most?
4. How would you want to review patches?

### For Advisor
1. Does this demonstrate progress?
2. Is the architecture sound?
3. What risks concern you?
4. What would you show investors?

## Follow-up Actions

### After Coworker Meeting
- [ ] Revise architecture based on feedback
- [ ] Assign implementation tasks
- [ ] Set up development environment
- [ ] Create shared test scenarios

### After Advisor Meeting
- [ ] Update timeline and roadmap
- [ ] Address technical concerns
- [ ] Plan community announcement
- [ ] Schedule follow-up check-ins

These questions will guide productive discussions and ensure we make informed decisions about the collaboration system.
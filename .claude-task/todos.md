# Task Todos: TUI Component Library

## Research Summary

### External TUI Libraries Reviewed

1. **Charm/Bubble Tea (Go)** - Elm-like architecture (Init/Update/View), components via Bubbles library (Spinner, TextInput, TextArea, Table, Progress), styling via Lipgloss. Strong ecosystem with 10k+ apps built.

2. **FsSpectre (F#)** - F#-friendly wrapper for Spectre.Console with computation expressions for tables, panels, progress bars, charts, live displays.

3. **Terminal.Gui (.NET)** - Full-featured cross-platform TUI toolkit with forms, dialogs, data views, wizards, powerful layout engine, complete keyboard/mouse input handling.

4. **Ratatui (Rust)** - Widget-based architecture with Block, Canvas, Chart, BarChart, Table, List, Gauge. Split into modular crates (ratatui-core, ratatui-widgets).

### Key Design Principles from Research
- **Component architecture**: All libraries use composable widget/component patterns
- **State management**: Clear separation of model/state from rendering
- **Layout system**: Constraint-based or flex-style layouts are common
- **Styling**: Theming through centralized style definitions
- **Events**: Unified event handling for keyboard, mouse, focus

### Existing Darklang TUI Components (17 total in `packages/darklang/cli/ui/`)

**Core Infrastructure:**
- `core/types.dark` - Base types (Color, Size, Alignment, Position, Bounds, ComponentState, Component, RenderContext, Events)
- `core/rendering.dark` - Rendering utilities (colorize, text styling, box drawing, padding, truncation, focus indicators)

**Components (in `components/`):**
- Basic: button, label, textblock, divider
- Forms: forms (TextInput, Checkbox, RadioGroup, Select, Slider, DateField)
- Layout: layout, panel, card, modal
- Navigation: navigation, pagination, listview, dropdown
- Display: progress, statusbar, scrollbar, message

### Test Coverage Status
- ✅ Core rendering utilities
- ✅ Basic components (Label, TextBlock, Divider)
- ✅ Interactive components (Button, Progress)
- ❌ Forms components
- ❌ Layout components
- ❌ Complex components

---

## Implementation Plan

### Phase 1: Namespace Organization & Migration ✅ COMPLETE
- [x] Review current namespace structure in experiments/
- [x] Create new `Darklang.CLI.UI` namespace structure
- [x] Migrate core types and rendering utilities to Darklang.CLI.UI.Core
- [x] Migrate all 17 component modules to Darklang.CLI.UI.Components
- [x] Update all internal references to use new namespaces
- [x] Verify builds pass without errors

### Phase 2: Component Enhancement & Consistency
- [x] Audit all components for API consistency (create/render/update pattern)
- [x] Ensure consistent state management across components
- [x] Add box drawing style variants (Double, Rounded, Heavy, Dashed) to rendering module
- [ ] Enhance Label component with truncation and max-width options
- [ ] Enhance TextBlock with word-wrap support
- [x] Add Spinner component (missing from current set, common in all reviewed libraries)
- [x] Add Table component (common in Charm, FsSpectre, Terminal.Gui, Ratatui)

### Phase 3: Testing Infrastructure
- [x] Create test structure in backend/testfiles/execution/cli/
- [x] Write rendering tests for core utilities
- [x] Write tests for basic components (Label, TextBlock, Divider)
- [x] Write tests for interactive components (Button, Progress)
- [x] Write tests for form components (TextInput, Checkbox, RadioGroup, Select, Slider, DateField)
- [x] Write tests for Spinner component
- [x] Write tests for Table component
- [x] Write tests for layout components (Panel, TabPanel, FilterPanel, Card, MediaCard)
- [ ] Write tests for complex components (Modal, Dropdown)

### Phase 4: Documentation & Examples
- [ ] Document core architecture (Component model, rendering, state management)
- [ ] Create usage examples for key component types
- [ ] Add inline comments explaining patterns

### Phase 5: Advanced Features (Time Permitting)
- [ ] Implement theme system using RenderContext.theme field
- [ ] Add keyboard navigation state machine
- [ ] Add animation/transition support (Spinner animations)

---

## Current Progress

**Completed:**
- ✅ Phase 1: All components migrated to `Darklang.CLI.UI` namespace
- ✅ Core tests written and passing
- ✅ Basic and interactive component tests written
- ✅ Box drawing style variants (Single, Double, Rounded, Heavy, Dashed) added
- ✅ Spinner component created with 5 animation styles
- ✅ Table component created with row selection and styling
- ✅ Forms component tests written
- ✅ Spinner component tests written
- ✅ Table component tests written
- ✅ Layout component tests written (Panel, TabPanel, FilterPanel, Card, MediaCard)

**In Progress:**
- Phase 4: Documentation

**Next Actions:**
1. Document component patterns
2. Optionally enhance Label/TextBlock components
3. Write tests for Modal/Dropdown if time permits

---

## Success Criteria

- [x] All components migrated to Darklang.CLI.UI namespace
- [x] Consistent API patterns across all components
- [x] Test coverage for core rendering utilities
- [x] Test coverage for basic and interactive components
- [x] Test coverage for forms components
- [x] Spinner component added
- [x] Table component added
- [x] Box drawing style variants added
- [x] Test coverage for layout components

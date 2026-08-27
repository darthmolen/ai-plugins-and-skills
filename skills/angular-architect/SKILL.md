---
name: angular-architect
description: Expert Angular architect mastering Angular 20+ with enterprise patterns. Specializes in signals, RxJS, NgRx state management, micro-frontend architecture, and performance optimization with focus on building scalable enterprise applications.
allowed-tools: Read Write Edit Bash Glob Grep
metadata:
  category: domain-skills
---

# Angular Architect Skill

Strategic, checklist-driven guidance for Angular 20+ enterprise applications: architecture, state, performance, micro-frontends, testing, Nx, and signals. Pairs with `angular-expert` (which carries code-level patterns) — this skill informs higher-level decisions.

---

## Auto-Detection

This skill activates when:

- Designing Angular module, feature, or state architecture
- Setting up Nx monorepo or micro-frontend (module federation) workspaces
- Choosing performance budgets, testing strategy, or signals adoption
- Reviewing existing Angular projects for enterprise readiness

---

## Architect Checklist

- Angular 20+ features used properly (standalone-by-default, signals, native control flow)
- TypeScript strict mode enabled
- OnPush strategy on every component
- Bundle budgets configured
- Test coverage > 85%
- Accessibility AXE-clean and WCAG AA compliant
- Performance budgets met (initial load < 3s, route transitions < 200ms)

---

## Architecture

- Standalone component tree (no NgModules)
- Lazy-loaded routes via `loadComponent` / `loadChildren`
- Provider scoping via `providedIn` and route-level providers
- Functional guards and resolvers
- Functional HTTP interceptors via `provideHttpClient(withInterceptors([...]))`
- Application config via `ApplicationConfig` and `provide*` functions
- Feature folders with barrel exports for public surfaces

---

## Signals (v20+ default)

- Signal-first component state
- `input()` and `output()` functions (no `@Input` / `@Output` decorators)
- `computed()` for derived state
- `effect()` for side effects (sparingly)
- `signal.update()` / `signal.set()` only — never `mutate`
- RxJS interop via `toSignal()` / `toObservable()`
- Pure, predictable state transformations

---

## RxJS Mastery

- Observable composition patterns
- Subject selection (`BehaviorSubject` vs `ReplaySubject` vs `Subject`)
- Operator chains and operator authoring
- Error handling and recovery (`catchError`, `retry`)
- Memory management (`takeUntilDestroyed`, async pipe, completion semantics)
- Multicasting (`shareReplay`, `share`)
- Marble testing

---

## State Management

- NgRx with `createFeature`, `createActionGroup`, functional effects
- Selector optimization and memoization
- Entity adapters for collections
- Router state integration
- Redux DevTools integration
- Store testing strategies

---

## Enterprise Patterns

- Smart / dumb (container / presentational) components
- Facade pattern for feature boundaries
- Repository pattern for data access
- Service layer composition
- Dependency injection scoping
- Custom decorators
- Dynamic components and content projection

---

## Performance Optimization

- OnPush change detection
- `track` expressions in `@for` loops
- Virtual scrolling for large lists
- Lazy loading and preloading strategies
- Bundle analysis and size budgets
- Tree shaking
- Production AoT build configuration

---

## Micro-frontend Architecture

- Module federation
- Shell architecture
- Remote loading strategies
- Shared dependency management (singleton libraries)
- Cross-app communication patterns
- Independent deployment
- Version compatibility
- Testing across remotes

---

## Testing Strategies

- Unit, component, and service testing via `TestBed`
- E2E with Cypress or Playwright
- Marble testing for RxJS streams
- Store testing (NgRx)
- Visual regression
- Performance testing

---

## Nx Monorepo

- Workspace setup and library taxonomy (`feature`, `ui`, `data-access`, `util`)
- Module boundary rules (`@nx/enforce-module-boundaries`)
- `affected` commands for CI
- Build caching
- CI/CD integration
- Cross-app code sharing
- Dependency graph analysis

---

## Advanced Features

- Custom directives (structural, attribute)
- Dynamic component creation
- Pipe optimization (pure pipes, signals over impure)
- Typed reactive forms
- Animation API
- CDK usage (overlay, drag-drop, scrolling, a11y)

---

## Best Practices

- Angular style guide
- TypeScript strict mode
- No `@HostBinding` / `@HostListener` — use the `host` object on the decorator
- No `ngClass` / `ngStyle` — use `[class.x]` / `[style.x]` bindings
- Native control flow (`@if`, `@for`, `@switch`) over `*ngIf`, `*ngFor`, `*ngSwitch`
- `NgOptimizedImage` for static images (not inline base64)
- AXE-clean accessibility, WCAG AA minimums (focus, contrast, ARIA)
- ESLint configured
- Prettier formatting
- Conventional commits
- Semantic versioning
- Documentation kept current
- Thorough code reviews

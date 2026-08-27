---
name: angular-expert
description: "Angular 20+/TypeScript frontend expert. PROACTIVELY use when working with Angular components, signals, RxJS, NgRx, angular.json configuration, or ng serve issues."
allowed-tools: Read Grep Glob Edit Write
metadata:
  category: domain-skills
---

# Angular Expert Skill

Expert-level Angular 20+ patterns for components, signals, RxJS, state management, accessibility, and performance.

---

## Auto-Detection

This skill activates when:

- Working with Angular projects
- Detected `angular.json` or `@angular/core` in package.json
- Working with `*.component.ts`, `*.service.ts`, `*.directive.ts` files
- Using RxJS, NgRx, or Angular Material

---

## TypeScript Best Practices

- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid `any`; use `unknown` when type is uncertain

---

## 1. Component Best Practices

### Core Rules (Angular 20+)

- Always use standalone components.
- Must NOT set `standalone: true` inside Angular decorators — it is the default in v20+.
- Use `input()` and `output()` functions instead of `@Input()` and `@Output()` decorators.
- Use `signal()` for local state, `computed()` for derived state.
- Set `changeDetection: ChangeDetectionStrategy.OnPush` on every component.
- Do NOT use `@HostBinding` or `@HostListener` — use the `host` object on the decorator.
- Do NOT use `ngClass` — use `[class.foo]="..."` bindings.
- Do NOT use `ngStyle` — use `[style.foo]="..."` bindings.
- Use `NgOptimizedImage` for static images (does not work for inline base64).
- Prefer inline templates for small components.
- When using external templates/styles, use paths relative to the component TS file.
- Keep components small and focused on a single responsibility.

### Standalone Component

```typescript
// ✅ GOOD - v20+ standalone (no `standalone: true` flag)
@Component({
  selector: 'app-user-card',
  imports: [RouterLink, NgOptimizedImage],
  template: `
    <div class="user-card" [class.selected]="selected()">
      <img ngSrc="/assets/avatar.png" width="48" height="48" alt="">
      <h3>{{ user().name }}</h3>
      <p>{{ user().email }}</p>
      <a [routerLink]="['/users', user().id]">View Profile</a>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserCardComponent {
  user = input.required<User>();
  selected = input(false);
  pick = output<User>();
}
```

```typescript
// ❌ BAD - v20+ violations
@Component({
  selector: 'app-user-card',
  standalone: true,                  // ❌ remove — default in v20+
  imports: [CommonModule],
  template: `<div [ngClass]="{ active: isActive }">...</div>`, // ❌ use [class.active]
})
export class UserCardComponent {
  @Input({ required: true }) user!: User;        // ❌ use input.required<User>()
  @Output() pick = new EventEmitter<User>();     // ❌ use output<User>()

  @HostBinding('class.dark') isDark = false;     // ❌ use host: { '[class.dark]': 'isDark()' }
  @HostListener('click') onClick() {}            // ❌ use host: { '(click)': 'onClick()' }
}
```

### Signal-Based State

```typescript
// ✅ GOOD - signals + computed + OnPush
@Component({
  selector: 'app-counter',
  template: `
    <p>Count: {{ count() }}</p>
    <p>Double: {{ doubleCount() }}</p>
    <button type="button" (click)="increment()">+</button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CounterComponent {
  count = signal(0);
  doubleCount = computed(() => this.count() * 2);

  increment() {
    this.count.update(c => c + 1);
  }
}
```

### Host Bindings via `host` Object

```typescript
// ✅ GOOD - host object replaces @HostBinding / @HostListener
@Component({
  selector: 'app-toggle',
  template: `<ng-content />`,
  host: {
    '[class.active]': 'active()',
    '[attr.aria-pressed]': 'active()',
    '(click)': 'toggle()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToggleComponent {
  active = signal(false);

  toggle() {
    this.active.update(v => !v);
  }
}
```

### Smart vs Dumb Components

```typescript
// ✅ GOOD - container (smart)
@Component({
  selector: 'app-users-container',
  imports: [UserListComponent],
  template: `
    <app-user-list
      [users]="users()"
      [loading]="loading()"
      (userSelected)="onUserSelected($event)"
    />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersContainerComponent {
  private userService = inject(UserService);

  users = signal<User[]>([]);
  loading = signal(false);

  constructor() {
    this.loadUsers();
  }

  private async loadUsers() {
    this.loading.set(true);
    this.users.set(await this.userService.getUsers());
    this.loading.set(false);
  }

  onUserSelected(user: User) {
    this.userService.selectUser(user);
  }
}

// ✅ GOOD - presentational (dumb)
@Component({
  selector: 'app-user-list',
  template: `
    @if (loading()) {
      <div class="loading">Loading…</div>
    } @else {
      <ul>
        @for (user of users(); track user.id) {
          <li (click)="userSelected.emit(user)">{{ user.name }}</li>
        }
      </ul>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserListComponent {
  users = input<User[]>([]);
  loading = input(false);
  userSelected = output<User>();
}
```

---

## 2. Services & Dependency Injection

- Design services around a single responsibility.
- Use `providedIn: 'root'` for singleton services.
- Use the `inject()` function instead of constructor injection.

```typescript
// ✅ GOOD - Service with inject()
@Injectable({ providedIn: 'root' })
export class UserService {
  private http = inject(HttpClient);
  private baseUrl = inject(API_BASE_URL);

  getUsers(): Observable<User[]> {
    return this.http.get<User[]>(`${this.baseUrl}/users`);
  }

  getUser(id: string): Observable<User> {
    return this.http.get<User>(`${this.baseUrl}/users/${id}`);
  }

  createUser(user: CreateUserDto): Observable<User> {
    return this.http.post<User>(`${this.baseUrl}/users`, user);
  }
}
```

### Injection Tokens

```typescript
// ✅ GOOD - Injection tokens for config
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

// In app.config.ts
export const appConfig: ApplicationConfig = {
  providers: [
    { provide: API_BASE_URL, useValue: environment.apiUrl },
  ],
};
```

---

## 3. Templates & Control Flow

- Keep templates simple — avoid complex logic.
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`.
- Use the `async` pipe to handle observables.
- Do not assume globals like `new Date()` are available in templates.

```html
<!-- ✅ GOOD - native control flow + async pipe -->
@if (user$ | async; as user) {
  <app-user-card [user]="user" />
} @else {
  <p>No user loaded.</p>
}

@for (item of items(); track item.id) {
  <app-item [item]="item" />
} @empty {
  <p>No items.</p>
}
```

---

## 4. RxJS Best Practices

### Declarative Streams

```typescript
// ✅ GOOD - bridging RxJS and signals
@Component({...})
export class UsersComponent {
  private userService = inject(UserService);
  private route = inject(ActivatedRoute);

  private userId = toSignal(
    this.route.paramMap.pipe(map(params => params.get('id')))
  );

  user = toSignal(
    toObservable(this.userId).pipe(
      filter((id): id is string => id != null),
      switchMap(id => this.userService.getUser(id)),
    )
  );
}
```

### Error Handling

```typescript
// ✅ GOOD - catchError with recovery
getUsers(): Observable<User[]> {
  return this.http.get<User[]>('/api/users').pipe(
    retry({ count: 3, delay: 1000 }),
    catchError(error => {
      console.error('Failed to fetch users', error);
      return of([]);
    }),
  );
}
```

### Unsubscribe Patterns

```typescript
// ✅ GOOD - takeUntilDestroyed
@Component({...})
export class MyComponent {
  private destroyRef = inject(DestroyRef);

  ngOnInit() {
    this.someObservable$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(value => {
        // handle value
      });
  }
}

// ✅ GOOD - async pipe (auto-unsubscribes)
@Component({
  template: `
    @if (users$ | async; as users) {
      <app-user-list [users]="users" />
    }
  `,
})
export class UsersComponent {
  users$ = this.userService.getUsers();
}
```

---

## 5. State Management

### Signals (Local State)

- Use signals for local component state.
- Use `computed()` for derived state.
- Keep state transformations pure and predictable.
- Do NOT use `mutate` on signals — use `update` or `set`.

### NgRx (Global State)

```typescript
// ✅ GOOD - NgRx feature with createFeature
export const usersFeature = createFeature({
  name: 'users',
  reducer: createReducer(
    initialState,
    on(UsersActions.loadUsers, state => ({ ...state, loading: true })),
    on(UsersActions.loadUsersSuccess, (state, { users }) => ({
      ...state,
      users,
      loading: false,
    })),
    on(UsersActions.loadUsersFailure, (state, { error }) => ({
      ...state,
      error,
      loading: false,
    })),
  ),
});

export const {
  selectUsers,
  selectLoading,
  selectError,
} = usersFeature;
```

```typescript
// ✅ GOOD - createActionGroup
export const UsersActions = createActionGroup({
  source: 'Users',
  events: {
    'Load Users': emptyProps(),
    'Load Users Success': props<{ users: User[] }>(),
    'Load Users Failure': props<{ error: string }>(),
    'Select User': props<{ userId: string }>(),
  },
});
```

```typescript
// ✅ GOOD - Functional effects
export const loadUsers = createEffect(
  (actions$ = inject(Actions), userService = inject(UserService)) => {
    return actions$.pipe(
      ofType(UsersActions.loadUsers),
      exhaustMap(() =>
        userService.getUsers().pipe(
          map(users => UsersActions.loadUsersSuccess({ users })),
          catchError(error =>
            of(UsersActions.loadUsersFailure({ error: error.message }))
          ),
        ),
      ),
    );
  },
  { functional: true },
);
```

---

## 6. Forms

- Prefer Reactive forms over Template-driven forms.
- Use `NonNullableFormBuilder` for typed reactive forms.

```typescript
// ✅ GOOD - typed reactive forms
@Component({...})
export class UserFormComponent {
  private fb = inject(NonNullableFormBuilder);

  form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    name: ['', [Validators.required, Validators.minLength(2)]],
    password: ['', [Validators.required, Validators.minLength(8)]],
  });

  onSubmit() {
    if (this.form.valid) {
      const value = this.form.getRawValue();
      // value: { email: string; name: string; password: string }
      this.save(value);
    }
  }
}
```

```html
<form [formGroup]="form" (ngSubmit)="onSubmit()">
  <div>
    <label for="email">Email</label>
    <input id="email" formControlName="email" type="email">
    @if (form.controls.email.errors?.['required']) {
      <span class="error">Email is required</span>
    }
    @if (form.controls.email.errors?.['email']) {
      <span class="error">Invalid email format</span>
    }
  </div>

  <button type="submit" [disabled]="form.invalid">Submit</button>
</form>
```

---

## 7. Routing

- Implement lazy loading for feature routes.
- Use functional guards, resolvers, and interceptors.

```typescript
// ✅ GOOD - lazy loaded routes
export const routes: Routes = [
  {
    path: 'users',
    loadComponent: () => import('./users/users.component').then(m => m.UsersComponent),
    children: [
      {
        path: ':id',
        loadComponent: () => import('./users/user-detail.component').then(m => m.UserDetailComponent),
      },
    ],
  },
];
```

```typescript
// ✅ GOOD - functional guard
export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login'], {
    queryParams: { returnUrl: state.url },
  });
};
```

```typescript
// ✅ GOOD - functional resolver
export const userResolver: ResolveFn<User> = (route) => {
  const userService = inject(UserService);
  const userId = route.paramMap.get('id')!;
  return userService.getUser(userId);
};
```

---

## 8. Performance Optimization

```typescript
// ✅ GOOD - OnPush + track + @defer
@Component({
  template: `
    @for (user of users(); track user.id) {
      <app-user-card [user]="user" />
    }

    @defer (on viewport) {
      <app-heavy-component />
    } @placeholder {
      <div class="skeleton"></div>
    } @loading {
      <div class="spinner"></div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyComponent {
  users = input<User[]>([]);
}
```

---

## 9. HTTP Interceptors

```typescript
// ✅ GOOD - functional interceptor
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();

  if (token) {
    req = req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    });
  }

  return next(req);
};

// In app.config.ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideHttpClient(withInterceptors([authInterceptor])),
  ],
};
```

---

## 10. Accessibility (Required)

Code MUST pass all AXE checks and meet WCAG AA minimums. This is non-negotiable.

Hard requirements:

- **Semantic HTML**: use `<button>`, `<nav>`, `<main>`, `<header>`, `<footer>` over `<div>` with click handlers.
- **Focus management**: every interactive element must be keyboard reachable; manage focus on route change and dialog open/close.
- **Color contrast**: text contrast ≥ 4.5:1 (normal), ≥ 3:1 (large). Verify with AXE / Lighthouse.
- **ARIA attributes**: label icon-only buttons (`aria-label`), associate labels (`for`/`id`), use `aria-live` for dynamic announcements.
- **Visible focus indicator**: never remove `:focus-visible` outline without replacing it.
- **Form errors**: associate with `aria-describedby` and use `role="alert"` for live error announcements.

```html
<!-- ✅ GOOD - accessible icon button -->
<button type="button" aria-label="Close dialog" (click)="close()">
  <svg aria-hidden="true" focusable="false">…</svg>
</button>

<!-- ✅ GOOD - associated label + error -->
<label for="email">Email</label>
<input id="email" formControlName="email" type="email"
       [attr.aria-invalid]="form.controls.email.invalid"
       aria-describedby="email-error">
@if (form.controls.email.invalid) {
  <span id="email-error" role="alert">Enter a valid email.</span>
}
```

```html
<!-- ❌ BAD -->
<div (click)="close()">×</div>             <!-- not keyboard-reachable, no name -->
<button (click)="close()">×</button>        <!-- no accessible name -->
<input formControlName="email">             <!-- no label -->
```

---

## 11. Testing

```typescript
// ✅ GOOD - component testing (no `standalone: true` needed in v20+)
describe('UserCardComponent', () => {
  let fixture: ComponentFixture<UserCardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(UserCardComponent);
  });

  it('should display user name', () => {
    fixture.componentRef.setInput('user', { id: '1', name: 'John', email: 'j@x.com' });
    fixture.detectChanges();

    const nameElement = fixture.nativeElement.querySelector('h3');
    expect(nameElement.textContent).toContain('John');
  });

  it('should emit when picked', () => {
    const component = fixture.componentInstance;
    fixture.componentRef.setInput('user', { id: '1', name: 'John', email: 'j@x.com' });
    jest.spyOn(component.pick, 'emit');

    fixture.nativeElement.querySelector('.user-card').click();

    expect(component.pick.emit).toHaveBeenCalled();
  });
});
```

---

## Quick Reference

```toon
checklist[16]{rule,enforcement}:
  Standalone,Default in v20+ - never set standalone: true
  Inputs/Outputs,Use input() / output() functions not @Input/@Output
  Host bindings,Use host: {} object not @HostBinding/@HostListener
  Class bindings,Use [class.x] not [ngClass]
  Style bindings,Use [style.x] not [ngStyle]
  Images,Use NgOptimizedImage for static assets
  Change detection,OnPush on every component
  State,signal() local + computed() derived + NgRx global
  Signal mutation,update() or set() never mutate()
  Templates,Native @if/@for/@switch + async pipe
  DI,inject() function with providedIn: 'root'
  Forms,Reactive + NonNullableFormBuilder
  Routes,Lazy loadComponent + functional guards
  HTTP,Functional interceptors via provideHttpClient
  RxJS,takeUntilDestroyed or async pipe
  Accessibility,AXE clean + WCAG AA + semantic HTML + ARIA
```

---

**Version:** 2.0.0

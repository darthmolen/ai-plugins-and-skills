# Angular state, forms, routing and HTTP interceptors

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

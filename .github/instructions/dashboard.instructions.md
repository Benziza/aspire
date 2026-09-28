---
applyTo: "src/Aspire.Dashboard/**/*.{cs,razor,js}"
---

# Dashboard agent instructions

## Reviewing

- Dashboard subscription/watch callbacks can run concurrently; protect shared mutable state with locking or concurrent collections.
- Prefer FluentUI for standard interactive controls. Raw HTML is fine when semantics, performance, or UX require it; if working around a FluentUI limitation, cite the FluentUI issue.
- Use `ViewportInformation.IsDesktop` / `IsUltraLowHeight` cascading parameter for responsive layout; throttle (not debounce) resize events to avoid excessive re-renders of the entire component tree.
- Use `@onclick:stopPropagation="true"` on interactive elements inside `FluentDataGrid` rows that have row-click handlers to prevent unintended navigation.
- Prefer JS interop for browser-only, latency-sensitive interactions (clipboard, global DOM listeners). If you register a persistent JS listener, keep a handle and unregister in `DisposeAsync`.
- For high-throughput log/trace/metric streams with a fixed cap, use `CircularBuffer<T>` instead of repeatedly removing the first item from a `List<T>`.
- For bounded channels feeding one consumer, prefer `BoundedChannelFullMode.DropOldest` and set `SingleReader = true`.
- Use `FormatHelpers` for culture-aware date/time/number display. Reserve invariant formatting for intentionally fixed diagnostic formats.
- Localize user-visible dashboard text with resource-backed localizers. Prefer typed localizers and `nameof` keys when practical, but existing model/helpers also generate localized UI text.

### Blazor components

- Avoid `@code` blocks and substantial C# logic in `.razor` files. Keep `.razor` files focused on markup, directives, and simple binding or event expressions; put component state, lifecycle methods, event handlers, and other logic in the matching `.razor.cs` code-behind partial class for better IDE and compiler support.
- Declare injected component dependencies as public, required, init-only properties:

	```csharp
	[Inject]
	public required IDashboardClient DashboardClient { get; init; }
	```

- `public` keeps dependencies visible to component and test infrastructure, `required` expresses that the component cannot operate without the service, and `init` prevents reassignment after component activation.
- Do not use non-public injected properties, mutable `set` accessors, or null-forgiving initializers such as `= null!;`. These weaken compile-time validation and hide missing dependencies when components are constructed in tests.

## Automated local development testing

### Rebuilding the dashboard

- When the dashboard is running in an AppHost as the `aspire-dashboard` project resource (`Projects.Aspire_Dashboard`) and **only the dashboard project changed**, use its **Rebuild** command instead of restarting the whole AppHost:

	```powershell
	aspire resource aspire-dashboard rebuild --apphost <apphost-path> --non-interactive
	aspire wait aspire-dashboard --apphost <apphost-path> --non-interactive
	```

- Select the exact running AppHost path when multiple AppHosts exist. The rebuild stops the dashboard project, builds it, and starts it again; a brief dashboard browser disconnect is expected. Check the command result and wait for the resource to become healthy before testing the UI.
- The built-in dashboard executable is not a project resource and does not expose this Rebuild command. If the AppHost, `Aspire.Hosting`, or **any other project** changed, restart the AppHost through the normal lifecycle workflow instead of only rebuilding the dashboard; the running AppHost will not load those changes from a dashboard rebuild.

### Browser verification with Playwright

- Start the exact AppHost using the normal lifecycle workflow and wait for `aspire-dashboard` to be healthy before opening it in Playwright. Do not guess the dashboard port. Choose one of these local-development authentication approaches:
  - **Authenticated (default):** Run `aspire ps --format json --non-interactive`, find the running entry whose `appHostPath` matches the selected AppHost, and pass its `dashboardUrl` directly to Playwright (for example, `await page.goto(dashboardUrl)`). The URL includes `/login?t=<token>` when browser-token authentication is enabled; visiting it establishes the browser session. Treat the full URL as a credential: do not commit it, paste it into reports, or include it in screenshots or logs.
  - **Anonymous local AppHost:** Set `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` **before starting** the AppHost. For a CLI-managed launch, set it in the same shell as `aspire start`; for an editor-managed launch, set it in the AppHost's launch environment before starting through the editor:

    ```powershell
    $env:ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS = "true"
    aspire start --apphost <apphost-path> --non-interactive
    Remove-Item Env:ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS
    ```

    Use this only on a trusted local development machine. `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` does **not** disable dashboard authentication. Get the URL for this AppHost from `aspire ps --format json --non-interactive`; in anonymous mode, `dashboardUrl` is the base URL without a login token.
- Open the selected `dashboardUrl` in a Playwright browser context, assert the dashboard loaded, and exercise the changed UI. For example, in a Playwright test with `page` and `expect` available:

	```typescript
	await page.goto(dashboardUrl);
	await expect(page.getByRole('heading', { name: 'Resources' })).toBeVisible();
	```

  After a dashboard rebuild, wait for `aspire-dashboard` again and reload the Playwright page to reconnect to the restarted dashboard.

# RoleManagement Feature - Blazor Interactive Server

## Overview

The RoleManagement feature provides a web UI for managing roles, permissions, and endpoint mappings using **Blazor Interactive Server** rendering mode.

## Architecture

- **Rendering Mode**: Blazor Interactive Server (requires WebSocket/SignalR)
- **Root Component**: `RoleManagementApp.razor`
- **Main Page**: `RoleManagementPage.razor` (route: `/role-management`)
- **State Management**: `RoleManagementState` (scoped per circuit)
- **Circuit Guard**: `RoleManagementCircuitGuard` (singleton) - ensures only one admin can edit at a time

## JavaScript Requirement ⚠️

**IMPORTANT**: This feature uses **Blazor Interactive Server** which **REQUIRES** JavaScript to function:

- `_framework/blazor.web.js` - Blazor framework file (NOT custom JavaScript)
- WebSocket connection for SignalR communication
- This is a **standard Blazor requirement**, not custom code

### Why JavaScript is Required

Blazor Interactive Server uses:
1. **SignalR over WebSocket** - real-time communication between browser and server
2. **DOM diffing** - efficient UI updates without full page reloads
3. **Event handling** - @onclick, @onchange, etc.

**Without `blazor.web.js`, the page will be blank or non-functional.**

If you see `content.js` in browser network tab, that's likely a browser extension, not part of this app.

## Components

```
Features/RoleManagement/
├── Components/
│   ├── RoleManagementApp.razor      # Root component with HTML structure
│   ├── MainLayout.razor              # Layout with inline CSS
│   ├── RolesList.razor               # Role list with search
│   ├── RoleEditor.razor              # Create/Edit role form
│   ├── PermissionSelector.razor      # Permission checkboxes
│   ├── EndpointMapper.razor          # Endpoint permission mapping
│   ├── Toast.razor                   # Success/Error notifications
│   └── ConfirmDialog.razor           # Confirmation modal
├── Pages/
│   ├── RoleManagementPage.razor      # Main page (@page "/role-management")
│   └── RoleManagementPageWrapper.razor # Test wrapper
├── Services/
│   ├── IRoleManagementService.cs     # Service interface
│   ├── RoleManagementService.cs      # Implementation (delegates to Forge.Core)
│   ├── RoleManagementState.cs        # State management with CancellationToken
│   └── RoleManagementCircuitGuard.cs # Exclusive editing lock
├── Mappers/
│   ├── RoleMappers.cs                # Role DTO mapping
│   ├── PermissionMappers.cs          # Permission DTO mapping
│   └── EndpointMappers.cs            # Endpoint mapping
└── RoleManagementFeatureExtensions.cs # DI registration
```

## Registration

In `Program.cs`:

```csharp
// Services
builder.Services.AddRoleManagementFeature(builder.Configuration);

// Authorization policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Constants.RoleManagementAccessPolicyName, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(Constants.RoleManagementAccessIdClaim);
    });
});

// Endpoints
app.MapRoleManagementFeature(); // Protected by RoleManagementAccessPolicyName
```

## Configuration

The access token is required; the gateway fails to start without it:

```json
{
  "RoleManagement": {
    "AccessToken": "<the access token>",
    "CookieExpirationMinutes": 60,
    "CookieName": "RoleManagementAuth"
  }
}
```

- Set the token through the environment (`RoleManagement__AccessToken`), never in source control. Use a
  random value of at least 32 characters.
- The console needs no IdP client and no other setting.
- Under the Aspire AppHost, the secret parameter `RoleManagementAccessToken` generates the token and passes
  it as `RoleManagement__AccessToken`.

## Authentication & Authorization

The console is opened with the shared access token; see [AUTHENTICATION.md](AUTHENTICATION.md) for the full
flow and the audit records.

1. **Sign-in**: open `/role-management?t=<token>`. A valid token gets the `RoleManagementAuth` cookie and a
   redirect to `/role-management` without the query. A missing or wrong token answers `404`.
2. **Session**: the cookie slides within `CookieExpirationMinutes`. An invalid or expired cookie answers
   `401`; open the token link again.
3. **Sign-out**: the page's "Sign out" button posts to `/role-management/signout`, which removes the cookie.

- **Policy**: `RoleManagementAccessPolicyName`, requiring the `rm_access_id` claim
- **Scheme**: `RoleManagementCookie`
- **Local access**: the "🛠️ Forge management" link in the Aspire dashboard carries the token

## Circuit Guard (Exclusive Editing)

- **Singleton service** - shared across all circuits
- **Lock timeout**: 30 minutes
- **Behavior**: Only one admin can edit roles at a time
- **UI**: Shows "Session Locked" message when another admin is editing

## Testing

Tests are located in `Tests/Modules/Services/Gateway/UserOrg.Gateway.Tests/`:

- **Unit Tests**: `RoleManagementServiceUnitTests.cs`
- **State Tests**: `Features/RoleManagement/RoleManagementStateTests.cs`
- **Circuit Guard Tests**: `Features/RoleManagement/RoleManagementCircuitGuardTests.cs`
- **Component Tests**: `Features/RoleManagement/Components/*Tests.cs` (bUnit)

Run tests:
```bash
dotnet test --filter "FullyQualifiedName~RoleManagement"
```

All 114+ tests passing ✅

## Styling

All CSS is **inline** in `MainLayout.razor` using the `.rm-*` class prefix:
- `.rm-container`, `.rm-card`, `.rm-grid` - layout
- `.rm-button`, `.rm-input`, `.rm-select` - form controls
- `.rm-toast`, `.rm-dialog` - overlays
- Gradient background, glassmorphism effects
- Responsive grid (1 column on mobile)

## Troubleshooting

### Blank Page
**Cause**: `blazor.web.js` not loaded or missing HTML structure in `RoleManagementApp.razor`

**Solution**: Ensure `RoleManagementApp.razor` contains:
```razor
<!DOCTYPE html>
<html>
<head>
    <HeadOutlet />
</head>
<body>
    <Router>...</Router>
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

### "Session Locked" Message
**Cause**: Another admin is currently editing roles (circuit guard active)

**Solution**: Wait for the other session to finish or timeout (30 minutes)

### Authorization Error (401/403)
**Cause**: User doesn't have `rm_access_id` claim

**Solution**: Ensure authentication provides the required claim

## Development

To run locally:
```bash
dotnet run --project src/Modules/Services/Gateway/UserOrg.Gateway
```

Navigate to: `https://localhost:7000/role-management` (or configured port)

## Architecture Decisions

- **Blazor Interactive Server** chosen for real-time updates and server-side validation
- **Circuit Guard** prevents concurrent editing conflicts
- **Inline CSS** keeps component self-contained (no external stylesheet)
- **CancellationToken** properly propagated for async operations
- **Result<T, Error>** pattern for error handling (from Lukdrasil library)

## Future Enhancements

- [ ] Role history/audit log
- [ ] Bulk permission updates
- [ ] Permission search/filter
- [ ] Export/Import roles
- [ ] Role templates

---

**Last Updated**: 2026-02-05
**Status**: ✅ Production Ready

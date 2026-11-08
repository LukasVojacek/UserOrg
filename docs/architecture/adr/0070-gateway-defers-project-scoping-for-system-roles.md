# Gateway defers project scoping for system roles

The gateway enforces project membership for project-scoped endpoints (route contains `ProjectShortName`) by checking the caller's `UserProjectShortNames`. System roles — Platform Administrator (`Admin`) and Provider Administrator (`ProviderAdmin`) — are cross-project by design and hold no project membership, so this check produced a 403 for them on legitimate cross-project endpoints (e.g. project-resource assignment).

We decided the gateway will **skip** the project-membership check for system roles by default, leaving fine-grained project scoping to the API (which receives the role and project records in the internal token). A global switch `GatewayAuthorization:EnforceProjectMembershipForSystemRoles` (default `false`) re-enables gateway-side enforcement for system roles. Absent configuration keeps the default.

This is recorded because a future reader will see admins bypassing the membership gate and may try to "fix" it by re-adding the check — that would reintroduce the 403. The non-membership permission-key check still applies to everyone, and project roles are still constrained to their own projects.

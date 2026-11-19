# Supabase accounts and Admin API

All routes below start with `/api/v1`. Application roles are read from `asx.app_users`, not editable Supabase user metadata. Credentials are stored by Supabase Auth; no password column is added to `app_users`.

## Local configuration

Use .NET user-secrets or deployment environment variables for `Auth:ProjectUrl`, `Auth:AnonKey`, `ConnectionStrings:Supabase`, and `Auth:Mode=Supabase`. Optional `Auth:PasswordResetRedirectUrl` must be an allowed redirect in the Supabase dashboard. Never commit keys, passwords or real account records.

Apply the additive `database/migrations/115_review2_auth_admin.sql` after migrations 001-114. It normalizes uniqueness of email and protects the last active administrator, including database writes outside the API. It also enables account administration audit events. API authentication reloads the stored role and active flag for each token request.

## Account routes

| Method | Path | Body | Response |
|---|---|---|---|
| POST | `/auth/register` | `{email,password,displayName,role}` | 201 `{user,session,requiresEmailConfirmation,message}` |
| POST | `/auth/login` | `{email,password}` | `AuthTokenResponse` |
| POST | `/auth/refresh` | `{refreshToken}` | `AuthTokenResponse` |
| POST | `/auth/forgot-password` | `{email}` | Generic `{message}`; does not disclose account existence |
| POST | `/auth/reset-password` | `{accessToken,newPassword}` | `{message}` after Supabase validates recovery token |
| GET | `/auth/me` | Bearer token | Current database profile |
| PATCH | `/auth/profile` | `{displayName}` + bearer | Current database profile |
| POST | `/auth/change-password` | `{currentPassword,newPassword}` + bearer | `{message}` after current password verification |

Passwords must have 8-128 characters. Display names have 1-120 characters. Self-registration accepts exactly `SCENARIO_ENGINEER`, `TEST_ENGINEER`, or `TEST_REVIEWER`; `ADMIN` and unknown roles are rejected before calling Supabase. Email is trimmed and normalized to lowercase.

When email confirmation is enabled, `session` is null and `requiresEmailConfirmation` is true. The frontend must show the confirmation instruction and must not fabricate a login session. Supabase controls recovery-link lifetime; record the actual provider setting in the final SRS/test environment. Existing Supabase accounts without an application profile are provisioned with a safe non-Admin default, never a role copied from `user_metadata`.

## Administration

`GET /admin/users?search=&role=&isActive=&page=1&pageSize=25` returns `{items,page,pageSize,totalCount,totalPages}`. `PATCH /admin/users/{id}` accepts `{role?,isActive?}` and returns the updated profile. These endpoints require an active `ADMIN` and also check permissions in the service. Admins cannot remove their own access or disable/demote the last active Admin. Changes are saved to the database and audited. Blocking a user revokes application access even when the external JWT has not yet expired.

## Verification

The `AccountServiceTests` fake only the outbound Supabase HTTP transport and verify requests, response handling, confirmation/no-session behavior, duplicate email, invalid roles, recovery-token verification, password changes and provider/network failures. `AdminUsersServiceTests` and `UserProvisioningTests` verify active status and application roles. `ScenarioLifecycleHttpTests` exercise real HTTP routing, policy checks and application services with isolated repository storage. These are not live Supabase end-to-end evidence.

For live acceptance, register a fresh test address on the frontend, confirm the email if required, sign in, reload `/auth/me`, change the profile, then have an Admin change/block that account and verify the next API request is rejected. Test recovery using an actual configured redirect and record only sanitized run identifiers/screenshots.

References: [Supabase signup](https://supabase.com/docs/reference/javascript/auth-signup), [password recovery](https://supabase.com/docs/reference/javascript/auth-resetpasswordforemail), [update user](https://supabase.com/docs/reference/javascript/auth-updateuser).

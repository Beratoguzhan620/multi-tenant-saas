# Roadmap

A multi-tenant help desk. Companies (tenants) sign up, invite their team and answer their customers' tickets.
The point of the project is the SaaS foundation underneath: tenant isolation that holds even when the code makes
a mistake, plans with limits that hold under concurrency, and roles checked on every request.

## Decisions

- **Shared tables, isolated twice.** Every tenant table has a `tenant_id`. The application filters by it with EF
  Core global query filters, and PostgreSQL row-level security enforces the same rule underneath: the API connects
  as a role that cannot see another tenant's rows even with a hand-written query.
- **The token names the organization, the database decides access.** Tokens carry the user and the active
  organization; membership and role are checked on every request, so removing someone takes effect at once.
- **One person, several organizations.** An account can belong to several organizations and switch between them.
- **Limits are counters, not counts.** Monthly usage is a counter row incremented with a conditional `UPDATE`, so
  twenty requests at once cannot all slip under the limit.
- **Per-organization numbers.** Tickets are numbered per organization (#1001, #1002, …) like in real help desks.
- **React admin panel** and a public support form per organization.

## Stages

| # | Stage | Contents |
|---|---|---|
| 1 | Skeleton | Organizations, accounts, memberships and roles, sign-up, sign-in, switching organization, Compose, CI |
| 2 | Isolation and tickets | Tenant context, global filters, row-level security with a separate database role, tickets with per-organization numbers, replies and internal notes, the public support form, isolation tests |
| 3 | Plans and limits | Invitations and roles, plans (Free, Team, Business), atomic usage counters, attachments with a storage quota, changing plans with downgrade checks, audit log, per-organization rate limit |
| 4 | Web panel | React and TypeScript: sign-up, tickets, team, plan and usage, the public support page; Playwright tests |
| 5 | Load and docs | A load test with many organizations at once that checks nothing leaks, README in English and Turkish, screenshots and GIF, portfolio card |

## Later

- Real billing through Stripe behind the same plan interface.
- Email-in: tickets created from incoming mail.
- Single sign-on per organization.

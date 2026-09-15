# Change management

LeaseVault follows a lightweight ITIL-style process: every production change is described in an
RFC, reviewed, released through the pipeline's `dev` -> `prod` environments, and has a tested
rollback path.

## Change classes

| Class | Examples | Approval |
|-------|----------|----------|
| Standard | dependency bumps, copy changes, new retention policy values | pre-approved; pipeline gates only |
| Normal | new features, schema changes, pipeline changes | RFC reviewed by tech lead + product owner |
| Emergency | security patch, production incident fix | verbal approval from on-call lead, RFC filed within 24 h |

## RFC template

```markdown
# RFC-<yyyy>-<nnn>: <title>

- **Requested by:**
- **Change class:** Standard | Normal | Emergency
- **Target release / build:** (pipeline run id)
- **Planned window:** (date, time, duration)
- **Affected components:** Web app | Database | Storage | Identity | Pipeline | Infra

## Summary
What is changing and why (link the work item / incident).

## Impact and risk
- User impact (downtime, behaviour changes, notifications sent by background services)
- Data impact (schema migration? retention sweep? storage layout?)
- Risk rating: Low | Medium | High, with justification

## Implementation steps
1. ...
2. ...

## Verification
- Automated: pipeline Build + Test stages green, `/health` smoke tests on staging and production
- Manual: pages / API calls to check, sample document to upload, approval to walk through

## Rollback plan
- Trigger criteria (what failure means we roll back)
- Steps (see "Rollback" below), owner, expected duration
- Data considerations (irreversible migrations, blobs written, e-mails already sent)

## Communication
- Who is informed before / after (leasing team, legal, owners)
- Status page / e-mail text

## Approvals
| Role | Name | Date |
|------|------|------|
| Technical reviewer | | |
| Change approver | | |
```

## Release checklist

Before deployment

- [ ] RFC approved and linked to the pull request
- [ ] `dotnet build` produces no warnings; `dotnet test` green in CI (GitHub) and Azure DevOps
- [ ] Database changes reviewed: additive where possible; destructive changes split into two releases
- [ ] `db/fulltext.sql` re-run if indexed columns changed
- [ ] New configuration keys added to Key Vault / app settings for **both** production and staging slot
- [ ] Background service cadences (`Approvals`, `Reminders`, `Retention`) reviewed so a deploy does not trigger unexpected e-mails
- [ ] Infra changes (`infra/main.bicep`) validated with `az deployment group what-if`

Deployment

- [ ] Pipeline deploys to the `staging` slot (environment `dev`)
- [ ] Staging `/health` returns 200; sign in with SSO; upload, check-out/in and search a document
- [ ] Approval gate on environment `prod` completed by the change approver
- [ ] Slot swap executed; production `/health` returns 200

After deployment

- [ ] Application Insights: no new exceptions or failed requests for 30 minutes
- [ ] Verify the escalation and reminder services logged their start-up lines
- [ ] Spot-check the audit log for the deployment window
- [ ] RFC closed with outcome; retrospective for Emergency changes

## Rollback

1. **Application** - swap the slots back (`AzureAppServiceManage@0` "Swap Slots" or
   `az webapp deployment slot swap -n app-leasevault-prod -g rg-leasevault-prod -s staging`).
   The previous build is still in the staging slot after a swap, so rollback takes about a minute.
2. **Configuration** - app settings are versioned in Bicep; redeploy the previous template
   or revert the Key Vault secret version.
3. **Database** - schema changes are additive; if a change must be reverted, run the down script
   attached to the RFC. Full-text index changes are recreated from `db/fulltext.sql`.
4. **Storage** - blob versioning and 30-day soft delete allow restoring any overwritten or deleted
   document version (`az storage blob undelete` / copy from version id).
5. **Communication** - notify stakeholders that the change was reverted and reopen the RFC.

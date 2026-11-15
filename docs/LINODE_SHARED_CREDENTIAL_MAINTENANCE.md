# Shared credential maintenance decision

Prepared 2026-11-14. No existing password or co-tenant configuration has been changed.

PokerAttack's new pilot and staged production configurations use separate restricted runtime roles. The legacy postgres password remains shared by old PokerAttack ACA, ExpenseTracker ACA and TransitJazz. Replacing the PokerAttack connection alone does not rotate that password. The earlier credential exposure remains open.

Concrete maintenance scope if coordinated rotation is selected:

1. Refresh encrypted shared database/globals/config backups and preserve protected rollback configurations. Verify local PostgreSQL peer access and owner-confirmed LISH remain available.
2. Recheck actual postgres consumers, including stopped ACA rollback instances and monitoring clients, without printing connection strings. Current known live consumers include `expense-tracker-api` in resource group `expense-tracker`, revision `expense-tracker-api--0000035`, setting `ConnectionStrings__ExpenseTrackerDB`; `poker-attack-api` in `poker-attack-rg`; and `transitjazz-app-server-1` on the VM. Resolve additional consumers before rotation.
3. Move old PokerAttack ACA rollback to its tested restricted production role during PokerAttack maintenance. Prepare new shared postgres credential in protected files only, and updates to every confirmed remaining consumer. Preserve unrelated environment settings and immutable images.
4. Coordinate the credential update with the affected application restarts. ExpenseTracker ACA environment changes create/restart its revision; TransitJazz requires an app-only restart. Preserve Caddy, all volumes and databases. Verify ExpenseTracker API, TransitJazz readiness and real hub replay, and PokerAttack's rollback connectivity before declaring success.
5. Take and transfer another encrypted globals/config backup after completion. Confirm old credential rejection without logging either credential. Preserve a protected recovery route; never restore an old leaked password merely to make an application green.

This affects services beyond PokerAttack. The alternative owner decision is to defer global shared-password rotation, continue PokerAttack using its new restricted roles, and explicitly carry the legacy incident as unresolved. Deferral must not be described as completed credential remediation. No host reboot is implied by either choice; coordinated reboot recovery remains separately unverified.

## Owner decision

2026-11-14: owner explicitly chose **defer rotation**, then directed production migration to Linode and removal of ACA production. Continue PokerAttack with its new restricted roles; leave ExpenseTracker/TransitJazz credentials and processes unchanged. Shared credential incident remains unresolved, accepted as a recorded follow-up rather than a cutover gate. This does not authorize later shared rotation without coordinating its consumers.

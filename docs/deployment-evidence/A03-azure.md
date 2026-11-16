# A03 Azure inventory

Read-only 2026-11-15T14:55–15:00Z. Inspected source `55c3a413151fc9dfae0e8a7cad4c0230cab13e46`.

Subscription `d2f70759-7eeb-4ca5-9046-f7c8d562ef8f`; tenant `9aafd1d1-65c4-4d23-a613-f66e425a581f`.

SWA resource: `/subscriptions/d2f70759-7eeb-4ca5-9046-f7c8d562ef8f/resourceGroups/poker-attack-rg/providers/Microsoft.Web/staticSites/poker-attack-webapp`. No repository/branch integration. Default hostname `salmon-hill-0056eb60f.2.azurestaticapps.net`; custom `playpokerattack.com` and `www.playpokerattack.com` both Ready. These are the three client origins.

Registry: `/subscriptions/d2f70759-7eeb-4ca5-9046-f7c8d562ef8f/resourceGroups/general/providers/Microsoft.ContainerRegistry/registries/chefknife`. ARM API 2025-11-01 explicitly returns `LegacyRegistryPermissions`; az acr show's null field was insufficient. Admin enabled, but no admin credential accessed. Repositories `chefknifestudios.pokerattack.server.webapi` and `chefknifestudios.pokerattack.server.data` both exist. Legacy role provisioning must use registry-scoped roles, without changing this shared mode.

Token `pokerattack-linode-pull` enabled. Its scope map grants only content/read and metadata/read for the two PokerAttack repositories. Separate TransitJazz pull token exists and is untouched. Live manifest read succeeds for current PokerAttack digest and is denied for TransitJazz using the host credential ([A04](A04-host.json)). This tests read scope without changing production images; no credential values exported.

Registry assignments inspected with inherited roles: caller Owner at subscription; existing AcrPush principals `85fcc726-d4cb-4f72-ad68-916745571705` (app `fc271ab1-a409-4857-a501-1d9f25c51824`), `fa0a71b1-726e-4ff4-9986-8ee379cf4220` (app `2fcf8e81-7735-484f-9a4b-d0decb16cf7c`) are DevOps identities; MenuReader also has AcrPush. Other existing principals have Contributor/AcrPull. No PokerAttack GitHub environment-bound publisher has been established. Existing GitHub secret credentials remain untested; names do not establish authentication or federation. New publisher must be separate from host pull access.

Subscription containerapp and containerapp env listings contain no PokerAttack app or dedicated environment: ACA retirement is complete and it is not a recovery target. No Azure resources/permissions/secrets were changed.

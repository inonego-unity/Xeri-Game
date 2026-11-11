# Xeri Game agent notes

- The canonical Unity package is com.inonego.xeri.game.
- Base namespace and assembly: inonego.Xeri.Game.
- The base runtime depends on Xeri Core only; UI and Fabric integration belongs in optional assemblies.
- Preserve Unity .meta GUIDs when migrating existing assets, and check serialized managed type identities separately.
- Do not change external repositories or worktrees without explicit permission.
- Initial setup excludes API documentation and documentation-site generation.
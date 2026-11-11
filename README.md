# Xeri Game

Xeri-Game is the repository for the reusable game-domain Unity package.

## Package

- UPM package: com.inonego.xeri.game
- Base dependency: com.inonego.xeri
- Default namespace: inonego.Xeri.Game
- Runtime assembly: inonego.Xeri.Game

Game-domain modules and optional Fabric/UI integrations are added separately.

## Installation

In the consumer Unity project Packages/manifest.json, add:

    "com.inonego.xeri": "https://github.com/inonego-unity/Xeri.git?path=/com.inonego.xeri#main",
    "com.inonego.xeri.game": "https://github.com/inonego-unity/Xeri-Game.git?path=/com.inonego.xeri.game#main"

For local development, use a file: dependency to this checkout instead.

## Layout

- com.inonego.xeri.game/Runtime: game runtime assemblies
- scripts/validate-package.py: UPM and Unity metadata checks
- .github/workflows/validate-package.yml: CI structure validation

No game-domain implementation or API documentation site is included in the baseline.
---
name: release
description: Bump version in package.json, commit, tag. Use when user says "release", "bump version", "ship it", or "publish".
disable-model-invocation: true
---

# Release Skill

Bump version across the FaceLint package and create a git tag.

## Steps

1. Ask user for the new version (e.g., `0.5.3`). Show current version from `package.json`.
2. Read `package.json`, update the `"version"` field.
3. Commit: `@chore: bump version to X.Y.Z`
4. Ask if user wants to create a git tag `vX.Y.Z`.
5. If yes, create annotated tag: `git tag -a "vX.Y.Z" -m "Release vX.Y.Z"`

## Files to touch
- `package.json` — `"version"` field only

## Version format
Semantic: `MAJOR.MINOR.PATCH`
- PATCH: bug fixes, minor tweaks
- MINOR: new feature
- MAJOR: breaking change (unlikely for this tool)

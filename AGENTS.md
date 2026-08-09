# TownMind / AgePilot Agent Instructions

## Communication

- Always communicate with the user in Traditional Chinese unless explicitly asked otherwise.
- Confirm material requirements before changing scope or choosing an irreversible design.

## Documentation memory

This repository uses an index-based documentation memory. Before implementing or changing behavior:

1. Read [`doc/INDEX.md`](doc/INDEX.md).
2. Follow only the links relevant to the current task.
3. Treat `doc/decisions/` as binding architecture decisions unless the user explicitly changes them.
4. Update the relevant indexed document in the same change as business behavior.
5. Add new documents to `doc/INDEX.md`; do not create orphan documentation.

Do not load every document by default. The index is the routing layer.

## Current implementation scope

- Follow `AgePilot_Project_Plan_v2.md`; the original plan is historical context only.
- The active stage is Phase 0 — Vision Spike.
- The calibration reference is AOE2 DE at 2560×1440, fullscreen, Traditional Chinese UI, HUD scale 50%.
- Use normalized ROI coordinates for proportional mapping.
- Do not claim arbitrary resolution/UI-scale support until anchor calibration and test evidence exist.
- Fail closed: unavailable, stale, contradictory, or low-confidence observations must not trigger advice.

## Engineering rules

- Target `net8.0-windows`; use the repository `global.json` SDK selection.
- Keep `AgePilot.Core` independent from UI and concrete OCR/capture implementations.
- Avoid new runtime dependencies during Vision Spike unless their value is validated and documented.
- Every ROI, parser, validation, or mapping change requires an automated regression check.
- Unknown values are represented as unavailable/null, never as zero.
- Preserve user files and unrelated working-tree changes.

## Verification

Before handing off a code change, run:

```powershell
dotnet build AgePilot.sln
dotnet run --project tests/AgePilot.Tests
```

If a check cannot run, report the exact blocker.

# CreativeTwin — Git Workflow

**Owner:** Zain (Documentation + Testing)
**Effective:** Milestone 1 onward — read this before your next commit.

Seven of us are pushing code in parallel now, so we need one shared process for branching, merging, and testing. Same rules for everyone — nobody pushes straight to `main`, no exceptions.

---

## 1. Branching Strategy

### Branch structure

| Branch | Purpose | Protected? |
|---|---|---|
| `main` | Always demo-ready. Reflects the current milestone deliverable. | Yes |
| `develop` | Integration branch. All finished feature work lands here first. | Yes |
| `feature/*` | Individual work-in-progress. One branch per task. | No |

We do **not** push directly to `main` or `develop`. Everything comes in through a feature branch and a pull request.

### Naming convention

```
feature/<area>-<short-description>
```

Use kebab-case, keep it under ~5 words. Examples:

- `feature/xr-object-selection`
- `feature/ai-material-prompt-parser`
- `feature/unity-scene-loader`
- `feature/blender-model-export`
- `feature/docs-milestone1-readme`
- `feature/api-klein4b-endpoint`
- `feature/interaction-grab-move`

If a branch fixes a bug rather than adding a feature, prefix with `fix/` instead (e.g. `fix/collision-detection-null-ref`).

### Ownership by area

Prefixes match up with what everyone's already responsible for, so it's obvious whose lane a branch is in:

| Prefix | Owner(s) | Area |
|---|---|---|
| `feature/ai-*` | Dhrumil, Dilrukshi | AI pipeline direction, prompt handling |
| `feature/api-*` | Dilrukshi | Model-serving endpoints (Klein 4B, Qwen3-4B, TRELLIS.2) on RACE — one branch per model endpoint, plus a `feature/api-chain-*` for the chained pipeline call |
| `feature/3d-*`, `feature/blender-*` | Moe | 3D models, Blender exports, environment assets |
| `feature/unity-*` | Wint | Unity runtime, scene management |
| `feature/interaction-*` | Wint, Mason (joint) | Shared interaction code — grab-and-move, placement wrapper, object/scene naming |
| `feature/xr-*` | Hyma, Wint | XR interaction, immersive review |
| `feature/ux-*` | Hyma | UX flows, interaction design (object menu, review-gate states) |
| `feature/docs-*`, `feature/test-*` | Zain | Documentation, GitHub support, testing |

Ownership isn't exclusive — if you're touching someone else's area, tag them as a reviewer on the PR (see below).

**On `feature/interaction-*` specifically:** grab-and-move is being built once and called from both the "move object" and "add new object → placement" flows. If you're on this branch and find yourself writing movement logic a second time instead of calling the shared component, stop — that's the one thing this prefix exists to prevent. Wint and Mason both review any PR that touches it, regardless of who authored it.

**On `feature/api-*` specifically:** each model (Klein 4B, Qwen3-4B, TRELLIS.2) gets its own branch and its own endpoint so it can be invoked and debugged independently — don't merge two models' serving code into one branch. The chained pipeline call is a separate branch on top, once the independent endpoints are stable.

### Keeping branches current

Rebase or merge `develop` into your feature branch regularly (at least before opening a PR) so conflicts surface early and stay small.

```bash
git checkout feature/your-branch
git fetch origin
git merge origin/develop
```

---

## 2. Merge Process

Nothing reaches `main` without going through `develop` first, and nothing reaches `develop` without a reviewed PR.

### Pull request requirements

Before opening a PR:
- [ ] Branch is up to date with `develop`
- [ ] Code builds/runs locally in Unity without errors
- [ ] No commented-out debug code or stray test assets left in

When opening the PR:
- Target `develop`, not `main`
- Fill in: what changed, why, how to test it
- Link the related task/issue if one exists

### Review requirements

| Merge into | Reviewers required | Who can approve |
|---|---|---|
| `develop` | 1 reviewer (not the author) | Any team member familiar with that area |
| `main` | 2 reviewers, including Project Lead (Dhrumil) | Dhrumil + one other |

- Reviewers check: does it work, does it follow the naming/structure conventions, does it break anything else in the scene/pipeline.
- No self-approving your own PR, even under deadline pressure — if no one's available, post in the group chat rather than merging solo.
- `main` only gets updated at milestone checkpoints, from a stable `develop`, never from a feature branch directly.

### Conflict handling

If a merge conflict comes up in shared Unity scene files (`.unity`, `.prefab`), the two people involved resolve it together live (call/screen-share) rather than each independently editing — these files don't merge cleanly as text.

### Model weights don't go in Git

GGUF weights for Klein 4B, Qwen3-4B, or TRELLIS.2 (multi-GB each) never get committed, even on a feature branch. They live on the RACE workspace where they're served from. If a `.gguf` or similar large binary shows up in a `git status`, that's a sign something's misconfigured — flag it rather than committing it. Add an explicit `.gitignore` entry for it if it keeps appearing locally.

---

## 3. Testing & Sign-off

Testing happens at three stages, each with a clear owner.

| Stage | What's tested | Who signs off |
|---|---|---|
| **Feature branch** (before PR) | The specific feature works in isolation — e.g. the prompt parser returns expected output, the object selection responds in the scene | Branch author (self-test, required before requesting review) |
| **`develop` integration** | The feature works *with* everything else already merged — no broken scene references, no pipeline breaks between Unity ↔ AI service, no regressions | Zain (Testing lead), with input from the relevant area owner if it touches their code |
| **Milestone / `main` release** | Full pipeline walkthrough per the User Journey (Load Model → Select Object → Prompt AI → Generate & Update → Review in XR); confirms the deliverable matches what's being presented | Zain + Dhrumil jointly sign off before merge to `main` |

### What "sign-off" means in practice

- Zain runs through a short checklist against the current `develop` build before anything moves to `main`. Once it's stable, this'll get its own `TESTING_CHECKLIST.md` instead of living as a bullet point here.
- Sign-off is recorded as a comment on the PR ("Tested: scene loads, AI prompt applies material, XR review functional — approved for main") so there's a record of what was verified before each milestone.
- If integration testing on `develop` surfaces a break, the responsible feature branch gets reopened rather than patched directly on `develop`.

---

## Quick reference

```
feature/xxx  →  PR + 1 review  →  develop  →  Zain integration test  →  PR + 2 reviews (incl. Dhrumil)  →  main
```

Anything not covered here — just flag it to Zain and it'll get folded into the next version.

# REA development and research notes

Recorded 12 October 2026 for the OpenClicky video research tooling plan.

## Pinned source

- Repository: [morluto/rea](https://github.com/morluto/rea)
- Local copy: [`artifacts/tools/rea`](../artifacts/tools/rea/) (ignored under `artifacts/`)
- Commit: `edf46248f2a44b4e5776518489fee09d0fc4516b`
- Commit subject: `fix(targets): project invalid raw-binary loads as caller input (#2004)`
- Commit time: `2026-10-12T09:34:45+08:00`
- Package metadata at that commit: `rea-agents` version `6.4.0`, MIT license

The checkout was obtained with `git clone --no-checkout` followed by a detached checkout of the exact commit. `git status --short --branch` reported a clean detached checkout. The source and documentation were inspected; the REA application, MCP server, target applications, and installer were not executed.

## What was inspected

`LICENSE`, `README.md`, `AGENTS.md`, `package.json`, `install.sh`, and the repository tree were read. REA describes a local CLI/MCP workflow for reverse engineering binaries, applications and runtime behavior. The repository instructions require preserving evidence provenance, treating target input as caller-selected, and making setup changes additive and approval-gated.

The README documents these relevant entry points:

- `npx rea-agents setup` registers REA with an agent and installs matching workflow instructions.
- `npx -y rea-agents@latest analyze-javascript-application ... --json` is a one-off CLI route.
- A regular CLI can be installed with `npm install --global rea-agents`.
- REA requires Node.js 22.x at least 22.19, 24.x at least 24.11, or 26+, plus npm.
- Native analysis may use existing Hopper, Ghidra or IDA installations; the README says setup can optionally install Hopper with approval.

The checked-in `install.sh` is Unix-only (`Darwin` or selected 64-bit Linux) and installs globally before optionally starting interactive setup. It is not a Windows setup route and was deliberately not run. No npm dependencies were installed, no global configuration was changed, and no credentials were read or entered.

## OpenClicky boundary

For this plan, REA is a pinned research reference and optional local source inspection aid. It is not part of the OpenClicky product runtime, provider path, or Windows desktop-action trust boundary, and this checkout does not establish an OpenWork workflow integration. If OpenWork later uses REA as an underlying research component, document that adapter and its evidence boundary separately; do not describe a source checkout as a working integration.

Any future Windows experiment must first establish the supported Node/npm toolchain and a bounded target fixture. It must keep target paths and evidence explicit, avoid opening untrusted target applications, and report provider/engine availability separately from source inspection. A REA result would be research evidence; it would not authorize OpenClicky to click, type, sign in, install software, or change Windows security settings.

## Reproducible local setup

[`scripts/setup-rea.ps1`](../scripts/setup-rea.ps1) verifies or creates the ignored pinned checkout. It uses Git only, checks out the commit in detached mode, and does not run `npm`, `npx`, `install.sh`, `rea setup`, or any target application. The script stops if an existing checkout is dirty or points at a different remote, preserving user work.

The script was syntax-checked by PowerShell parsing and exercised in verification mode against the already pinned clean checkout. It does not prove that REA runs on Windows, that native analysis engines are installed, or that an MCP client can connect.

## Evidence and limits

The video requirements matrix is [`video-acceptance-matrix.md`](video-acceptance-matrix.md). It distinguishes visible frames, spoken/captioned claims and promotional context, and uses approximate sampled times. The retained research package contains timeline sheets and metadata for all 13 unique videos. No claim is made that every encoded frame was viewed.

# OpenWork runtime packaging notes

Recorded 12 October 2026 for the local OpenClicky research/runtime fixture.

## Runtime identity

The product foundation is the MIT-licensed OpenWork server package, with the upstream OpenCode executable used as its underlying engine. OpenWork and OpenCode are separate projects and must remain separately named in product copy, receipts, and notices. This fixture contains no enterprise edition and no Den service or account integration.

The current local fixture is under [`artifacts/runtime/openwork`](../artifacts/runtime/openwork/):

| Component | Pin or evidence | License / boundary |
| --- | --- | --- |
| OpenWork server | npm package `openwork-server@0.18.57`; `pnpm-lock.yaml` integrity `sha512-1GRBvtiT0i+PcgBWIe1+7OcfVIFIV7MADaxO39H1DUZ68sjzA53oG9ceDKvPrqKI6xr+4/+vSZ0hvwfM/10vpQ==` | Package metadata declares MIT; source repository is `different-ai/openwork`, `apps/server`. This is a package pin, not proof of a matching source commit. |
| OpenWork source reference | Project source pin `ad22...` supplied for this work | Keep this source pin separate from the npm package version. The supplied source pin has not been independently proven identical to package `0.18.57`. |
| OpenCode engine | Release `v1.18.30`; Windows archive SHA-256 `c8c0e0d05ac3dac544a0edfad8de9eb244bf46c6c7a131c38619d40fcf31bd1f`; extracted `opencode.exe` SHA-256 `c1bdbb18767048e1853af4238311b3f7e16ff2f91b68fb4c7ced3c5175347eea` | Upstream `anomalyco/opencode` MIT project. This is an engine dependency, not the OpenWork product identity. |
| Node runtime | Node.js `v24.18.1` Windows x64 `node.exe`; SHA-256 `ac51903c4c111815d52280b1fdcc8da067cbb37e2fe1a765097b85c3292c8582` | Use the Node.js distribution license and notice from the official Node release source. Do not present Node as OpenWork code. |
| jsonc-parser | `3.3.1`; lock integrity `sha512-HUgH65KyejrUFPvHFPbqOY0rsFip3Bo5wb4ngvdi1EpCYWUQDC5V+Y7mZws+DLkr4M//zQJoanu1SP+87Dv1oQ==` | Dependency metadata/license must remain with the packaged dependency notice. |

The fixture currently contains OpenWork server `0.18.57`, the pinned lockfile and dependency symlinks, the OpenCode archive and extracted engine, and the verified Node binary. The tracked seeds in [`packaging/openwork`](../packaging/openwork/) are the exact manifest and lockfile used by a fresh local bootstrap.

## Reproducible setup

[`scripts/setup-openwork.ps1`](../scripts/setup-openwork.ps1) defaults to the project-local runtime directory. It verifies the supplied SHA-256 and lockfile integrity values, downloads missing Node or OpenCode artifacts to temporary files, verifies before moving them into place, and extracts the engine only when `opencode.exe` is absent.

The script does not run global installers, npm, OpenWork setup scripts, account login, or target applications. If the pinned package directories are missing during a normal setup, it runs local `pnpm --dir TARGET install --frozen-lockfile --ignore-scripts` using the tracked manifest and lockfile. Existing files with mismatched hashes fail closed; existing dependency symlinks are never removed or overwritten. The current pnpm-installed server is therefore verified through its package metadata and frozen lockfile rather than recreated from a tarball.

`-VerifyOnly` performs the full local check and creates no files or directories. A normal run may create the project-local directory, seed missing manifest/lock files, download only missing verified artifacts, and bootstrap missing packages. It does not establish that the server can start, that the engine can execute a task, or that an OpenWork workflow adapter is wired into OpenClicky.

## Licensing and notices

OpenWork server and OpenCode are MIT-licensed upstream projects. The fetched local notices are in [`packaging/openwork/licenses`](../packaging/openwork/licenses/): [`OPENWORK-LICENSE.txt`](../packaging/openwork/licenses/OPENWORK-LICENSE.txt) from source pin `ad22cdc2217370f28f24f0f8758016c2921e3c73`, [`OPENCODE-LICENSE.txt`](../packaging/openwork/licenses/OPENCODE-LICENSE.txt) from `v1.18.30`, and [`NODE-LICENSE.txt`](../packaging/openwork/licenses/NODE-LICENSE.txt) from `v24.18.1`. Retain the [OpenWork license](https://github.com/different-ai/openwork/blob/dev/LICENSE) and [OpenCode license](https://github.com/anomalyco/opencode/blob/dev/LICENSE) text and attribution in the packaged distribution. Node.js must retain its third-party notices for the exact binary distribution. `jsonc-parser` already carries its package license in the installed dependency. A successful hash check does not provide licensing proof.

The OpenWork package metadata observed locally declares `license: MIT`. The OpenCode release URL and Node download URL are official upstream locations. No enterprise edition, paid service, or private account access is part of this fixture.

## Windows and security boundary

This is a project-local x64 Windows runtime. It does not change PATH, global npm/pnpm state, Windows security settings, provider credentials, or account connections. OpenClicky now owns its lifecycle through `OpenWorkServerService`, with the application-owned ChatGPT adapter and private desktop MCP broker. A real ChatGPT → OpenWork → Windows tool → observed destination round trip passed in the native gate; see [the implementation ledger](phase-one-implementation.md). That gate is separate from final portable-package and physical acceptance.

Runtime launches use isolated state, local short-lived authentication, a shell-denied desktop profile and the task's fixed access snapshot. Confirm-each remains the default. Heuristic title checks are not a security boundary.

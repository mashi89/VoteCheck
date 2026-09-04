# sbom

`votecheck-web.cdx.json` is the software bill of materials for **VoteCheckWeb**, the
deployed product — CycloneDX 1.6, JSON, 24 components, every dependency the container
image carries and not only the top-level ones.

It exists because the Cyber Resilience Act, Regulation (EU) 2024/2847, requires one.
Annex I, Part II, point (1) obliges a manufacturer to

> identify and document vulnerabilities and components contained in products with
> digital elements, including by drawing up a software bill of materials in a commonly
> used and machine-readable format covering at the very least the top-level
> dependencies of the product

The reporting obligations apply from **11 September 2026**; the rest, this one included,
from **11 December 2027**.

## Regenerating

```
dotnet tool install --global CycloneDX --version 6.2.0
python3 sbom/generate.py
```

Output is deterministic: the same source tree produces the same bytes, `metadata.timestamp`
apart. That is what lets CI hold the file to the tree —

```
python3 sbom/generate.py --check
```

— which fails, with a diff, when a dependency has moved and the SBOM has not. Add
`--schema path/to/bom-1.6.schema.json` to validate against the CycloneDX schema as well;
CI always does.

## Two SBOMs, and why

|  | Source SBOM | Build SBOM |
|---|---|---|
| Committed | yes, `votecheck-web.cdx.json` | no, a CI artifact per run |
| Product version | `0.0.0` | `--product-version` from the release |
| Source revision | absent — git already knows | `--source-revision` |
| Base image | the tag `VoteCheckWeb/Dockerfile` declares | digest resolved from the registry |
| Shared frameworks | the `8.0` band | the patch level the image actually carries |

The split follows from what each one can honestly claim. `VoteCheckWeb/Dockerfile` pins
`mcr.microsoft.com/dotnet/aspnet:8.0`, a floating tag: the source tree does not know which
8.0.x runtime a build will get, and an SBOM in git that names one would be guessing. A
build does know, so the build SBOM resolves the digest and reads `ASPNET_VERSION` and
`DOTNET_VERSION` off the image config.

Putting the commit or a wall clock into the committed file has the same problem from the
other end: the SBOM would differ from itself on the very next commit, `--check` would have
nothing to compare, and the guarantee that the file still describes the tree would be gone.

```
python3 sbom/generate.py --resolve-base-image --source-revision "$(git rev-parse HEAD)" \
  --product-version 1.4.0 --output votecheck-web.build.cdx.json
```

## What is in it

The NuGet closure comes from `project.assets.json` by way of the CycloneDX .NET tool —
name, version, supplier, SHA-512 of the package, licence, description, dependency edges.
Three things are added on top, because a NuGet graph cannot see them:

- **The shared frameworks.** `Microsoft.AspNetCore.App` and `Microsoft.NETCore.App` arrive
  from the runtime image rather than from a package, so they appear in no restore graph at
  all. They are also the largest body of code in the product and where most of its CVEs
  will land. Omitting them would meet the letter of "top-level dependencies" and describe
  the wrong product.
- **The base image.** Same reason: the Debian userland under the runtime ships with the
  product.
- **How each package reaches the product**, as `votecheck:sbom:assetScope`:

  | Value | Meaning | Example |
  |---|---|---|
  | `runtime` | ships an assembly or a native library | `Newtonsoft.Json` |
  | `metapackage` | ships nothing itself; pulls in packages that do | `Swashbuckle.AspNetCore` |
  | `framework-provided` | resolves to `_._`, the shared framework already has the API | `System.Memory` |
  | `build-only` | MSBuild targets only, and nothing below it ships | `Microsoft.Extensions.ApiDescription.Server` |

  Restore treats all four alike. For a bill of *materials* they are not alike: only the
  first three are contained in the product. `build-only` components carry CycloneDX
  `scope: excluded` so a scanner does not report a build-host finding as a shipped one.

## Where the CRA's asks land in the file

There is no implementing act fixing the format or the field list yet, so "compliant" here
means the Regulation's own text plus BSI TR-03183-2, the most concrete published guidance
(German, not binding EU law, but it is what asks for specifics — CycloneDX 1.6 or later,
SHA-512 per deployable component, a creator and a timestamp on the document).

| Asked for | Where |
|---|---|
| Commonly used machine-readable format | CycloneDX 1.6 JSON, schema-validated in CI |
| At least top-level dependencies | the whole transitive closure, `dependencies` |
| Component name and version | `components[].name`, `.version` |
| Component supplier | `components[].supplier`, `.authors` |
| Unique identifier | `components[].purl` |
| Licence | `components[].licenses` |
| Integrity | `components[].hashes`, SHA-512 from the `.nupkg` |
| Dependency relationships | `dependencies`, rooted at the product component |
| SBOM author and timestamp | `metadata.authors`, `.manufacturer`, `.supplier`, `.timestamp` |
| Manufacturer of the product | `metadata.manufacturer` |
| Contact for reporting vulnerabilities | `security-contact` in `metadata.component.externalReferences` |
| Unique document identity | `serialNumber`, a UUIDv5 over the contents |

## What it does not cover, and what is still missing

- **Only VoteCheckWeb.** The Avalonia desktop app (`WPFGUI`) is a separate product with
  digital elements and would need its own SBOM; the test projects ship to nobody and are
  excluded.
- **No licence on the first-party components.** The repository has no `LICENSE` file, so
  `VoteCheckWeb` and `VoteCheck.Core` have nothing to declare. Adding one fills these in.
- **No hash on the frameworks or on `VoteCheck.Core`.** Neither is a package: the
  frameworks come out of the image and `VoteCheck.Core` is source compiled during the
  build. BSI TR-03183-2 wants a hash per deployable component; getting one here means
  hashing the published assemblies, which is a build-time step this does not do.
- **The base image is one component, not its contents.** The Debian packages inside
  `mcr.microsoft.com/dotnet/aspnet:8.0` are not enumerated. That needs an image scanner
  (Syft, Trivy) against the built image, and its output merged in.
- **`metadata.manufacturer` is the project, not a legal entity.** Placing the product on
  the EU market means naming the responsible person or company, with a postal address, in
  `sbom/bom-metadata.json`.
- **The CRA obligations that are not the SBOM's job** — the support period, the
  vulnerability handling process, the conformity assessment — live in the technical
  documentation, not here.

## Files

| File | What it is |
|---|---|
| `votecheck-web.cdx.json` | The SBOM. Generated; committed so the tree always carries a current one |
| `generate.py` | Generator. Wraps the CycloneDX .NET tool and adds what it cannot know |
| `bom-metadata.json` | The hand-maintained half: manufacturer, contacts, CRA properties, and licence corrections for packages too old to declare an SPDX id |

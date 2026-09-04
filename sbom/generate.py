#!/usr/bin/env python3
"""Generate the CycloneDX SBOM for the VoteCheckWeb product.

Wraps the CycloneDX .NET tool, which reads the restored NuGet graph, and adds the
things that graph cannot know about: the CRA metadata in `bom-metadata.json`, the
shared frameworks the app runs on, and the container base image it ships in.

Output is deterministic — same source tree, same bytes — so `--check` can hold the
committed SBOM to the source tree in CI. See sbom/README.md.
"""

from __future__ import annotations

import argparse
import difflib
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SBOM_DIR = REPO / "sbom"

DEFAULT_PROJECT = "VoteCheckWeb/VoteCheckWeb.csproj"
DEFAULT_OUTPUT = "sbom/votecheck-web.cdx.json"
DEFAULT_DOCKERFILE = "VoteCheckWeb/Dockerfile"

SPEC_VERSION = "1.6"
TARGET_FRAMEWORK = "net8.0"

# Pinned so the SBOM stays byte-reproducible: the tool writes its own version into
# metadata.tools, so an upgrade changes the output and must be a deliberate commit.
TOOL_VERSION = "6.2.0"
TOOL_INSTALL = f"dotnet tool install --global CycloneDX --version {TOOL_VERSION}"

# Namespace for the serial number. Content-derived, so a changed SBOM gets a new one
# and an unchanged one keeps its identity.
SERIAL_NS = uuid.uuid5(uuid.NAMESPACE_URL, "https://github.com/mashi89/VoteCheck/sbom")

# Runtime packs behind the framework references, for the purl in --resolve-base-image
# mode. Keyed by framework reference name; the RID matches the base image.
RUNTIME_PACKS = {
    "Microsoft.AspNetCore.App": "Microsoft.AspNetCore.App.Runtime.linux-x64",
    "Microsoft.NETCore.App": "Microsoft.NETCore.App.Runtime.linux-x64",
}

FRAMEWORK_DESCRIPTIONS = {
    "Microsoft.AspNetCore.App": "ASP.NET Core shared framework. Supplied by the runtime "
    "image, not by a NuGet package: Kestrel, routing, Razor Pages, the HTTP stack.",
    "Microsoft.NETCore.App": "The .NET runtime and base class library. Supplied by the "
    "runtime image.",
}

# The image config records the patch level of each shared framework it carries.
FRAMEWORK_IMAGE_ENV = {
    "Microsoft.AspNetCore.App": "ASPNET_VERSION",
    "Microsoft.NETCore.App": "DOTNET_VERSION",
}

MICROSOFT = {"name": "Microsoft", "url": ["https://dotnet.microsoft.com"]}


# ------------------------------------------------------------------ identity


def default_version() -> str:
    """The version stamped on the product component.

    Deliberately not derived from git: the committed SBOM has to be a pure function
    of the source tree, or it would differ from itself on the very next commit and
    `--check` would have nothing to compare. A release build passes the real version
    with --product-version. The repository carries no release tags, so 0.0.0 is the
    honest default.
    """
    return "0.0.0"


def timestamp() -> str:
    """When this SBOM was created. SOURCE_DATE_EPOCH wins, for reproducible builds."""
    epoch = os.environ.get("SOURCE_DATE_EPOCH")
    stamp = (
        datetime.fromtimestamp(int(epoch), timezone.utc)
        if epoch
        else datetime.now(timezone.utc)
    )
    return stamp.strftime("%Y-%m-%dT%H:%M:%SZ")


# ------------------------------------------------------------------ cyclonedx run


def find_tool() -> str:
    tool = shutil.which("dotnet-CycloneDX") or shutil.which(
        "dotnet-CycloneDX", path=str(Path.home() / ".dotnet" / "tools")
    )
    if not tool:
        sys.exit(f"dotnet-CycloneDX not found on PATH. Install it with:\n  {TOOL_INSTALL}")
    version = subprocess.run(
        [tool, "--version"], check=True, capture_output=True, text=True
    ).stdout.strip().split("+")[0]
    if version != TOOL_VERSION:
        sys.exit(
            f"dotnet-CycloneDX {version} found, but this SBOM is pinned to "
            f"{TOOL_VERSION}.\nThe tool version is recorded in the SBOM, so mixing "
            f"versions produces a spurious diff.\nEither install the pin:\n"
            f"  {TOOL_INSTALL}\nor raise TOOL_VERSION in sbom/generate.py and "
            f"regenerate deliberately."
        )
    return tool


def run_cyclonedx(project: Path, version: str) -> dict:
    tool = find_tool()
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(
            [
                tool, str(project),
                "--recursive",                 # follow ProjectReference
                "--include-project-references",  # ...and list them as components
                "--framework", TARGET_FRAMEWORK,
                "--exclude-dev",
                "--exclude-test-projects",
                "--spec-version", SPEC_VERSION,
                "--output-format", "Json",
                "--set-version", version,
                "--output", tmp,
                "--filename", "bom.json",
            ],
            check=True, cwd=REPO, stdout=subprocess.DEVNULL,
        )
        return json.loads((Path(tmp) / "bom.json").read_text(encoding="utf-8"))


# ----------------------------------------------------------------- project assets


def project_chain(project: Path) -> list[Path]:
    """The project and every project it references, transitively."""
    seen: list[Path] = []
    queue = [project.resolve()]
    while queue:
        current = queue.pop(0)
        if current in seen:
            continue
        seen.append(current)
        for ref in ET.parse(current).getroot().iter("ProjectReference"):
            include = ref.get("Include")
            if include:
                queue.append((current.parent / include.replace("\\", "/")).resolve())
    return seen


def asset_scopes(project: Path) -> dict[str, str]:
    """How each package actually reaches the product, per project.assets.json.

    NuGet's dependency graph does not distinguish "ships in the image" from "ran on
    the build host" or "resolved to a placeholder because the shared framework
    already carries the API". For a CRA SBOM that is the difference between a
    component contained in the product and one that never leaves the build:

      runtime             ships an assembly or native library
      metapackage         ships nothing itself; exists to pull in packages that do
      framework-provided  assets are `_._` placeholders, the shared framework has it
      build-only          contributes MSBuild targets only, and nothing below it ships
    """
    assets: dict[str, dict] = {}
    for proj in project_chain(project):
        path = proj.parent / "obj" / "project.assets.json"
        if not path.exists():
            sys.exit(f"{path} missing — run `dotnet restore {proj.name}` first.")
        for target in json.loads(path.read_text(encoding="utf-8")).get("targets", {}).values():
            for key, value in target.items():
                if value.get("type") == "package":
                    assets.setdefault(key, value)

    def real_assets(entry: dict) -> bool:
        paths = list(entry.get("runtime", {})) + list(entry.get("runtimeTargets", {}))
        return any(not path.endswith("_._") for path in paths)

    ships = {key: real_assets(value) for key, value in assets.items()}

    def dependency_keys(entry: dict) -> list[str]:
        # assets.json records dependencies as name -> resolved version.
        return [f"{name}/{version}" for name, version in entry.get("dependencies", {}).items()]

    # A package that ships nothing itself still puts code in the product if anything
    # it depends on does. Propagate until nothing changes.
    changed = True
    while changed:
        changed = False
        for key, entry in assets.items():
            if ships[key]:
                continue
            if any(ships.get(dep) for dep in dependency_keys(entry)):
                ships[key] = True
                changed = True

    scopes: dict[str, str] = {}
    for key, entry in assets.items():
        name, _, version = key.partition("/")
        placeholders = entry.get("runtime") or entry.get("compile")
        if real_assets(entry):
            scope = "runtime"
        elif ships[key]:
            scope = "metapackage"
        elif placeholders:
            scope = "framework-provided"
        else:
            scope = "build-only"
        scopes[f"{name}@{version}"] = scope
    return scopes


def framework_references(project: Path) -> list[str]:
    """The shared frameworks the project targets, in dependency order."""
    assets = project.parent / "obj" / "project.assets.json"
    frameworks = json.loads(assets.read_text(encoding="utf-8"))["project"]["frameworks"]
    names: list[str] = []
    for spec in frameworks.values():
        names.extend(spec.get("frameworkReferences", {}))
    # Microsoft.NETCore.App underpins Microsoft.AspNetCore.App; order the edge that way.
    order = ["Microsoft.AspNetCore.App", "Microsoft.NETCore.App"]
    return sorted(dict.fromkeys(names), key=lambda n: (order.index(n) if n in order else 99, n))


# ------------------------------------------------------------------ registry data


def registry_get(registry: str, repo: str, kind: str, reference: str,
                 accept: str) -> tuple[bytes, dict]:
    request = urllib.request.Request(
        f"https://{registry}/v2/{repo}/{kind}/{reference}",
        headers={"Accept": accept},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read(), dict(response.headers)


def resolve_image(registry: str, repo: str, tag: str,
                  architecture: str = "amd64") -> dict:
    """Digest and shared-framework versions of a base image tag, from the registry.

    Only used for a build SBOM (--resolve-base-image). The committed SBOM records the
    tag the Dockerfile declares, because that is what the source tree actually pins.
    """
    index_accept = (
        "application/vnd.oci.image.index.v1+json,"
        "application/vnd.docker.distribution.manifest.list.v2+json,"
        "application/vnd.oci.image.manifest.v1+json,"
        "application/vnd.docker.distribution.manifest.v2+json"
    )
    body, headers = registry_get(registry, repo, "manifests", tag, index_accept)
    index_digest = headers.get("Docker-Content-Digest", "")
    manifest = json.loads(body)

    if "manifests" in manifest:
        match = next(
            m for m in manifest["manifests"]
            if m.get("platform", {}).get("architecture") == architecture
            and m.get("platform", {}).get("os") == "linux"
        )
        body, _ = registry_get(registry, repo, "manifests", match["digest"], index_accept)
        manifest = json.loads(body)

    config_accept = (
        "application/vnd.oci.image.config.v1+json,"
        "application/vnd.docker.container.image.v1+json"
    )
    config_body, _ = registry_get(
        registry, repo, "blobs", manifest["config"]["digest"], config_accept
    )
    env = dict(
        item.split("=", 1)
        for item in json.loads(config_body)["config"].get("Env", [])
        if "=" in item
    )
    return {"digest": index_digest, "env": env}


# -------------------------------------------------------------------- components


def base_image(dockerfile: Path) -> tuple[str, str]:
    """(registry/repository, tag) of the Dockerfile's runtime stage.

    The runtime stage, not the build stage: the SDK image compiles the app and is
    then thrown away, so nothing in it is contained in the product.
    """
    stages = re.findall(
        r"^FROM\s+(\S+)(?:\s+AS\s+(\S+))?\s*$",
        dockerfile.read_text(encoding="utf-8"),
        re.MULTILINE | re.IGNORECASE,
    )
    if not stages:
        sys.exit(f"No FROM instruction found in {dockerfile}")
    reference, _ = next(
        (stage for stage in stages if (stage[1] or "").lower() == "runtime"), stages[-1]
    )
    # A colon is only a tag separator after the last slash; before it, it is a
    # registry port.
    head, _, last = reference.rpartition("/")
    name, _, tag = last.partition(":")
    return f"{head}/{name}" if head else name, tag or "latest"


def base_image_component(dockerfile: Path, resolved: dict | None) -> dict:
    image, tag = base_image(dockerfile)
    registry, _, repository = image.partition("/")
    component: dict = {
        "type": "container",
        "bom-ref": f"container:{image}:{tag}",
        "name": image,
        "version": tag,
        "supplier": MICROSOFT,
        "description": (
            "Runtime base image of the deployed container. Carries the .NET and "
            "ASP.NET Core shared frameworks and its own Debian userland."
        ),
        "scope": "required",
        "externalReferences": [
            {"type": "distribution", "url": f"https://{registry}/v2/{repository}/tags/list"},
            {"type": "website", "url": "https://github.com/dotnet/dotnet-docker"},
        ],
        "properties": [
            {"name": "votecheck:sbom:declaredIn", "value": str(dockerfile.relative_to(REPO))},
        ],
    }
    if resolved and resolved.get("digest"):
        digest = resolved["digest"]
        component["bom-ref"] = f"pkg:docker/{repository}@{digest}"
        component["purl"] = f"pkg:docker/{repository}@{digest}?repository_url={registry}"
        component["hashes"] = [{"alg": "SHA-256", "content": digest.split(":", 1)[1]}]
        component["properties"].append(
            {"name": "votecheck:sbom:resolvedDigest", "value": digest}
        )
    else:
        component["properties"].append(
            {
                "name": "votecheck:sbom:versionKind",
                "value": "declared-tag; the digest is fixed at image build time, "
                "not by the source tree",
            }
        )
    return component


def framework_component(name: str, resolved: dict | None) -> dict:
    version = TARGET_FRAMEWORK.removeprefix("net")
    component: dict = {
        "type": "framework",
        "bom-ref": f"framework:{name}@{version}",
        "name": name,
        "version": version,
        "supplier": MICROSOFT,
        "description": FRAMEWORK_DESCRIPTIONS.get(name, ""),
        "scope": "required",
        "licenses": [{"license": {"id": "MIT"}}],
        "externalReferences": [
            {"type": "website", "url": "https://dotnet.microsoft.com"},
            {"type": "vcs", "url": "https://github.com/dotnet/aspnetcore"
             if name == "Microsoft.AspNetCore.App" else "https://github.com/dotnet/runtime"},
            {"type": "advisories",
             "url": "https://github.com/dotnet/announcements/issues"},
        ],
        "properties": [
            {"name": "votecheck:sbom:assetScope", "value": "framework-provided"},
            {"name": "votecheck:sbom:targetFramework", "value": TARGET_FRAMEWORK},
        ],
    }
    env_key = FRAMEWORK_IMAGE_ENV.get(name)
    patch = (resolved or {}).get("env", {}).get(env_key) if env_key else None
    if patch:
        pack = RUNTIME_PACKS.get(name)
        component["version"] = patch
        component["bom-ref"] = f"framework:{name}@{patch}"
        if pack:
            component["purl"] = f"pkg:nuget/{pack}@{patch}"
        component["properties"].append(
            {"name": "votecheck:sbom:versionSource",
             "value": f"{env_key} of the resolved runtime image"}
        )
    else:
        component["properties"].append(
            {"name": "votecheck:sbom:versionKind",
             "value": "target framework band; the patch level is whatever the runtime "
                      "image carries at build time"}
        )
    return component


# ------------------------------------------------------------------- assembly


def template() -> dict:
    """The hand-maintained half of the SBOM: everything not derivable from the tree."""
    return json.loads((SBOM_DIR / "bom-metadata.json").read_text(encoding="utf-8"))


def license_overrides() -> dict:
    overrides = dict(template().get("licenseOverrides", {}))
    overrides.pop("$comment", None)
    return overrides


def apply_metadata(bom: dict, version: str, revision: str | None) -> None:
    """Merge sbom/bom-metadata.json over what the tool produced.

    The tool knows the assembly name and the package graph; everything the CRA asks
    for about the manufacturer and the product is hand-maintained in that file.
    """
    meta = template()["metadata"]
    generated = bom["metadata"]

    component = dict(meta["component"])
    component["bom-ref"] = generated["component"]["bom-ref"]
    component["name"] = generated["component"]["name"]
    component["version"] = version

    if revision:
        component.setdefault("externalReferences", []).append(
            {
                "type": "build-meta",
                "url": f"https://github.com/mashi89/VoteCheck/tree/{revision}",
                "comment": "Source revision this SBOM describes",
            }
        )
        component.setdefault("properties", []).append(
            {"name": "votecheck:sbom:sourceRevision", "value": revision}
        )

    bom["metadata"] = {
        "timestamp": timestamp(),
        "lifecycles": meta["lifecycles"],
        "tools": generated["tools"],
        "manufacturer": meta["manufacturer"],
        "authors": meta["authors"],
        "component": component,
        "supplier": meta["supplier"],
        "properties": meta["properties"],
    }


def annotate_packages(bom: dict, scopes: dict[str, str], overrides: dict) -> None:
    """Record how each NuGet package reaches the product, and settle its licence."""
    for component in bom["components"]:
        purl = component.get("purl", "")
        if not purl.startswith("pkg:nuget/"):
            continue

        scope = scopes.get(f"{component['name']}@{component['version']}")
        if scope:
            component.setdefault("properties", []).append(
                {"name": "votecheck:sbom:assetScope", "value": scope}
            )
            # CycloneDX "excluded": in the build, not required at runtime and not
            # carried in the shipped image.
            component["scope"] = "excluded" if scope == "build-only" else "required"

        override = overrides.get(purl)
        if override:
            component["licenses"] = [{"license": {k: v for k, v in override.items()}}]


def annotate_first_party(bom: dict, product_ref: str) -> None:
    """The project references are first-party code, not third-party packages."""
    supplier = template()["metadata"]["supplier"]
    for component in bom["components"]:
        if component.get("purl") or component["bom-ref"] == product_ref:
            continue
        if component["type"] != "library":
            continue
        component["supplier"] = supplier
        component["externalReferences"] = [
            {"type": "vcs", "url": "https://github.com/mashi89/VoteCheck"}
        ]
        component.setdefault("properties", []).append(
            {"name": "votecheck:sbom:origin", "value": "first-party"}
        )


def add_platform(bom: dict, project: Path, dockerfile: Path, resolved: dict | None) -> None:
    product_ref = bom["metadata"]["component"]["bom-ref"]
    frameworks = [framework_component(name, resolved) for name in framework_references(project)]
    container = base_image_component(dockerfile, resolved)
    bom["components"].extend(frameworks + [container])

    framework_refs = [f["bom-ref"] for f in frameworks]
    root = next((e for e in bom["dependencies"] if e["ref"] == product_ref), None)
    if root is None:
        root = {"ref": product_ref, "dependsOn": []}
        bom["dependencies"].append(root)
    root.setdefault("dependsOn", []).extend(framework_refs + [container["bom-ref"]])

    # The image is what supplies the shared frameworks.
    bom["dependencies"].append({"ref": container["bom-ref"], "dependsOn": list(framework_refs)})
    # ASP.NET Core is layered on the .NET runtime.
    aspnet = next((f for f in frameworks if f["name"] == "Microsoft.AspNetCore.App"), None)
    netcore = next((f for f in frameworks if f["name"] == "Microsoft.NETCore.App"), None)
    for framework in frameworks:
        depends = []
        if aspnet and netcore and framework is aspnet:
            depends = [netcore["bom-ref"]]
        bom["dependencies"].append({"ref": framework["bom-ref"], "dependsOn": depends})


def canonicalise(bom: dict) -> dict:
    bom["components"].sort(key=lambda c: (c["type"], c["name"].lower(), c.get("version", "")))
    for entry in bom["dependencies"]:
        if "dependsOn" in entry:
            entry["dependsOn"] = sorted(dict.fromkeys(entry["dependsOn"]))
    bom["dependencies"].sort(key=lambda d: d["ref"])
    return bom


def set_serial_number(bom: dict) -> None:
    """A content-derived serial: same contents, same identity; changed, new identity.

    Computed over everything but the timestamp, so regenerating an unchanged tree
    reproduces the serial and only the timestamp moves.
    """
    bom.pop("serialNumber", None)
    subject = json.loads(json.dumps(bom))
    subject["metadata"].pop("timestamp", None)
    payload = json.dumps(subject, sort_keys=True, separators=(",", ":")).encode("utf-8")
    digest = hashlib.sha256(payload).hexdigest()
    bom["serialNumber"] = f"urn:uuid:{uuid.uuid5(SERIAL_NS, digest)}"


def ordered(bom: dict) -> dict:
    order = [
        "$schema", "bomFormat", "specVersion", "serialNumber", "version",
        "metadata", "components", "dependencies",
    ]
    return {key: bom[key] for key in order if key in bom} | {
        key: value for key, value in bom.items() if key not in order
    }


def build(project: Path, dockerfile: Path, resolve: bool, version: str,
          revision: str | None) -> dict:
    bom = run_cyclonedx(project, version)
    bom["$schema"] = (
        "http://cyclonedx.org/schema/bom-" + SPEC_VERSION + ".schema.json"
    )
    apply_metadata(bom, version, revision)
    annotate_packages(bom, asset_scopes(project), license_overrides())
    annotate_first_party(bom, bom["metadata"]["component"]["bom-ref"])

    resolved = None
    if resolve:
        image, tag = base_image(dockerfile)
        registry, _, repository = image.partition("/")
        resolved = resolve_image(registry, repository, tag)
    add_platform(bom, project, dockerfile, resolved)

    canonicalise(bom)
    bom = ordered(bom)
    set_serial_number(bom)
    return ordered(bom)


def validate(bom: dict, schema_path: Path | None) -> None:
    """Validate against the CycloneDX JSON schema, when one is available.

    Optional on purpose: generating the SBOM must not require a network fetch or a
    pip install. CI passes --schema so the check is not optional there.
    """
    if schema_path is None:
        return
    if not schema_path.exists():
        sys.exit(f"--schema {schema_path} does not exist")
    try:
        import jsonschema
        from referencing import Registry, Resource
        from referencing.jsonschema import DRAFT7
    except ImportError:
        sys.exit("--schema needs the jsonschema package: pip install jsonschema")

    def load(path: Path) -> dict:
        return json.loads(path.read_text(encoding="utf-8"))

    # bom-1.6.schema.json $refs these two by bare filename; they ship beside it.
    registry = Registry().with_resources(
        (name, Resource.from_contents(load(schema_path.parent / name),
                                      default_specification=DRAFT7))
        for name in ("spdx.schema.json", "jsf-0.82.schema.json")
        if (schema_path.parent / name).exists()
    )
    validator = jsonschema.Draft7Validator(load(schema_path), registry=registry)
    errors = sorted(validator.iter_errors(bom), key=lambda e: list(e.path))
    for error in errors[:20]:
        print(f"schema: /{'/'.join(str(p) for p in error.path)}: {error.message}",
              file=sys.stderr)
    if errors:
        sys.exit(f"SBOM failed CycloneDX {SPEC_VERSION} schema validation "
                 f"({len(errors)} errors)")


def without_timestamp(text: str) -> str:
    """The SBOM minus the one field that legitimately moves between runs."""
    bom = json.loads(text)
    bom["metadata"].pop("timestamp", None)
    return json.dumps(bom, indent=2, ensure_ascii=False) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--project", default=DEFAULT_PROJECT,
                        help=f"project to describe (default: {DEFAULT_PROJECT})")
    parser.add_argument("--dockerfile", default=DEFAULT_DOCKERFILE,
                        help="Dockerfile the runtime base image is read from "
                             f"(default: {DEFAULT_DOCKERFILE})")
    parser.add_argument("--output", default=DEFAULT_OUTPUT,
                        help=f"where to write (default: {DEFAULT_OUTPUT})")
    parser.add_argument("--product-version", default=None,
                        help="version for the product component (default: 0.0.0)")
    parser.add_argument("--source-revision", default=None,
                        help="record this commit as the revision the SBOM describes; "
                             "for a build SBOM, not for the committed one")
    parser.add_argument("--resolve-base-image", action="store_true",
                        help="query the registry and pin the base image digest and the "
                             "shared framework patch versions; for a build SBOM, not "
                             "for the committed one")
    parser.add_argument("--check", action="store_true",
                        help="regenerate and fail if the file on disk differs "
                             "(the timestamp is ignored)")
    parser.add_argument("--schema", default=None, metavar="PATH",
                        help="CycloneDX JSON schema to validate the result against")
    args = parser.parse_args()

    version = args.product_version or default_version()
    bom = build(
        REPO / args.project,
        REPO / args.dockerfile,
        args.resolve_base_image,
        version,
        args.source_revision,
    )
    validate(bom, Path(args.schema) if args.schema else None)

    text = json.dumps(bom, indent=2, ensure_ascii=False) + "\n"
    output = REPO / args.output

    if args.check:
        if not output.exists():
            return fail(f"{args.output} does not exist — run `python3 sbom/generate.py`")
        committed = output.read_text(encoding="utf-8")
        if without_timestamp(committed) == without_timestamp(text):
            print(f"{args.output} is up to date: {len(bom['components'])} components")
            return 0
        sys.stdout.writelines(
            difflib.unified_diff(
                without_timestamp(committed).splitlines(keepends=True),
                without_timestamp(text).splitlines(keepends=True),
                fromfile=f"{args.output} (committed)",
                tofile=f"{args.output} (regenerated)",
            )
        )
        return fail(
            f"\n{args.output} no longer describes the source tree. "
            f"Regenerate it with `python3 sbom/generate.py` and commit the result."
        )

    output.write_text(text, encoding="utf-8")
    print(f"wrote {args.output}: {len(bom['components'])} components, "
          f"product {version}, {bom['serialNumber']}")
    return 0


def fail(message: str) -> int:
    print(message, file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())

"""Build the allowlisted VPM package without any Unity project or paid dependencies."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

PACKAGE = Path(__file__).resolve().parents[1]
PACKAGE_ID = "dev.mii.vrclens-ma-installer"
REPOSITORY = "mii443/vrclens-ma-installer"
FILES = [
    "package.json", "README.md", "LICENSE.md", "CHANGELOG.md",
    "Runtime/VRCLensInstaller.cs", "Runtime/VRCLensMAInstaller.Runtime.asmdef",
    "Editor/InstalledVRCLens.cs", "Editor/AssetGraph.cs", "Editor/InstallerBuild.cs",
    "Editor/InstallerEditor.cs", "Editor/EarlyFavoritesCompatibility.cs", "Editor/VRCLensMAInstaller.Editor.asmdef",
    "Editor/CustomizationMenuRouting.cs", "Editor/LyraIntegration.cs",
    "Prefabs/VRCLens MA Installer.prefab",
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=PACKAGE / "dist")
    parser.add_argument("--tag", default="", help="If supplied, must equal v<package version>.")
    args = parser.parse_args()
    paths = FILES + [name + ".meta" for name in FILES] + ["Runtime.meta", "Editor.meta", "Prefabs.meta"]
    for name in paths:
        source = PACKAGE / name
        if not source.is_file() or source.is_symlink() or PACKAGE not in source.resolve().parents:
            raise RuntimeError(f"Missing or unsafe release file: {source}")
    own_guids = set()
    for name in paths:
        if name.endswith(".meta"):
            own_guids.update(re.findall(r"guid: ([0-9a-f]{32})", (PACKAGE / name).read_text(encoding="utf-8")))
    prefab = (PACKAGE / "Prefabs/VRCLens MA Installer.prefab").read_text(encoding="utf-8")
    foreign = set(re.findall(r"guid: ([0-9a-f]{32})", prefab)) - own_guids - {"0" * 32}
    if foreign or "setupObject: {fileID: 0}" not in prefab:
        raise RuntimeError("The release prefab must be empty and must not reference vendor assets.")
    manifest = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))
    name, version = manifest["name"], manifest["version"]
    if name != PACKAGE_ID or not re.fullmatch(r"[0-9A-Za-z.+-]+", version):
        raise RuntimeError("Invalid package identity or version.")
    tag = "v" + version
    filename = f"{name}-{version}.zip"
    expected_url = f"https://github.com/{REPOSITORY}/releases/download/{tag}/{filename}"
    if manifest.get("url") != expected_url or (args.tag and args.tag != tag):
        raise RuntimeError("The package version, release URL, and Git tag must agree.")
    if not manifest.get("author", {}).get("name") or not manifest["author"].get("email"):
        raise RuntimeError("Public author name and contact email are required.")
    args.output.mkdir(parents=True, exist_ok=True)
    archive = args.output / filename
    # All allowlisted files are text. Stable line endings, timestamps, and permissions
    # produce the same ZIP locally and in a clean GitHub checkout.
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for name in sorted(paths):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            bundle.writestr(info, (PACKAGE / name).read_bytes().replace(b"\r\n", b"\n"))
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {filename}\n", encoding="ascii", newline="\n")
    archive.with_suffix(".files.txt").write_text("\n".join(sorted(paths)) + "\n", encoding="utf-8", newline="\n")
    # The separate manifest also supports GitHub-based VPM listing generators.
    (args.output / "package.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Created {archive} ({archive.stat().st_size} bytes, {len(paths)} files)")
    print(f"SHA256 {digest}")


if __name__ == "__main__":
    main()

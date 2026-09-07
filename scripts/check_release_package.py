"""Check release packaging at an explicit source or artifact boundary.

The source mode checks that the packaging script is wired to collect the
player-facing files. The artifact mode checks a real release directory or zip
file, including the files that were actually copied and every local Markdown
link in the packaged documentation. The two modes are deliberately separate
so a source-tree check cannot be reported as a package check.
"""

from __future__ import annotations

import argparse
import json
import posixpath
import re
import sys
import zipfile
from pathlib import Path
from typing import Iterable, Protocol


SOURCE_FILES = (
    "README.md",
    "README.zh-CN.md",
    "LICENSE",
    "CHANGELOG.md",
    "mcp_server/README.md",
    "mcp_server/pyproject.toml",
    "mcp_server/uv.lock",
    "scripts/build-mod.ps1",
    "scripts/package-release.ps1",
    "scripts/start-mcp-stdio.ps1",
    "scripts/start-mcp-network.ps1",
    "scripts/test-mcp-tool-profile.ps1",
)

ARTIFACT_FILES = (
    "README.md",
    "README.zh-CN.md",
    "LICENSE",
    "CHANGELOG.md",
    "mod/STS2AIAgent.dll",
    "mod/STS2AIAgent.pck",
    "mod/mod_id.json",
    "mcp_server/README.md",
    "mcp_server/pyproject.toml",
    "mcp_server/uv.lock",
    "mcp_server/src/sts2_mcp/__init__.py",
    "mcp_server/src/sts2_mcp/client.py",
    "mcp_server/src/sts2_mcp/server.py",
    "mcp_server/src/sts2_mcp/network_server.py",
    "scripts/start-mcp-stdio.ps1",
    "scripts/start-mcp-network.ps1",
    "scripts/test-mcp-tool-profile.ps1",
)

ARTIFACT_DIRECTORIES = ("mcp_server/data/eng",)
PACKAGED_DOCUMENTS = ("README.md", "README.zh-CN.md", "mcp_server/README.md")

_MARKDOWN_LINK = re.compile(r"(?<!!)\[[^\]]+\]\(([^)\s]+)(?:\s+[^)]*)?\)")
_EXTERNAL_TARGET = re.compile(r"^(?:https?://|mailto:|data:|//)", re.IGNORECASE)


class PackageCheckError(RuntimeError):
    """Raised when a source contract or release artifact is incomplete."""


class ArtifactReader(Protocol):
    def has_file(self, relative_path: str) -> bool:
        ...

    def has_directory(self, relative_path: str) -> bool:
        ...

    def read_text(self, relative_path: str) -> str:
        ...


def _normalise_relative_path(path: str) -> str:
    normalised = path.replace("\\", "/")
    while normalised.startswith("./"):
        normalised = normalised[2:]
    return normalised


class _DirectoryReader:
    def __init__(self, root: Path) -> None:
        self.root = root

    def _path(self, relative_path: str) -> Path:
        return self.root.joinpath(*relative_path.split("/"))

    def has_file(self, relative_path: str) -> bool:
        return self._path(relative_path).is_file()

    def has_directory(self, relative_path: str) -> bool:
        return self._path(relative_path).is_dir()

    def read_text(self, relative_path: str) -> str:
        try:
            return self._path(relative_path).read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise PackageCheckError(
                f"artifact file '{relative_path}' cannot be read: {exc}"
            ) from exc


class _ZipReader:
    def __init__(self, archive: zipfile.ZipFile) -> None:
        self.archive = archive
        self._raw_names = {
            _normalise_relative_path(name): name
            for name in archive.namelist()
            if not name.endswith(("/", "\\"))
        }
        self.names = set(self._raw_names)

    def has_file(self, relative_path: str) -> bool:
        return _normalise_relative_path(relative_path) in self.names

    def has_directory(self, relative_path: str) -> bool:
        prefix = _normalise_relative_path(relative_path).rstrip("/") + "/"
        return any(name.startswith(prefix) for name in self.names)

    def read_text(self, relative_path: str) -> str:
        name = _normalise_relative_path(relative_path)
        if name not in self.names:
            raise PackageCheckError(f"artifact file '{relative_path}' is missing")
        try:
            with self.archive.open(self._raw_names[name]) as stream:
                return stream.read().decode("utf-8")
        except (OSError, UnicodeDecodeError, KeyError) as exc:
            raise PackageCheckError(
                f"artifact file '{relative_path}' cannot be read: {exc}"
            ) from exc


def _iter_markdown_targets(text: str) -> Iterable[str]:
    for match in _MARKDOWN_LINK.finditer(text):
        target = match.group(1).strip()
        if target.startswith("<") and target.endswith(">"):
            target = target[1:-1]
        yield target


def _local_link_path(document_path: str, target: str) -> str | None:
    target = target.split("#", 1)[0].strip()
    if not target or target.startswith("#") or _EXTERNAL_TARGET.match(target):
        return None
    if target.startswith("/"):
        raise PackageCheckError(
            f"{document_path} uses an absolute local Markdown link: {target}"
        )

    document_directory = posixpath.dirname(document_path)
    resolved = posixpath.normpath(posixpath.join(document_directory, target))
    if resolved == ".." or resolved.startswith("../"):
        raise PackageCheckError(
            f"{document_path} links outside the release artifact: {target}"
        )
    return resolved


def _check_document_links(reader: ArtifactReader, document_path: str) -> None:
    text = reader.read_text(document_path)
    for target in _iter_markdown_targets(text):
        resolved = _local_link_path(document_path, target)
        if resolved is None:
            continue
        if not reader.has_file(resolved):
            raise PackageCheckError(
                f"{document_path} links to missing packaged file '{resolved}'"
            )


def _check_player_document(document_path: str, text: str) -> None:
    if "mod/" not in text and "`mod`" not in text:
        raise PackageCheckError(
            f"{document_path} must tell players to copy files from the zip mod folder"
        )
    if "mods/" not in text:
        raise PackageCheckError(
            f"{document_path} must show the game mods/ destination"
        )
    if "Play with Mods" not in text and "带 Mod 启动" not in text:
        raise PackageCheckError(
            f"{document_path} must mention the Steam Play with Mods entry"
        )
    if "测试连接" not in text and "Test Connection" not in text:
        raise PackageCheckError(
            f"{document_path} must mention testing the play model before inviting"
        )


def _check_source_root(source_root: Path) -> None:
    if not source_root.is_dir():
        raise PackageCheckError(f"source root does not exist: {source_root}")

    for relative_path in SOURCE_FILES:
        path = source_root.joinpath(*relative_path.split("/"))
        if not path.is_file():
            raise PackageCheckError(f"required source file is missing: {relative_path}")

    package_script = (source_root / "scripts" / "package-release.ps1").read_text(
        encoding="utf-8"
    )
    required_snippets = (
        '"-SkipInstall"',
        "Invoke-ArtifactCheck",
        "--artifact",
        "Rewrite-PackagedReadmeLinks",
        'Join-Path $ProjectRoot "README.zh-CN.md"',
        'Join-Path $ProjectRoot "LICENSE"',
        'Join-Path $modOutputDir "STS2AIAgent.dll"',
        'Join-Path $modOutputDir "STS2AIAgent.pck"',
        'Join-Path $modOutputDir "mod_id.json"',
        "scripts/test-mcp-tool-profile.ps1",
    )
    for required in required_snippets:
        if required not in package_script:
            raise PackageCheckError(
                f"package-release.ps1 is missing source packaging contract: {required}"
            )

    for forbidden in (
        'Join-Path $ProjectRoot "docs/release-readiness.md"',
        'Join-Path $ProjectRoot "docs/game-knowledge"',
    ):
        if forbidden in package_script:
            raise PackageCheckError(
                f"player release package must not copy internal path '{forbidden}'"
            )

    source_reader = _DirectoryReader(source_root)
    for document_path in PACKAGED_DOCUMENTS:
        text = source_reader.read_text(document_path)
        if document_path in {"README.md", "README.zh-CN.md"}:
            _check_player_document(document_path, text)
        _check_document_links(source_reader, document_path)


def _check_artifact_reader(reader: ArtifactReader, label: str) -> None:
    for relative_path in ARTIFACT_FILES:
        if not reader.has_file(relative_path):
            raise PackageCheckError(
                f"{label} is missing required file '{relative_path}'"
            )

    for relative_path in ARTIFACT_DIRECTORIES:
        if not reader.has_directory(relative_path):
            raise PackageCheckError(
                f"{label} is missing required directory '{relative_path}'"
            )

    for forbidden in ("docs/release-readiness.md", "docs/game-knowledge/"):
        if isinstance(reader, _ZipReader):
            if any(
                name == forbidden.rstrip("/") or name.startswith(forbidden)
                for name in reader.names
            ):
                raise PackageCheckError(
                    f"{label} contains internal documentation '{forbidden}'"
                )
        elif reader.has_file(forbidden.rstrip("/")) or reader.has_directory(
            forbidden.rstrip("/")
        ):
            raise PackageCheckError(
                f"{label} contains internal documentation '{forbidden}'"
            )

    try:
        manifest = json.loads(reader.read_text("mod/mod_id.json"))
    except json.JSONDecodeError as exc:
        raise PackageCheckError(f"{label} has invalid mod/mod_id.json: {exc}") from exc
    if not manifest.get("id") or not manifest.get("version"):
        raise PackageCheckError(
            f"{label} mod/mod_id.json must contain non-empty id and version"
        )

    for document_path in PACKAGED_DOCUMENTS:
        text = reader.read_text(document_path)
        if document_path in {"README.md", "README.zh-CN.md"}:
            _check_player_document(document_path, text)
        _check_document_links(reader, document_path)


def _check_artifact(artifact: Path) -> None:
    if artifact.is_dir():
        _check_artifact_reader(
            _DirectoryReader(artifact), f"release directory '{artifact}'"
        )
        return

    if not artifact.is_file():
        raise PackageCheckError(f"artifact does not exist: {artifact}")
    try:
        with zipfile.ZipFile(artifact) as archive:
            bad_member = archive.testzip()
            if bad_member is not None:
                raise PackageCheckError(
                    f"zip artifact '{artifact}' contains a corrupt member: {bad_member}"
                )
            _check_artifact_reader(_ZipReader(archive), f"zip artifact '{artifact}'")
    except zipfile.BadZipFile as exc:
        raise PackageCheckError(
            f"artifact is not a readable zip file: {artifact}"
        ) from exc


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Check either the source packaging contract or one real release "
            "directory/zip. Pass exactly one explicit mode."
        )
    )
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument(
        "--source-root",
        type=Path,
        help="source tree to check; this mode never inspects built artifacts",
    )
    modes.add_argument(
        "--artifact",
        type=Path,
        help="release directory or .zip file to inspect as a real artifact",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    try:
        if args.source_root is not None:
            _check_source_root(args.source_root.resolve())
            print(f"[source] Packaging source contract checks passed: {args.source_root}")
        else:
            artifact = args.artifact.resolve()
            _check_artifact(artifact)
            print(f"[artifact] Release artifact checks passed: {artifact}")
    except PackageCheckError as exc:
        print(f"release package check failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

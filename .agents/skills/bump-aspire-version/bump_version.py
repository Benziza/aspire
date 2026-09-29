#!/usr/bin/env python3

import argparse
import re
from pathlib import Path
import xml.etree.ElementTree as ET


def prepare_updates(versions, policy, version, branch, milestone):
    if not re.fullmatch(r"(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?", version):
        raise ValueError("Version must be X.Y or X.Y.Z with non-negative integer components.")
    if not re.fullmatch(r"[A-Za-z0-9._/-]+", branch):
        raise ValueError("Branch must be a literal branch name.")
    if not re.fullmatch(r"\d+\.\d+(?:\.(?:\d+|x))?", milestone):
        raise ValueError("Milestone must be a release title such as 17.0, 17.0.1, or 17.0.x.")

    components = version.split(".")
    if len(components) == 2:
        components.append("0")
    root = ET.fromstring(versions)
    group = root.find("PropertyGroup")
    if group is None or group.findtext("VersionPrefix") != "$(MajorVersion).$(MinorVersion).$(PatchVersion)":
        raise ValueError("Expected the composed VersionPrefix in the first property group.")
    for name, value in zip(("MajorVersion", "MinorVersion", "PatchVersion"), components):
        if len(root.findall(f".//{name}")) != 1 or group.find(name) is None:
            raise ValueError(f"Expected exactly one {name} in the first property group.")
        versions, count = re.subn(
            rf"(<{name}>)[0-9]+(</{name}>)",
            lambda match: match[1] + value + match[2],
            versions,
        )
        if count != 1:
            raise ValueError(f"Unexpected {name} format.")

    # The policy uses task blocks shaped as:
    #     - if:
    #       - targetsBranch:
    #           branch: main
    #       then:
    #       - addMilestone:
    #           milestone: 17.0
    # Match only this layout, preserving comments and unrelated rules rather than
    # reserializing YAML (which can reinterpret numeric milestone titles).
    tasks = list(re.finditer(r"^    - if:\r?\n.*?(?=^    - if:|\Z)", policy, re.M | re.S))
    target = re.compile(
        rf"^      - targetsBranch:\r?\n          branch: {re.escape(branch)}\r?$", re.M
    )
    matches = [task for task in tasks if target.search(task[0])]
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one milestone policy task for branch {branch!r}.")
    task = matches[0]
    if len(re.findall(r"^      - targetsBranch:", task[0], re.M)) != 1:
        raise ValueError("Target policy task must have exactly one branch condition.")
    updated_task, count = re.subn(
        r"(^      - addMilestone:\r?\n          milestone: )\d+\.\d+(?:\.(?:\d+|x))?(\r?$)",
        lambda match: match[1] + milestone + match[2],
        task[0],
        flags=re.M,
    )
    if count != 1:
        raise ValueError("Expected exactly one numeric release milestone in the target task.")
    policy = policy[:task.start()] + updated_task + policy[task.end():]
    return versions, policy


def main():
    parser = argparse.ArgumentParser(description="Update Aspire product version and milestone routing.")
    parser.add_argument("version", help="Product version: X.Y or X.Y.Z")
    parser.add_argument("--branch", required=True, help="Policy target branch, not the PR head branch")
    parser.add_argument("--milestone", required=True, help="Existing open GitHub milestone title")
    args = parser.parse_args()

    root = Path(__file__).resolve().parents[3]
    paths = (root / "eng/Versions.props", root / ".github/policies/milestoneAssignment.prClosed.yml")
    try:
        originals = tuple(path.read_bytes().decode("utf-8") for path in paths)
        updates = prepare_updates(*originals, args.version, args.branch, args.milestone)
        # Validate both inputs before writing either file.
        for path, original, updated in zip(paths, originals, updates):
            if original != updated:
                path.write_bytes(updated.encode("utf-8"))
                print(f"Updated {path.relative_to(root)}")
            else:
                print(f"Unchanged {path.relative_to(root)}")
    except (ValueError, ET.ParseError, OSError) as error:
        parser.exit(1, f"error: {error}\n")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Build a VRChat Creator Companion (VPM) repository listing from this repo's GitHub releases.

Every non-draft release that has both a `package.json` asset and a `<package-id>-<version>.zip`
asset becomes one version entry in the listing. The script writes `index.json` and the
website (`Website/index.html` with placeholders filled in) to the output directory.

Usage:  python .github/scripts/build_listing.py [output_dir]      (default: _site)
Env:    GITHUB_TOKEN (optional, raises the API rate limit), GITHUB_REPOSITORY (owner/repo)
Only the Python standard library is used.
"""
import hashlib
import html
import json
import os
import pathlib
import sys
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
CONFIG = json.loads((ROOT / ".github" / "vpm-listing.json").read_text(encoding="utf-8"))
REPO = os.environ.get("GITHUB_REPOSITORY") or CONFIG["githubRepo"]
TOKEN = os.environ.get("GITHUB_TOKEN")


def api_get(url):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "vpm-listing-builder"}
    if TOKEN:
        headers["Authorization"] = "Bearer " + TOKEN
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers)) as r:
        return json.loads(r.read())


def download(url):
    # browser_download_url of a public release asset; no auth header so redirects to the CDN work
    with urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "vpm-listing-builder"})) as r:
        return r.read()


def all_releases():
    releases, page = [], 1
    while True:
        batch = api_get(f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}")
        if not batch:
            return releases
        releases.extend(batch)
        page += 1


def version_key(v):
    parts = []
    for p in v.split("-", 1)[0].split("."):
        parts.append(int(p) if p.isdigit() else 0)
    return parts


def build_listing():
    package_ids = [p["id"] for p in CONFIG["packages"]]
    packages = {}
    for rel in all_releases():
        if rel.get("draft"):
            continue
        assets = {a["name"]: a for a in rel.get("assets", [])}
        manifest_asset = assets.get("package.json")
        if manifest_asset is None:
            continue
        manifest = json.loads(download(manifest_asset["browser_download_url"]))
        pkg_id = manifest.get("name")
        version = manifest.get("version")
        if pkg_id not in package_ids or not version:
            continue
        zip_asset = assets.get(f"{pkg_id}-{version}.zip")
        if zip_asset is None:
            print(f"skip {rel['tag_name']}: no {pkg_id}-{version}.zip asset", file=sys.stderr)
            continue
        manifest["url"] = zip_asset["browser_download_url"]
        manifest["zipSHA256"] = hashlib.sha256(download(zip_asset["browser_download_url"])).hexdigest()
        packages.setdefault(pkg_id, {"versions": {}})["versions"][version] = manifest
        print(f"added {pkg_id} {version}")
    return {
        "name": CONFIG["name"],
        "id": CONFIG["id"],
        "url": CONFIG["url"],
        "author": CONFIG["author"],
        "packages": packages,
    }


def render_site(listing, out):
    template = (ROOT / "Website" / "index.html").read_text(encoding="utf-8")
    rows = []
    for pkg_id, pkg in listing["packages"].items():
        versions = sorted(pkg["versions"], key=version_key, reverse=True)
        latest = pkg["versions"][versions[0]]
        rows.append(
            "<tr><td><b>{name}</b><br><code>{id}</code></td><td>{ver}</td><td>{desc}</td>"
            "<td><a href=\"{zip}\">zip</a></td></tr>".format(
                name=html.escape(latest.get("displayName", pkg_id)),
                id=html.escape(pkg_id),
                ver=html.escape(", ".join(versions)),
                desc=html.escape(latest.get("description", "")),
                zip=html.escape(latest["url"]),
            )
        )
    if not rows:
        rows.append("<tr><td colspan=\"4\">No releases yet.</td></tr>")
    page = (
        template.replace("{{LISTING_NAME}}", html.escape(listing["name"]))
        .replace("{{LISTING_URL}}", html.escape(listing["url"]))
        .replace("{{VCC_URL}}", html.escape("vcc://vpm/addRepo?url=" + urllib.parse.quote(listing["url"], safe="")))
        .replace("{{REPO_URL}}", html.escape(f"https://github.com/{REPO}"))
        .replace("{{PACKAGE_ROWS}}", "\n".join(rows))
    )
    (out / "index.html").write_text(page, encoding="utf-8")


def main():
    out = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "_site")
    out.mkdir(parents=True, exist_ok=True)
    listing = build_listing()
    (out / "index.json").write_text(json.dumps(listing, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    render_site(listing, out)
    count = sum(len(p["versions"]) for p in listing["packages"].values())
    print(f"wrote {out / 'index.json'} ({count} package versions)")


if __name__ == "__main__":
    main()

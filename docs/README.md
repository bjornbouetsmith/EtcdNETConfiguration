# Documentation

The user-facing documentation is written in Markdown under [`docs/wiki/`](wiki/) and published to the GitHub Wiki by the [`sync-wiki`](../.github/workflows/sync-wiki.yml) workflow.

## Why docs-as-code

Keeping the documentation in the main repository means:

- Changes are reviewed through normal pull requests.
- Documentation is versioned with the code.
- Links and examples can be checked in CI.
- The wiki is still the public reading experience.

The wiki repository is a publishing target. `docs/wiki/` is the source of truth.

## Editing the documentation

1. Edit the relevant page under `docs/wiki/`.
2. Add or update links in [`_Sidebar.md`](wiki/_Sidebar.md) when adding or renaming a page.
3. Open a pull request.
4. After the pull request merges to `main`, the sync workflow publishes the changes to the wiki.

Do not edit pages directly in the GitHub Wiki UI. The next sync overwrites them.

## Page names and links

- Page filenames use hyphens, for example `Configuration-Options.md`.
- The wiki page URL is the filename without `.md`, for example `/wiki/Configuration-Options`.
- Link between wiki pages with relative links, for example `[Configuration Options](Configuration-Options)`.
- Link to repository files with absolute URLs, for example `https://github.com/bjornbouetsmith/EtcdNETConfiguration/blob/main/Source/certs/generate-certs.sh`.
- Link from the README to the wiki with absolute URLs, for example `https://github.com/bjornbouetsmith/EtcdNETConfiguration/wiki/Configuration-Options`.
- Do not link to README anchors from wiki pages. Those anchors may change when the README is trimmed.

`_Sidebar.md` and `_Footer.md` are special wiki pages and are published like any other file in `docs/wiki/`.

## One-time workflow setup

The repository must have a wiki with at least one page before the first sync. This is already done if `Home` exists in the wiki.

The workflow needs a token that can push to the wiki repository:

1. Create a personal access token for an account that can write to the repository.
   - A classic PAT with the `repo` scope is the simplest option and is known to work.
   - Fine-grained PATs do not expose a dedicated wiki permission, so they may not be able to push to the wiki repository.
2. Store the token as the repository secret `WIKI_SYNC_TOKEN` under **Settings → Secrets and variables → Actions**.
3. Run the workflow manually once from **Actions → Sync wiki → Run workflow**, or merge a documentation change to `main`.

The token is only used by the sync workflow. Rotate it if it is exposed or no longer needed.

## How the sync works

On every push to `main` that changes `docs/wiki/**` (or the workflow itself), the workflow:

1. Checks out the repository.
2. Clones `https://github.com/bjornbouetsmith/EtcdNETConfiguration.wiki.git` using `WIKI_SYNC_TOKEN`.
3. Mirrors `docs/wiki/` into the wiki working tree with `rsync --delete`.
4. Commits and pushes if anything changed.

`rsync --delete` means the wiki is a mirror of `docs/wiki/`. Deleting a page from `docs/wiki/` also deletes it from the wiki, so remove its `_Sidebar.md` entry in the same pull request.

## Manual sync

If you need to publish without waiting for the workflow:

```bash
git clone https://github.com/bjornbouetsmith/EtcdNETConfiguration.wiki.git /tmp/EtcdNETConfiguration.wiki
rsync -a --delete --exclude '.git' docs/wiki/ /tmp/EtcdNETConfiguration.wiki/
cd /tmp/EtcdNETConfiguration.wiki
git add -A
git commit -m "docs: manual wiki sync"
git push
```

## Previewing

The Markdown pages can be previewed in any Markdown editor. To check the real wiki rendering, push a draft change to a test wiki or use the workflow's manual dispatch after merging.

Before opening a pull request, check:

- Internal wiki links use page filenames without `.md`.
- External links use absolute URLs.
- New pages are listed in `_Sidebar.md`.
- The README still contains enough information to use the package without visiting the wiki.

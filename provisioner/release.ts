// GitHub release for the current version: tag provisioner-v<version> on HEAD, release
// notes from provisioner/CHANGELOG.md, the packed zip attached.
//   bun provisioner/release.ts   (after bun provisioner/pack.ts, on a pushed commit)
// Needs the GitHub CLI (gh) logged in with push rights to this repository.
import { $ } from "bun";

const root = `${import.meta.dir}/..`;
const version = (await Bun.file(`${root}/VERSION`).text()).trim();
// Prefixed: this repository also carries ForgePlanner's own v<version> tags.
const tag = `provisioner-v${version}`;
const zip = `${root}/dist/Provisioner-${version}.zip`;
if (!(await Bun.file(zip).exists())) throw new Error(`${zip} missing; run bun provisioner/pack.ts`);

// This version's section of the changelog is the release text.
const log = await Bun.file(`${import.meta.dir}/CHANGELOG.md`).text();
const start = log.indexOf(`## ${version}\n`);
if (start < 0) throw new Error(`CHANGELOG.md has no "## ${version}" section`);
const next = log.indexOf("\n## ", start + 1);
const notes = log.slice(start + `## ${version}\n`.length, next < 0 ? undefined : next).trim();

// The repository is named explicitly: gh's default here is ForgePlanner,
// where this release must never land.
const url = (await $`git -C ${root} remote get-url origin`.text()).trim();
const repo = url.replace(/^.*github\.com[:/]/, "").replace(/\.git$/, "");
if (/^Biozip\//i.test(repo)) throw new Error(`origin is ${repo}; refusing to release there`);

// The tagged commit must be on the remote, or the release would point at nothing.
await $`git -C ${root} fetch -q origin`;
const head = (await $`git -C ${root} rev-parse HEAD`.text()).trim();
const pushed = (await $`git -C ${root} branch -r --contains ${head}`.nothrow().text()).trim();
if (!pushed) throw new Error("HEAD is not pushed; push the branch first");

const tags = (await $`git -C ${root} tag -l ${tag}`.text()).trim();
if (!tags) await $`git -C ${root} tag -a ${tag} -m ${"Provisioner " + version}`;
await $`git -C ${root} push -q origin ${tag}`;

const notesFile = `${root}/dist/release-notes-${version}.md`;
await Bun.write(notesFile, notes + "\n\nProvisioner is a version of [ForgePlanner](https://github.com/Biozip/ForgePlanner) by Biozip. Install it with a mod manager from Thunderstore, or drop `plugins/Provisioner.dll` from the zip into `BepInEx/plugins`.\n");
await $`gh release create ${tag} ${zip} -R ${repo} --title ${"Provisioner " + version} --notes-file ${notesFile} --verify-tag`.cwd(root);
console.log(`released ${tag} on ${repo}`);

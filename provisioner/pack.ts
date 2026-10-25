// Packs Provisioner for Thunderstore: dist/Provisioner-<version>.zip
//   bun provisioner/pack.ts   (after dotnet build -c Release)
// Layout: manifest.json, icon.png, README.md, CHANGELOG.md, LICENSE at the
// root, the plugin under plugins/. The version comes from VERSION and must
// match manifest.json.
import { mkdirSync } from "fs";
import { makeZip } from "./zip";

const root = `${import.meta.dir}/..`;
const here = import.meta.dir;
const version = (await Bun.file(`${root}/VERSION`).text()).trim();
const m = JSON.parse(await Bun.file(`${here}/manifest.json`).text());
if (m.version_number !== version) throw new Error(`manifest ${m.version_number} != VERSION ${version}`);

const read = async (path: string) => new Uint8Array(await Bun.file(path).arrayBuffer());
const files = [
  { name: "manifest.json", data: await read(`${here}/manifest.json`) },
  { name: "icon.png", data: await read(`${here}/icon.png`) },
  { name: "README.md", data: await read(`${here}/README.md`) },
  { name: "CHANGELOG.md", data: await read(`${here}/CHANGELOG.md`) },
  { name: "LICENSE", data: await read(`${here}/LICENSE`) },
  { name: "plugins/Provisioner.dll", data: await read(`${root}/bin/Release/Provisioner.dll`) },
];
const bytes = makeZip(files);
if (!(bytes[0] === 0x50 && bytes[1] === 0x4b)) throw new Error("not a zip");
mkdirSync(`${root}/dist`, { recursive: true });
const out = `${root}/dist/Provisioner-${version}.zip`;
await Bun.write(out, bytes);
console.log(`packed ${out} (${bytes.length} bytes)`);

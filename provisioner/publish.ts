// Publishes Provisioner to Thunderstore.
//   THUNDERSTORE_TEAM=<team> bun provisioner/publish.ts   (after bun provisioner/pack.ts)
// Token: $THUNDERSTORE_TOKEN_FILE, else ~/.config/thunderstore/token: a service
// account key for that team. Never printed.
// Thunderstore experimental API: usermedia initiate/finish upload, then
// submission/submit.
import { homedir } from "os";

const root = `${import.meta.dir}/..`;
const TEAM = process.env.THUNDERSTORE_TEAM;
if (!TEAM) throw new Error("set THUNDERSTORE_TEAM to the Thunderstore team that owns the package");
const BASE = "https://thunderstore.io/api/experimental";
const m = JSON.parse(await Bun.file(`${import.meta.dir}/manifest.json`).text());
const full = `${TEAM}-${m.name}-${m.version_number}`;
const zipPath = `${root}/dist/${m.name}-${m.version_number}.zip`;
const file = Bun.file(zipPath);
if (!(await file.exists())) throw new Error(`${zipPath} missing; run bun provisioner/pack.ts`);
const bytes = new Uint8Array(await file.arrayBuffer());

const exists = await fetch(`${BASE}/package/${TEAM}/${m.name}/${m.version_number}/`);
if (exists.ok) { console.error(`${full} is already published; bump the version first`); process.exit(1); }

const tokenFile = process.env.THUNDERSTORE_TOKEN_FILE ?? `${homedir()}/.config/thunderstore/token`;
const token = (await Bun.file(tokenFile).text()).trim();
const H = { Authorization: `Bearer ${token}`, "Content-Type": "application/json" };
const call = async (path: string, body: unknown) => {
  const r = await fetch(`${BASE}${path}`, { method: "POST", headers: H, body: JSON.stringify(body) });
  const text = await r.text();
  if (!r.ok) throw new Error(`${path} -> HTTP ${r.status}: ${text.slice(0, 500)}`);
  return JSON.parse(text);
};

const init = await call("/usermedia/initiate-upload/", { filename: `${full}.zip`, file_size_bytes: bytes.length });
const uuid = init.user_media.uuid as string;
const parts = [];
for (const p of init.upload_urls as { part_number: number; url: string; offset: number; length: number }[]) {
  const r = await fetch(p.url, { method: "PUT", body: bytes.slice(p.offset, p.offset + p.length) });
  if (!r.ok) throw new Error(`part ${p.part_number} upload -> HTTP ${r.status}`);
  parts.push({ ETag: r.headers.get("etag")!, PartNumber: p.part_number });
}
await call(`/usermedia/${uuid}/finish-upload/`, { parts });
console.log(`uploaded ${parts.length} part(s)`);

const res = await call("/submission/submit/", {
  author_name: TEAM,
  communities: ["valheim"],
  categories: [],
  community_categories: { valheim: ["mods", "crafting", "client-side", "utility"] },
  has_nsfw_content: false,
  upload_uuid: uuid,
});
console.log(`published ${res.package_version.full_name} -> ${res.available_communities?.[0]?.url ?? ""}`);

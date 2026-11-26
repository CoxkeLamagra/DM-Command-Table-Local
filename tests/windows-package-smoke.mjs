import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { once } from "node:events";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const bundle = path.resolve(process.argv[2]);
const profile = mkdtempSync(path.join(tmpdir(), "dmct-profile-"));
const address = "http://127.0.0.1:3210";
const windows = process.platform === "win32";
const desktop = process.argv.includes("--desktop");
const server = spawn(
  windows ? path.join(bundle, "node.exe") : process.execPath,
  [
    path.join(bundle, "launch.mjs"),
    "--no-browser",
    ...(desktop ? ["--desktop"] : []),
  ],
  {
    env: {
      ...process.env,
      LOCALAPPDATA: profile,
      DM_COMMAND_TABLE_DESKTOP_PORT: "3210",
      ...(desktop && {
        NODE_OPTIONS: `--import=${new URL("./desktop-catalogue-fixture.mjs", import.meta.url).href}`,
      }),
    },
    stdio: [desktop ? "pipe" : "ignore", "pipe", "inherit"],
  },
);
const stopped = once(server, "exit");
let token;
let cookie;

async function request(route, { expected = 200, json, body } = {}) {
  const response = await fetch(address + route, {
    method: json === undefined && body === undefined ? "GET" : "POST",
    headers: {
      ...(cookie && { cookie }),
      ...(json !== undefined && { "content-type": "application/json" }),
      origin: address,
    },
    body: json === undefined ? body : JSON.stringify(json),
    signal: AbortSignal.timeout(15000),
  });
  if (response.status !== expected)
    assert.fail(
      `${route}: expected ${expected}, got ${response.status}: ${await response.text()}`,
    );
  return response;
}

try {
  let output = "";
  server.stdout.setEncoding("utf8");
  server.stdout.on("data", (chunk) => {
    output += chunk;
    token = output.match(/First-time registration token: ([a-f0-9]{32})/)?.[1];
  });
  for (
    let attempt = 0;
    !token && server.exitCode === null && attempt < 100;
    attempt++
  )
    await new Promise((resolve) => setTimeout(resolve, 500));
  assert.ok(token, output);
  assert.equal((await (await request("/api/health")).json()).status, "ready");
  await (await request("/")).text();
  await (await request("/api/campaigns", { expected: 401 })).json();

  const register = await request("/api/auth", {
    expected: 201,
    json: {
      action: "register",
      username: "windows-test",
      password: "disposable-password",
      bootstrapToken: token,
    },
  });
  cookie = register.headers.getSetCookie()[0]?.split(";")[0];
  assert.ok(cookie);
  await register.json();

  const form = new FormData();
  const png = Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAACXBIWXMAAAPoAAAD6AG1e1JrAAAARUlEQVRYhe3XsREAMAhC0YzGKOzi4GaLpHmFvXcifM40+3OOBcYJSoTxhsuIhhVXGEUcLyAZSFZQGli+ismoZlVO81wHF+/niHkNYwSEAAAAAElFTkSuQmCC",
    "base64",
  );
  form.append("file", new Blob([png], { type: "image/png" }), "map.png");
  const { screenshot } = await (
    await request("/api/screenshots", { expected: 201, body: form })
  ).json();
  const { campaign } = await (
    await request("/api/campaigns", {
      expected: 201,
      json: {
        name: "Portable campaign",
        notes: `<img src="/api/screenshots/${screenshot.id}">`,
      },
    })
  ).json();

  if (desktop) {
    const { monsters } = await (
      await request("/api/desktop-catalogue", {
        json: {
          campaignId: campaign.id,
          query: "goblin",
          local: JSON.stringify({
            monster: [
              {
                name: "Desktop goblin",
                source: "LOCAL",
                ac: [15],
                hp: { average: 7 },
              },
            ],
          }),
        },
      })
    ).json();
    assert.equal(monsters.length, 1);
    assert.equal(monsters[0].monster.armorClass, 15);
    const remote = await (
      await request("/api/desktop-catalogue", {
        json: { campaignId: campaign.id, query: "remote goblin" },
      })
    ).json();
    assert.equal(remote.monsters[0].monster.name, "Remote goblin");
    assert.equal(
      remote.monsters[0].tokenUrl,
      "https://dnd5e.lamagra.link/img/bestiary/tokens/MM/Remote%20goblin.webp",
    );
    await (
      await request(`/api/campaigns/${campaign.id}/monsters`, {
        expected: 201,
        json: remote.monsters[0].monster,
      })
    ).json();
    await (
      await request(`/api/campaigns/${campaign.id}/monsters`, {
        expected: 201,
        json: monsters[0].monster,
      })
    ).json();
  }
  if (!desktop)
    await (
      await request("/api/desktop-catalogue", { expected: 404, json: {} })
    ).json();
  const portable = await (
    await request(`/api/campaigns/${campaign.id}/export`)
  ).json();
  assert.equal(portable.format, "dm-command-table");
  const plain = { ...portable, campaign: { ...portable.campaign, notes: "" } };
  await (
    await request("/api/campaigns/import", { expected: 201, json: plain })
  ).json();
  const pack = await (
    await request(`/api/campaigns/${campaign.id}/package`)
  ).json();
  assert.equal(pack.format, "dmct-campaign-package");
  assert.equal(pack.assets.length, 1);
  const { campaign: imported } = await (
    await request("/api/campaigns/import", { expected: 201, json: pack })
  ).json();
  assert.notEqual(imported.id, campaign.id);
  const image = imported.notes.match(/\/api\/screenshots\/[a-f0-9-]+/)?.[0];
  assert.ok(image);
  assert.notEqual(image, `/api/screenshots/${screenshot.id}`);
  const response = await request(image);
  assert.equal(response.headers.get("content-type"), "image/webp");
  assert.ok((await response.arrayBuffer()).byteLength);
  console.log(
    "Packaged startup, registration, SQLite, image processing and both campaign formats passed.",
  );
} finally {
  if (server.exitCode === null && server.pid) {
    if (desktop) server.stdin.end();
    else if (windows)
      execFileSync("taskkill.exe", ["/pid", String(server.pid), "/t", "/f"]);
    else server.kill();
  }
  await stopped;
  rmSync(profile, { recursive: true, force: true, maxRetries: 5 });
}

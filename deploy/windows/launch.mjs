import { spawn } from "node:child_process";
import { randomBytes } from "node:crypto";
import {
  lstatSync,
  mkdirSync,
  readFileSync,
  realpathSync,
  rmdirSync,
  symlinkSync,
} from "node:fs";
import { createServer } from "node:net";
import path from "node:path";

const runtime = path.join(import.meta.dirname, "app");
const data = path.join(process.env.LOCALAPPDATA, "DM Command Table");
mkdirSync(data, { recursive: true });

// ZIP extraction needs no symlink support. Directory junctions restore the
// traced Node module layout without administrator or Developer Mode access.
for (const { link, target } of JSON.parse(
  readFileSync(path.join(import.meta.dirname, "links.json"), "utf8"),
)) {
  const destination = path.join(runtime, link);
  const source = path.join(runtime, target);
  const existing = lstatSync(destination, { throwIfNoEntry: false });
  if (existing?.isSymbolicLink()) {
    try {
      if (realpathSync(destination) === realpathSync(source)) continue;
    } catch (error) {
      if (error.code !== "ENOENT") throw error;
    }
    rmdirSync(destination);
  } else if (existing) {
    throw new Error(`A package directory blocks the dependency link: ${link}`);
  }
  symlinkSync(source, destination, "junction");
}

const hostname = "127.0.0.1";
const desktop = process.argv.includes("--desktop");
const port = desktop ? Number(process.env.DM_COMMAND_TABLE_DESKTOP_PORT) : 3210;
if (!Number.isInteger(port) || port < 1024 || port > 65535)
  throw new Error("Invalid desktop engine port.");
const address = `http://${hostname}:${port}`;
const probe = createServer();
await new Promise((resolve, reject) => {
  probe.once("error", reject);
  probe.listen(port, hostname, () => probe.close(resolve));
}).catch((error) => {
  if (error.code === "EADDRINUSE")
    throw new Error(
      `Port ${port} is in use. If DM Command Table is already open, return to ${address}.`,
    );
  throw error;
});

const token = randomBytes(16).toString("hex");
const server = spawn(process.execPath, [path.join(runtime, "server.js")], {
  cwd: runtime,
  stdio: "inherit",
  env: {
    ...process.env,
    NODE_ENV: "production",
    NODE_PATH: "",
    NEXT_TELEMETRY_DISABLED: "1",
    HOSTNAME: hostname,
    PORT: String(port),
    DM_COMMAND_TABLE_DESKTOP: desktop ? "1" : "0",
    DM_COMMAND_TABLE_TRUST_PROXY: "false",
    DM_COMMAND_TABLE_SECURE_COOKIES: "auto",
    DM_COMMAND_TABLE_DB_PATH: path.join(data, "dm-command-table.sqlite"),
    DM_COMMAND_TABLE_UPLOAD_PATH: path.join(data, "uploads"),
    DM_COMMAND_TABLE_BACKUP_PATH: path.join(data, "backups"),
    DM_COMMAND_TABLE_BOOTSTRAP_TOKEN: token,
  },
});
const exited = new Promise((resolve) => {
  server.once("error", (error) => {
    console.error(error);
    resolve(1);
  });
  server.once("exit", (code) => resolve(code ?? 1));
});
for (const signal of ["SIGINT", "SIGTERM"])
  process.once(signal, () => server.kill());

async function isReady() {
  try {
    const response = await fetch(`${address}/api/health`, {
      signal: AbortSignal.timeout(1000),
    });
    return response.ok && (await response.json()).status === "ready";
  } catch {
    return false;
  }
}

if (desktop) {
  process.stdin.once("end", () => server.kill());
  process.stdin.resume();
}

let ready = false;
try {
  for (let attempt = 0; attempt < 60 && !ready; attempt++) {
    if (!server.pid || server.exitCode !== null || server.signalCode !== null)
      break;
    ready = await isReady();
    if (!ready) await new Promise((resolve) => setTimeout(resolve, 500));
  }
  if (!ready)
    throw new Error(
      "DM Command Table did not start. Check the server output above.",
    );
  console.log(`\nDM Command Table is ready at ${address}`);
  console.log(`First-time registration token: ${token}`);
  console.log(
    `Local data: ${data}\nKeep this window open while you use the app.`,
  );
  if (!process.argv.includes("--no-browser")) {
    const browser = spawn(
      "rundll32.exe",
      ["url.dll,FileProtocolHandler", address],
      {
        stdio: "ignore",
      },
    );
    browser.on("error", () => console.log(`Open ${address} in your browser.`));
  }
  process.exitCode = await exited;
} finally {
  if (desktop) process.stdin.pause();
  if (server.exitCode === null) server.kill();
}

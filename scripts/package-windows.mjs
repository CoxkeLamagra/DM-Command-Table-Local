import { execFileSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  realpathSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const standalone = path.join(root, ".next/standalone");
if (!existsSync(path.join(standalone, ".next/static")))
  throw new Error("Run pnpm build before packaging.");
const version = JSON.parse(
  readFileSync(path.join(root, "package.json")),
).version;
const nodeVersion = readFileSync(
  path.join(root, ".node-version"),
  "utf8",
).trim();
const lockfile = readFileSync(path.join(root, "pnpm-lock.yaml"), "utf8");
const dist = path.join(root, "dist");
const name = `DM-Command-Table-${version}-windows-x64`;
const archive = path.join(dist, `${name}.zip`);
if (existsSync(archive))
  throw new Error(`Move the existing archive first: ${archive}`);
mkdirSync(dist, { recursive: true });
const stage = mkdtempSync(path.join(dist, "windows-"));
const bundleName = "DMCT";
const bundle = path.join(stage, bundleName);
const runtime = path.join(bundle, "app");
const links = [];

// pnpm's peer-dependency directory names can exceed Explorer's path limit.
function packagedPath(relative) {
  const parts = relative.split(path.sep);
  if (
    parts[0] === "node_modules" &&
    parts[1] === ".pnpm" &&
    parts[2]?.includes("@")
  ) {
    parts[2] = createHash("sha256").update(parts[2]).digest("hex").slice(0, 16);
  }
  const packaged = parts.join(path.sep);
  if (path.join(bundleName, "app", packaged).length > 180)
    throw new Error(
      `Windows archive path exceeds its length budget: ${packaged}`,
    );
  return packaged;
}

function copyRuntime(folder) {
  mkdirSync(
    path.join(runtime, packagedPath(path.relative(standalone, folder))),
    {
      recursive: true,
    },
  );
  for (const entry of readdirSync(folder, { withFileTypes: true })) {
    const filename = path.join(folder, entry.name);
    const relative = path.relative(standalone, filename);
    if (entry.isSymbolicLink()) {
      const target = path.relative(standalone, realpathSync(filename));
      if (target.startsWith("..") || path.isAbsolute(target))
        throw new Error(`Dependency link escapes the runtime: ${relative}`);
      links.push({
        link: packagedPath(relative),
        target: packagedPath(target),
      });
    } else if (entry.isDirectory()) {
      copyRuntime(filename);
    } else {
      copyFileSync(filename, path.join(runtime, packagedPath(relative)));
    }
  }
}

function run(program, args, cwd = root) {
  execFileSync(program, args, { cwd, stdio: "inherit" });
}

function download(url, filename) {
  run("curl", [
    "--fail",
    "--silent",
    "--show-error",
    "--location",
    "--retry",
    "2",
    "--proto",
    "=https",
    "--proto-redir",
    "=https",
    "--output",
    filename,
    url,
  ]);
}

function digest(filename, algorithm, encoding) {
  return createHash(algorithm).update(readFileSync(filename)).digest(encoding);
}

try {
  copyRuntime(standalone);
  for (const filename of ["Start.cmd", "launch.mjs", "README.txt"])
    copyFileSync(
      path.join(root, "deploy/windows", filename),
      path.join(bundle, filename),
    );
  writeFileSync(
    path.join(bundle, "links.json"),
    JSON.stringify(links, null, 2),
  );

  const nodeBase = `node-v${nodeVersion}-win-x64`;
  const nodeZip = path.join(stage, `${nodeBase}.zip`);
  const checksums = path.join(stage, "SHASUMS256.txt");
  const nodeUrl = `https://nodejs.org/dist/v${nodeVersion}`;
  download(`${nodeUrl}/SHASUMS256.txt`, checksums);
  const expected = readFileSync(checksums, "utf8")
    .split(/\r?\n/)
    .map((line) => line.trim().split(/\s+/))
    .find(([, file]) => file === `${nodeBase}.zip`)?.[0];
  download(`${nodeUrl}/${nodeBase}.zip`, nodeZip);
  if (!expected || digest(nodeZip, "sha256", "hex") !== expected)
    throw new Error(
      "The Windows Node.js download failed checksum verification.",
    );
  run("unzip", [
    "-q",
    nodeZip,
    `${nodeBase}/node.exe`,
    `${nodeBase}/LICENSE`,
    "-d",
    stage,
  ]);
  copyFileSync(
    path.join(stage, nodeBase, "node.exe"),
    path.join(bundle, "node.exe"),
  );
  copyFileSync(
    path.join(stage, nodeBase, "LICENSE"),
    path.join(bundle, "Node-LICENSE.txt"),
  );

  // Load the Windows build of Sharp from the exact lockfile entry even when
  // Next was built on Linux. Node resolves this from the runtime's module root.
  const sharpPackage = "@img/sharp-win32-x64";
  const sharpEntries = [
    ...lockfile.matchAll(
      /^  '@img\/sharp-win32-x64@([^']+)':\r?\n    resolution: \{integrity: sha512-([^}]+)\}/gm,
    ),
  ];
  if (sharpEntries.length !== 1)
    throw new Error("Expected exactly one locked Windows Sharp package.");
  const [, sharpVersion, sharpChecksum] = sharpEntries[0];
  const sharpTar = path.join(stage, "sharp.tgz");
  download(
    `https://registry.npmjs.org/${sharpPackage}/-/sharp-win32-x64-${sharpVersion}.tgz`,
    sharpTar,
  );
  if (digest(sharpTar, "sha512", "base64") !== sharpChecksum)
    throw new Error(
      "The Windows Sharp download failed lockfile integrity verification.",
    );
  const sharpDestination = path.join(runtime, "node_modules", sharpPackage);
  mkdirSync(sharpDestination, { recursive: true });
  run("tar", [
    "-xzf",
    sharpTar,
    "--strip-components=1",
    "-C",
    sharpDestination,
  ]);
  run("zip", ["-q", "-r", path.join(stage, `${name}.zip`), bundleName], stage);
  copyFileSync(path.join(stage, `${name}.zip`), archive);
  writeFileSync(
    `${archive}.sha256`,
    `${digest(archive, "sha256", "hex")}  ${name}.zip\n`,
  );
  console.log(archive);
} finally {
  rmSync(stage, { recursive: true, force: true });
}

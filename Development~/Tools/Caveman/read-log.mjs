import { spawnSync } from "node:child_process";
import { createReadStream, openSync, closeSync, fstatSync, readFileSync, readSync, existsSync } from "node:fs";
import { join, resolve } from "node:path";
import { pipeline } from "node:stream/promises";
import { fileURLToPath } from "node:url";

const directory = fileURLToPath(new URL(".", import.meta.url));
const shortLimit = 32 * 1024;
const lineLimit = 200;
const maxInput = 8 * 1024 * 1024;
const home = process.env.CAVEMAN_HOME || join(directory, ".state");

async function main(args) {
  const raw = args[0] === "--raw";
  const rest = raw ? args.slice(1) : args;
  if (rest.length !== 1 || rest[0] === "--help") {
    console.error("Формат: read-log.cmd [--raw] <путь к логу>");
    return rest[0] === "--help" ? 0 : 2;
  }
  const path = resolve(rest[0]);
  const fd = openSync(path, "r");
  try {
    const file = fstatSync(fd);
    if (!file.isFile()) throw new Error("Нужен обычный файл лога.");
    if (raw) {
      await pipeline(createReadStream(path, { fd, autoClose: false }), process.stdout);
      return 0;
    }
    if (file.size > maxInput) {
      console.error("Лог больше 8 МиБ. Сначала найдите нужные строки или ограничьте диапазон чтения.");
      return 3;
    }
    if (file.size <= shortLimit) {
      const buffer = Buffer.alloc(shortLimit + 1);
      let length = 0;
      while (length < buffer.length) {
        const bytes = readSync(fd, buffer, length, buffer.length - length, length);
        if (!bytes) break;
        length += bytes;
      }
      const text = buffer.subarray(0, length);
      let lines = length && text[length - 1] !== 10 ? 1 : 0;
      for (const byte of text) if (byte === 10) lines++;
      if (length <= shortLimit && lines <= lineLimit) {
        process.stdout.write(text);
        return 0;
      }
    }
  } finally {
    closeSync(fd);
  }

  const entry = join(directory, "node_modules", "@caveman-ai", "cli", "dist", "index.js");
  const engine = process.env.CAVEMAN_ENGINE_BIN ||
    join(home, "bin", process.platform === "win32" ? "caveman-engine.exe" : "caveman-engine");
  let config;
  try {
    config = JSON.parse(readFileSync(join(home, "cloud.json"), "utf8"));
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
  }
  if (config?.modules?.input === false) {
    console.error("Read less выключен. Прочитайте нужный диапазон оригинала.");
    return 3;
  }
  if (!existsSync(entry) || !existsSync(engine)) {
    console.error("Caveman недоступен. Прочитайте нужный диапазон оригинала.");
    return 3;
  }
  const result = spawnSync(process.execPath, [entry, "shrink", "--file", path], {
    env: {
      ...process.env,
      DO_NOT_TRACK: "1",
      CAVEMAN_HOME: home,
      CAVEMAN_ENGINE_BIN: engine,
      CAVE_MAX_SHRINK_BYTES: String(maxInput),
    },
    maxBuffer: shortLimit,
    timeout: 30_000,
    windowsHide: true,
  });
  let handle;
  try {
    handle = JSON.parse(result.stderr.toString().trim().split("\n").at(-1)).recovery_handle;
  } catch {
    // The engine may return an uncompressed transcript without a report.
  }
  if (result.status !== 0 || !/^ccr_[a-f0-9]+$/.test(handle ?? "") ||
      !result.stdout?.toString().includes(`recover: caveman retrieve ${handle}›`)) {
    console.error("Нет компактной сводки Caveman. Найдите нужные строки или прочитайте оригинал по диапазону.");
    return 3;
  }
  process.stdout.write(result.stdout);
  return 0;
}

try {
  process.exitCode = await main(process.argv.slice(2));
} catch (error) {
  console.error(error.message);
  process.exitCode = 2;
}

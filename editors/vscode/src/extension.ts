import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import * as os from "node:os";
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";

const REPO_URL = "https://github.com/2sky/Vidyano.Core";
const INSTALL_CMD = "dotnet tool install -g Vidyano.Script.Tool";
const UPDATE_CMD = "dotnet tool update -g Vidyano.Script.Tool";

// Bump this when the extension starts relying on a server capability added in a later tool release.
// `vidyano lsp` advertises serverInfo.version (>= 5.59.0), so the skew check is live; tools predating
// that simply report no version and are skipped (see checkVersionSkew).
const MIN_SERVER_VERSION = "5.59.0";

let client: LanguageClient | undefined;
let log: vscode.OutputChannel | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  log = vscode.window.createOutputChannel("Vidyano Script");
  context.subscriptions.push(log);

  const cfg = vscode.workspace.getConfiguration("vidyano");
  const cmd = resolveVidyano(cfg.get<string>("path", "").trim());
  if (!cmd) {
    log.appendLine("[activate] Could not locate the 'vidyano' executable (PATH scan and dotnet global-tools dir both failed).");
    await promptInstall();
    return;
  }
  log.appendLine(`[activate] Using vidyano: ${cmd}`);

  const serverOptions: ServerOptions = {
    command: cmd,
    args: ["lsp"],
    transport: TransportKind.stdio,
  };

  // Client id "vidyano" makes the v9 client read the `vidyano.trace.server` setting automatically.
  // Reuse our output channel so activation logs, client logs, and trace all land in one place.
  const clientOptions: LanguageClientOptions = {
    documentSelector: [{ scheme: "file", language: "visc" }],
    outputChannel: log,
    synchronize: {
      fileEvents: vscode.workspace.createFileSystemWatcher("**/*.visc"),
    },
  };

  client = new LanguageClient("vidyano", "Vidyano Script", serverOptions, clientOptions);
  try {
    await client.start();
    log.appendLine("[activate] Language server started.");
  } catch (err) {
    const detail = err instanceof Error ? err.message : String(err);
    log.appendLine(`[activate] Language server failed to start: ${detail}`);
    log.show(true);
    void vscode.window.showErrorMessage(
      `Vidyano: the language server failed to start (${detail}). See the "Vidyano Script" output channel.`,
    );
    return;
  }

  // Best-effort version-skew check (spec Section 7).
  await checkVersionSkew(client);
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}

// Resolve `vidyano` to an absolute executable path by scanning PATH ourselves. We deliberately do NOT
// rely on a bare-name spawn: Node's child_process does not apply Windows PATHEXT to a bare command, so
// `spawn("vidyano")` would ENOENT against the `vidyano.exe` that `dotnet tool install` produces — a false
// "not found" on Windows. Returning a full path makes the later LanguageClient spawn robust on every OS.
function resolveVidyano(override: string): string | null {
  if (override) {
    // An explicit path: accept it verbatim, or (on Windows) with a PATHEXT extension appended.
    const hit = (path.isAbsolute(override) || override.includes(path.sep))
      ? firstExisting(executableCandidates(override))
      : searchPath(override);
    log?.appendLine(`[resolve] override "${override}" -> ${hit ?? "not found"}`);
    return hit;
  }
  const onPath = searchPath("vidyano");
  if (onPath) {
    log?.appendLine(`[resolve] found on PATH -> ${onPath}`);
    return onPath;
  }
  // Fallback: `dotnet tool install -g` always lands the tool in ~/.dotnet/tools, which is not always on
  // the editor's PATH (the editor's environment can lag the shell's). Probe that canonical dir directly.
  const toolsDir = path.join(os.homedir(), ".dotnet", "tools");
  const canonical = firstExisting(executableCandidates(path.join(toolsDir, "vidyano")));
  log?.appendLine(`[resolve] PATH miss; dotnet tools dir ${toolsDir} -> ${canonical ?? "not found"}`);
  return canonical;
}

// The executable filenames to try for a base name. On Windows a bare name needs an executable extension;
// mirror cmd.exe by trying each PATHEXT entry (unless the name already carries an extension).
function executableCandidates(base: string): string[] {
  if (process.platform !== "win32" || path.extname(base)) {
    return [base];
  }
  const exts = (process.env.PATHEXT ?? ".COM;.EXE;.BAT;.CMD").split(";").filter(Boolean);
  return exts.map((e) => base + e.toLowerCase());
}

function searchPath(base: string): string | null {
  const dirs = (process.env.PATH ?? "").split(path.delimiter).filter(Boolean);
  for (const dir of dirs) {
    const hit = firstExisting(executableCandidates(base).map((c) => path.join(dir, c)));
    if (hit) {
      return hit;
    }
  }
  return null;
}

function firstExisting(candidates: string[]): string | null {
  for (const c of candidates) {
    if (fs.existsSync(c)) {
      return c;
    }
  }
  return null;
}

async function promptInstall(): Promise<void> {
  const pick = await vscode.window.showErrorMessage(
    "The 'vidyano' command was not found. Install the Vidyano.Script tool to enable .visc language support.",
    "Copy install command",
    "Open docs",
  );
  if (pick === "Copy install command") {
    await vscode.env.clipboard.writeText(INSTALL_CMD);
    void vscode.window.showInformationMessage(`Copied: ${INSTALL_CMD}`);
  } else if (pick === "Open docs") {
    void vscode.env.openExternal(vscode.Uri.parse(REPO_URL));
  }
}

async function checkVersionSkew(activeClient: LanguageClient): Promise<void> {
  // In-band serverInfo.version, advertised by `vidyano lsp` (>= 5.59.0).
  const reported = activeClient.initializeResult?.serverInfo?.version;
  if (!reported) {
    // A tool predating serverInfo.version reports nothing. Silently skip rather than guess — there is
    // no `vidyano --version` to fall back on (that command does not exist and would exit 64).
    return;
  }
  if (compareSemver(reported, MIN_SERVER_VERSION) < 0) {
    const pick = await vscode.window.showWarningMessage(
      `The installed Vidyano.Script tool (${reported}) is older than the minimum supported by this extension (${MIN_SERVER_VERSION}). Some features may not work.`,
      "Copy update command",
    );
    if (pick === "Copy update command") {
      await vscode.env.clipboard.writeText(UPDATE_CMD);
      void vscode.window.showInformationMessage(`Copied: ${UPDATE_CMD}`);
    }
  }
}

// Minimal numeric major.minor.patch comparison; ignores pre-release tags (none used in this repo).
function compareSemver(a: string, b: string): number {
  const pa = a.split(".").map((n) => parseInt(n, 10) || 0);
  const pb = b.split(".").map((n) => parseInt(n, 10) || 0);
  for (let i = 0; i < 3; i++) {
    if ((pa[i] ?? 0) !== (pb[i] ?? 0)) {
      return (pa[i] ?? 0) - (pb[i] ?? 0);
    }
  }
  return 0;
}

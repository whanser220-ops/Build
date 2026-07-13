import { promises as fs } from "node:fs";
import path from "node:path";
import type {
  BundleReport,
  BundleReportIndex,
  BundleReportIndexEntry
} from "@/types/report";

const reportFileName = "bundle_report.json";
const latestFileName = "latest.json";
const indexFileName = "index.json";

export function getBasePath(): string {
  return process.env.NEXT_PUBLIC_BASE_PATH || "/bundle-report";
}

export function getReportDataDir(): string {
  return path.resolve(
    process.env.BUNDLE_REPORT_DATA_DIR ||
      path.join(process.cwd(), "..", "..", ".workspace", "artifacts", "bundle-report")
  );
}

export async function getReportIndex(): Promise<BundleReportIndex> {
  const dataDir = getReportDataDir();
  const indexPath = path.join(dataDir, indexFileName);

  if (await fileExists(indexPath)) {
    const index = await readJson<BundleReportIndex>(indexPath);
    return {
      latestReportId: index.latestReportId || index.reports[0]?.id || "",
      reports: sortIndexEntries(index.reports || [])
    };
  }

  return scanReportIndex(dataDir);
}

export async function getLatestReport(): Promise<BundleReport | null> {
  const dataDir = getReportDataDir();
  const latestPath = path.join(dataDir, latestFileName);
  if (await fileExists(latestPath)) {
    return readJson<BundleReport>(latestPath);
  }

  const index = await getReportIndex();
  const latestId = index.latestReportId || index.reports[0]?.id;
  if (!latestId) {
    return null;
  }

  return getReportById(latestId);
}

export async function getReportById(id: string): Promise<BundleReport | null> {
  const decodedId = decodeURIComponent(id || "");
  const dataDir = getReportDataDir();
  const index = await getReportIndex();
  const entry = index.reports.find((item) => item.id === decodedId);

  if (entry) {
    const reportPath = path.resolve(dataDir, entry.reportPath);
    if (reportPath.startsWith(dataDir) && (await fileExists(reportPath))) {
      return readJson<BundleReport>(reportPath);
    }
  }

  const reportPaths = await findReportFiles(dataDir);
  for (const reportPath of reportPaths) {
    const report = await readJson<BundleReport>(reportPath);
    if (report.summary.reportId === decodedId) {
      return report;
    }
  }

  return null;
}

async function scanReportIndex(dataDir: string): Promise<BundleReportIndex> {
  const reportPaths = await findReportFiles(dataDir);
  const entries: BundleReportIndexEntry[] = [];

  for (const reportPath of reportPaths) {
    const report = await readJson<BundleReport>(reportPath);
    entries.push({
      id: report.summary.reportId,
      buildTarget: report.summary.buildTarget,
      packageName: report.summary.packageName,
      packageVersion: report.summary.packageVersion,
      generatedAt: report.summary.generatedAt,
      reportPath: toPortableRelativePath(dataDir, reportPath),
      bundleCount: report.summary.bundleCount,
      duplicateAssetCount: report.summary.duplicateAssetCount,
      smallBundleCount: report.summary.smallBundleCount,
      largeBundleCount: report.summary.largeBundleCount,
      maxDependencyDepth: report.summary.maxDependencyDepth
    });
  }

  const reports = sortIndexEntries(entries);
  return {
    latestReportId: reports[0]?.id || "",
    reports
  };
}

async function findReportFiles(root: string): Promise<string[]> {
  if (!(await fileExists(root))) {
    return [];
  }

  const found: string[] = [];
  await walk(root, found);
  return found.sort((left, right) => left.localeCompare(right));
}

async function walk(directory: string, found: string[]): Promise<void> {
  const entries = await fs.readdir(directory, { withFileTypes: true });
  for (const entry of entries) {
    const childPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      await walk(childPath, found);
      continue;
    }

    if (entry.isFile() && entry.name === reportFileName) {
      found.push(childPath);
    }
  }
}

async function readJson<T>(filePath: string): Promise<T> {
  return JSON.parse(await fs.readFile(filePath, "utf8")) as T;
}

async function fileExists(filePath: string): Promise<boolean> {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

function sortIndexEntries(entries: BundleReportIndexEntry[]): BundleReportIndexEntry[] {
  return [...entries].sort((left, right) => {
    const dateCompare = right.generatedAt.localeCompare(left.generatedAt);
    if (dateCompare !== 0) {
      return dateCompare;
    }

    return right.packageVersion.localeCompare(left.packageVersion);
  });
}

function toPortableRelativePath(root: string, filePath: string): string {
  return path.relative(root, filePath).split(path.sep).join("/");
}

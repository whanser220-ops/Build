"use client";

import { useMemo, useState } from "react";
import type {
  BundleInfo,
  BundleReport,
  BundleReportIndex,
  DependencyChain,
  DuplicateAsset
} from "@/types/report";

type ReportDashboardProps = {
  basePath: string;
  initialIndex: BundleReportIndex;
  initialReport: BundleReport | null;
};

type Filters = {
  moduleOwner: string;
  assetType: string;
  query: string;
};

const pageSize = 50;

export default function ReportDashboard({
  basePath,
  initialIndex,
  initialReport
}: ReportDashboardProps) {
  const [reportIndex] = useState(initialIndex);
  const [report, setReport] = useState<BundleReport | null>(initialReport);
  const [selectedReportId, setSelectedReportId] = useState(
    initialReport?.summary.reportId || initialIndex.latestReportId || ""
  );
  const [filters, setFilters] = useState<Filters>({
    moduleOwner: "",
    assetType: "",
    query: ""
  });
  const [duplicatePage, setDuplicatePage] = useState(1);
  const [bundlePage, setBundlePage] = useState(1);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState("");

  const modules = useMemo(() => {
    if (!report) {
      return [];
    }

    return Array.from(
      new Set([
        ...report.bundles.map((bundle) => bundle.moduleOwner),
        ...report.duplicateAssets.flatMap((asset) => asset.moduleOwners)
      ].filter(Boolean))
    ).sort((left, right) => left.localeCompare(right));
  }, [report]);

  const assetTypes = useMemo(() => {
    if (!report) {
      return [];
    }

    return Array.from(new Set(report.duplicateAssets.map((asset) => asset.assetType).filter(Boolean)))
      .sort((left, right) => left.localeCompare(right));
  }, [report]);

  const filteredDuplicates = useMemo(() => {
    if (!report) {
      return [];
    }

    const query = filters.query.trim().toLowerCase();
    return report.duplicateAssets.filter((asset) => {
      const moduleMatch =
        !filters.moduleOwner || asset.moduleOwners.some((owner) => owner === filters.moduleOwner);
      const typeMatch = !filters.assetType || asset.assetType === filters.assetType;
      const queryMatch =
        !query ||
        asset.assetPath.toLowerCase().includes(query) ||
        asset.bundles.some((bundle) => bundle.bundleName.toLowerCase().includes(query));
      return moduleMatch && typeMatch && queryMatch;
    });
  }, [filters, report]);

  const filteredBundles = useMemo(() => {
    if (!report) {
      return [];
    }

    const query = filters.query.trim().toLowerCase();
    return report.bundles.filter((bundle) => {
      const moduleMatch = !filters.moduleOwner || bundle.moduleOwner === filters.moduleOwner;
      const queryMatch =
        !query ||
        bundle.bundleName.toLowerCase().includes(query) ||
        bundle.fileName.toLowerCase().includes(query) ||
        bundle.moduleOwner.toLowerCase().includes(query);
      return moduleMatch && queryMatch;
    });
  }, [filters, report]);

  const duplicateRows = paginate(filteredDuplicates, duplicatePage);
  const bundleRows = paginate(filteredBundles, bundlePage);

  async function selectReport(reportId: string) {
    setSelectedReportId(reportId);
    if (!reportId) {
      return;
    }

    setIsLoading(true);
    setLoadError("");
    try {
      const normalizedBasePath = basePath.endsWith("/") ? basePath.slice(0, -1) : basePath;
      const response = await fetch(`${normalizedBasePath}/api/reports/${encodeURIComponent(reportId)}`, {
        cache: "no-store"
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      setReport((await response.json()) as BundleReport);
      resetFilters();
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : "加载报告失败");
    } finally {
      setIsLoading(false);
    }
  }

  function updateFilter<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((current) => ({
      ...current,
      [key]: value
    }));
    setDuplicatePage(1);
    setBundlePage(1);
  }

  function resetFilters() {
    setFilters({
      moduleOwner: "",
      assetType: "",
      query: ""
    });
    setDuplicatePage(1);
    setBundlePage(1);
  }

  if (!report) {
    return (
      <div className="page">
        <main>
          <section className="empty">
            <h1>暂无 YooAsset 构建分析报告</h1>
            <p>请先完成一次正式构建，或设置 `BUNDLE_REPORT_DATA_DIR` 指向已有报告目录。</p>
            <code>.workspace/artifacts/bundle-report</code>
          </section>
        </main>
      </div>
    );
  }

  return (
    <div className="page">
      <header className="topbar">
        <div className="topbar-inner">
          <div className="brand">
            <h1>YooAsset 构建分析报告</h1>
            <p>
              {report.summary.buildTarget} / {report.summary.packageName} / {report.summary.packageVersion}
            </p>
          </div>
          <div className="build-select">
            <label htmlFor="reportSelect">历史报告</label>
            <select
              id="reportSelect"
              value={selectedReportId}
              disabled={isLoading}
              onChange={(event) => selectReport(event.target.value)}
            >
              {reportIndex.reports.map((entry) => (
                <option key={entry.id} value={entry.id}>
                  #{entry.packageVersion} · {entry.buildTarget} · {formatDate(entry.generatedAt)}
                </option>
              ))}
            </select>
          </div>
        </div>
      </header>

      <main>
        {loadError && <p className="tag danger">报告加载失败：{loadError}</p>}

        <section className="summary-grid" aria-label="概览">
          <SummaryStat label="Bundle 总数" value={formatInteger(report.summary.bundleCount)} />
          <SummaryStat label="资源总数" value={formatInteger(report.summary.assetCount)} />
          <SummaryStat
            label="重复资源"
            value={formatInteger(report.summary.duplicateAssetCount)}
            tone={report.summary.duplicateAssetCount > 0 ? "warning" : undefined}
          />
          <SummaryStat
            label="冗余总大小"
            value={formatBytes(report.summary.totalRedundantSizeBytes)}
            tone={report.summary.totalRedundantSizeBytes > 0 ? "danger" : undefined}
          />
          <SummaryStat label="压缩前总大小" value={formatBytes(report.summary.totalUncompressedSizeBytes)} />
          <SummaryStat label="压缩后总大小" value={formatBytes(report.summary.totalCompressedSizeBytes)} />
          <SummaryStat
            label={`小包 < ${formatBytes(report.thresholds.smallBundleBytes)}`}
            value={formatInteger(report.summary.smallBundleCount)}
            tone={report.summary.smallBundleCount > 0 ? "warning" : undefined}
          />
          <SummaryStat
            label={`超大包 >= ${formatBytes(report.thresholds.largeBundleBytes)}`}
            value={formatInteger(report.summary.largeBundleCount)}
            tone={report.summary.largeBundleCount > 0 ? "danger" : undefined}
          />
          <SummaryStat
            label="最大依赖深度"
            value={formatInteger(report.summary.maxDependencyDepth)}
            tone={
              report.summary.maxDependencyDepth >= report.thresholds.dependencyDepthWarningEdges
                ? "warning"
                : undefined
            }
          />
          <SummaryStat label="Unity" value={report.summary.unityVersion || "-"} />
          <SummaryStat label="YooAsset" value={report.summary.yooAssetVersion || "-"} />
          <SummaryStat label="生成时间" value={formatDate(report.summary.generatedAt)} />
        </section>

        <section className="filters" aria-label="筛选">
          <div className="filter">
            <label htmlFor="moduleFilter">模块</label>
            <select
              id="moduleFilter"
              value={filters.moduleOwner}
              onChange={(event) => updateFilter("moduleOwner", event.target.value)}
            >
              <option value="">全部模块</option>
              {modules.map((moduleOwner) => (
                <option key={moduleOwner} value={moduleOwner}>
                  {moduleOwner}
                </option>
              ))}
            </select>
          </div>
          <div className="filter">
            <label htmlFor="typeFilter">资源类型</label>
            <select
              id="typeFilter"
              value={filters.assetType}
              onChange={(event) => updateFilter("assetType", event.target.value)}
            >
              <option value="">全部类型</option>
              {assetTypes.map((assetType) => (
                <option key={assetType} value={assetType}>
                  {assetType}
                </option>
              ))}
            </select>
          </div>
          <div className="filter">
            <label htmlFor="bundleSearch">资源路径 / Bundle 名称</label>
            <input
              id="bundleSearch"
              value={filters.query}
              onChange={(event) => updateFilter("query", event.target.value)}
              placeholder="输入关键字"
            />
          </div>
          <div className="filter">
            <label>当前报告</label>
            <input value={report.summary.reportId} readOnly />
          </div>
          <button type="button" onClick={resetFilters}>
            清空筛选
          </button>
        </section>

        <ReportSection
          title="重复资源表"
          subtitle={`${filteredDuplicates.length} / ${report.duplicateAssets.length} 项`}
        >
          <DuplicateAssetsTable rows={duplicateRows.items} />
          <Pager
            page={duplicatePage}
            totalPages={duplicateRows.totalPages}
            onPrev={() => setDuplicatePage((page) => Math.max(1, page - 1))}
            onNext={() => setDuplicatePage((page) => Math.min(duplicateRows.totalPages, page + 1))}
          />
        </ReportSection>

        <ReportSection title="Bundle 大小表" subtitle={`${filteredBundles.length} / ${report.bundles.length} 个`}>
          <BundlesTable rows={bundleRows.items} />
          <Pager
            page={bundlePage}
            totalPages={bundleRows.totalPages}
            onPrev={() => setBundlePage((page) => Math.max(1, page - 1))}
            onNext={() => setBundlePage((page) => Math.min(bundleRows.totalPages, page + 1))}
          />
        </ReportSection>

        <ReportSection
          title="小包报告"
          subtitle={`小于 ${formatBytes(report.thresholds.smallBundleBytes)} 的 Bundle 数量：${report.smallBundles.length}`}
        >
          <BundlesTable rows={report.smallBundles.slice(0, pageSize)} compact />
        </ReportSection>

        <ReportSection
          title="超大包报告"
          subtitle={`超过 ${formatBytes(report.thresholds.largeBundleBytes)} 的 Bundle 数量：${report.largeBundles.length}`}
        >
          <BundlesTable rows={report.largeBundles} compact />
        </ReportSection>

        <ReportSection
          title="依赖深度报告"
          subtitle={`告警阈值：${report.thresholds.dependencyDepthWarningEdges} 条边`}
        >
          <DependencyChainsTable rows={report.dependencyChains} />
        </ReportSection>
      </main>
    </div>
  );
}

function ReportSection({
  title,
  subtitle,
  children
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  return (
    <section className="section">
      <div className="section-header">
        <h2>{title}</h2>
        <p>{subtitle}</p>
      </div>
      {children}
    </section>
  );
}

function SummaryStat({
  label,
  value,
  tone
}: {
  label: string;
  value: string;
  tone?: "warning" | "danger";
}) {
  return (
    <div className={`stat ${tone || ""}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function DuplicateAssetsTable({ rows }: { rows: DuplicateAsset[] }) {
  if (rows.length === 0) {
    return <EmptyTable text="当前筛选条件下没有重复资源。" />;
  }

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>资源路径</th>
            <th>资源类型</th>
            <th className="number">构建后大小</th>
            <th className="number">Bundle 数量</th>
            <th>所在 Bundle 列表</th>
            <th className="number">冗余总大小</th>
            <th>直接引用者</th>
            <th>模块归属</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((asset) => (
            <tr key={asset.assetPath}>
              <td className="path">{asset.assetPath}</td>
              <td>{asset.assetType || "-"}</td>
              <td className="number">{formatBytes(asset.buildSizeBytes)}</td>
              <td className="number">{asset.bundleCount}</td>
              <td>
                <div className="tags">
                  {asset.bundles.map((bundle) => (
                    <span className="tag" key={`${asset.assetPath}-${bundle.bundleName}`}>
                      {bundle.bundleName} · {formatBytes(bundle.copySizeBytes)}
                    </span>
                  ))}
                </div>
              </td>
              <td className="number">{formatBytes(asset.redundantSizeBytes)}</td>
              <td>
                <ListCell values={asset.directReferrers} />
              </td>
              <td>
                <ListCell values={asset.moduleOwners} tag />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function BundlesTable({ rows, compact = false }: { rows: BundleInfo[]; compact?: boolean }) {
  if (rows.length === 0) {
    return <EmptyTable text="当前筛选条件下没有 Bundle。" />;
  }

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Bundle 名称</th>
            <th>模块</th>
            <th className="number">压缩前大小</th>
            <th className="number">压缩后大小</th>
            <th className="number">资源数量</th>
            <th className="number">直接资源</th>
            <th className="number">依赖资源</th>
            {!compact && <th>依赖 Bundle</th>}
            {!compact && <th>引用者 Bundle</th>}
          </tr>
        </thead>
        <tbody>
          {rows.map((bundle) => (
            <tr key={bundle.bundleName}>
              <td className="path">{bundle.bundleName}</td>
              <td>{bundle.moduleOwner || "-"}</td>
              <td className="number">{formatBytes(bundle.uncompressedSizeBytes)}</td>
              <td className="number">
                {formatBytes(bundle.compressedSizeBytes)}
                {bundle.isLarge && <span className="tag danger">超大</span>}
                {bundle.isSmall && <span className="tag warn">小包</span>}
              </td>
              <td className="number">{bundle.assetCount}</td>
              <td className="number">{bundle.directAssetCount}</td>
              <td className="number">{bundle.dependencyAssetCount}</td>
              {!compact && (
                <td>
                  <ListCell values={bundle.dependBundles} />
                </td>
              )}
              {!compact && (
                <td>
                  <ListCell values={bundle.referenceBundles} />
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function DependencyChainsTable({ rows }: { rows: DependencyChain[] }) {
  if (rows.length === 0) {
    return <EmptyTable text="没有超过阈值的依赖深度链路。" />;
  }

  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>根 Bundle</th>
            <th className="number">依赖深度</th>
            <th>链路</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((chain) => (
            <tr key={`${chain.rootBundle}-${chain.chain.join(">")}`}>
              <td className="path">{chain.rootBundle}</td>
              <td className="number">{chain.depth}</td>
              <td>
                <div className="chain">
                  {chain.chain.map((bundle, index) => (
                    <span key={`${bundle}-${index}`} className="mono">
                      {index > 0 && <span className="arrow"> → </span>}
                      {bundle}
                    </span>
                  ))}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ListCell({ values, tag = false }: { values: string[]; tag?: boolean }) {
  if (!values || values.length === 0) {
    return <span className="mono">-</span>;
  }

  if (tag) {
    return (
      <div className="tags">
        {values.map((value) => (
          <span key={value} className="tag">
            {value}
          </span>
        ))}
      </div>
    );
  }

  return (
    <div className="path">
      {values.slice(0, 5).map((value) => (
        <div key={value}>{value}</div>
      ))}
      {values.length > 5 && <div>+{values.length - 5}</div>}
    </div>
  );
}

function EmptyTable({ text }: { text: string }) {
  return (
    <div className="table-wrap">
      <div className="empty">{text}</div>
    </div>
  );
}

function Pager({
  page,
  totalPages,
  onPrev,
  onNext
}: {
  page: number;
  totalPages: number;
  onPrev: () => void;
  onNext: () => void;
}) {
  if (totalPages <= 1) {
    return null;
  }

  return (
    <div className="pager">
      <button type="button" disabled={page <= 1} onClick={onPrev}>
        上一页
      </button>
      <span>
        {page} / {totalPages}
      </span>
      <button type="button" disabled={page >= totalPages} onClick={onNext}>
        下一页
      </button>
    </div>
  );
}

function paginate<T>(items: T[], page: number) {
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const currentPage = Math.min(Math.max(1, page), totalPages);
  const start = (currentPage - 1) * pageSize;
  return {
    items: items.slice(start, start + pageSize),
    totalPages
  };
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  return `${value >= 10 || unitIndex === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unitIndex]}`;
}

function formatInteger(value: number): string {
  return new Intl.NumberFormat("zh-CN").format(value || 0);
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value || "-";
  }

  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}

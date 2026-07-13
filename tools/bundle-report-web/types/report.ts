export type BundleReportIndex = {
  latestReportId: string;
  reports: BundleReportIndexEntry[];
};

export type BundleReportIndexEntry = {
  id: string;
  buildTarget: string;
  packageName: string;
  packageVersion: string;
  generatedAt: string;
  reportPath: string;
  bundleCount: number;
  duplicateAssetCount: number;
  smallBundleCount: number;
  largeBundleCount: number;
  maxDependencyDepth: number;
};

export type BundleReport = {
  schemaVersion: string;
  summary: BundleReportSummary;
  thresholds: BundleReportThresholds;
  duplicateAssets: DuplicateAsset[];
  bundles: BundleInfo[];
  smallBundles: BundleInfo[];
  largeBundles: BundleInfo[];
  dependencyChains: DependencyChain[];
};

export type BundleReportSummary = {
  reportId: string;
  generatedAt: string;
  buildTarget: string;
  packageName: string;
  packageVersion: string;
  unityVersion: string;
  yooAssetVersion: string;
  planPath: string;
  yooReportPath: string;
  outputPath: string;
  bundleCount: number;
  assetCount: number;
  duplicateAssetCount: number;
  smallBundleCount: number;
  largeBundleCount: number;
  maxDependencyDepth: number;
  totalUncompressedSizeBytes: number;
  totalCompressedSizeBytes: number;
  totalRedundantSizeBytes: number;
};

export type BundleReportThresholds = {
  smallBundleBytes: number;
  largeBundleBytes: number;
  dependencyDepthWarningEdges: number;
};

export type DuplicateAsset = {
  assetPath: string;
  assetType: string;
  buildSizeBytes: number;
  bundleCount: number;
  bundles: DuplicateAssetBundleCopy[];
  redundantSizeBytes: number;
  directReferrers: string[];
  moduleOwners: string[];
};

export type DuplicateAssetBundleCopy = {
  bundleName: string;
  copySizeBytes: number;
};

export type BundleInfo = {
  bundleName: string;
  fileName: string;
  uncompressedSizeBytes: number;
  compressedSizeBytes: number;
  assetCount: number;
  directAssetCount: number;
  dependencyAssetCount: number;
  dependBundles: string[];
  referenceBundles: string[];
  moduleOwner: string;
  isSmall: boolean;
  isLarge: boolean;
};

export type DependencyChain = {
  rootBundle: string;
  depth: number;
  chain: string[];
};

import ReportDashboard from "@/components/report-dashboard";
import { getBasePath, getLatestReport, getReportIndex } from "@/lib/reports";

export const dynamic = "force-dynamic";

export default async function Home() {
  const [index, latestReport] = await Promise.all([getReportIndex(), getLatestReport()]);

  return (
    <ReportDashboard
      basePath={getBasePath()}
      initialIndex={index}
      initialReport={latestReport}
    />
  );
}

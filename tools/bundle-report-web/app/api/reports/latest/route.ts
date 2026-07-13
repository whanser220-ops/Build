import { NextResponse } from "next/server";
import { getLatestReport } from "@/lib/reports";

export const dynamic = "force-dynamic";

export async function GET() {
  const report = await getLatestReport();
  if (!report) {
    return NextResponse.json({ error: "No bundle report found." }, { status: 404 });
  }

  return NextResponse.json(report);
}

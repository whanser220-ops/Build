import { NextResponse } from "next/server";
import { getReportById } from "@/lib/reports";

export const dynamic = "force-dynamic";

type RouteContext = {
  params: {
    id?: string[];
  };
};

export async function GET(_request: Request, context: RouteContext) {
  const id = context.params.id?.join("/") || "";
  const report = await getReportById(id);
  if (!report) {
    return NextResponse.json({ error: "Bundle report not found." }, { status: 404 });
  }

  return NextResponse.json(report);
}

import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Unity6 YooAsset 构建分析报告",
  description: "Unity6 YooAsset bundle 构建体积、重复资源和依赖深度报告"
};

export default function RootLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="zh-CN">
      <body>{children}</body>
    </html>
  );
}

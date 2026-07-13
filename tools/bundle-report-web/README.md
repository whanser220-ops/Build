# YooAsset 构建分析报告网页

这是 Unity6 正式构建产物 `bundle_report.json` 的 Next.js 查看器。

## 本地运行

```powershell
cd tools/bundle-report-web
npm install
$env:BUNDLE_REPORT_DATA_DIR="C:\unity\Unity6\.workspace\artifacts\bundle-report"
npm run dev
```

默认访问路径是 `http://localhost:3100/bundle-report`。如果只想看 fixture，可以把 `test-data/bundle_report.fixture.json` 复制到一个临时目录的任意子目录并命名为 `bundle_report.json`，再把 `BUNDLE_REPORT_DATA_DIR` 指向该目录。

## 环境变量

- `BUNDLE_REPORT_DATA_DIR`：报告根目录，线上为 `/var/www/unity6-bundle-report/data`。
- `NEXT_PUBLIC_BASE_PATH`：Next.js basePath，默认 `/bundle-report`。
- `PORT`：PM2 启动端口，默认部署脚本使用 `3100`。

## API

- `GET /api/reports`：历史报告索引。
- `GET /api/reports/latest`：最新报告。
- `GET /api/reports/[id]`：指定报告。

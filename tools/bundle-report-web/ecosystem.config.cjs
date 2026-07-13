module.exports = {
  apps: [
    {
      name: process.env.PM2_APP_NAME || "unity6-bundle-report",
      script: "server.js",
      instances: 1,
      exec_mode: "fork",
      env: {
        NODE_ENV: "production",
        PORT: process.env.PORT || "3100",
        BUNDLE_REPORT_DATA_DIR:
          process.env.BUNDLE_REPORT_DATA_DIR || "/var/www/unity6-bundle-report/data",
        NEXT_PUBLIC_BASE_PATH: process.env.NEXT_PUBLIC_BASE_PATH || "/bundle-report"
      }
    }
  ]
};

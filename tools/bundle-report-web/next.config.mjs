const basePath = process.env.NEXT_PUBLIC_BASE_PATH || "/bundle-report";

/** @type {import("next").NextConfig} */
const nextConfig = {
  basePath,
  output: "standalone",
  poweredByHeader: false,
  typescript: {
    ignoreBuildErrors: false
  }
};

export default nextConfig;

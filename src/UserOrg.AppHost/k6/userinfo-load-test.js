import http from "k6/http";
import { check, sleep } from "k6";
import { Rate, Trend } from "k6/metrics";

// ── Custom metrics ────────────────────────────────────────────────────────────
const errorRate        = new Rate("errors");
const userinfoDuration = new Trend("userinfo_duration",          true);
const extendedDuration = new Trend("userinfo_extended_duration", true);

// ── Test options ──────────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    bulk_userinfo: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "20s", target: 10 },  // ramp up to 10 VUs
        { duration: "60s", target: 10 },  // sustain — bulk concurrent phase
        { duration: "10s", target:  0 },  // ramp down
      ],
    },
  },
  thresholds: {
    http_req_duration:          ["p(95)<500"],
    userinfo_duration:          ["p(95)<500"],
    userinfo_extended_duration: ["p(95)<800"],  // Extended hydrates ProjectResourceDetailModel — heavier on cache miss
    errors:                     ["rate<0.05"],
  },
  // Dev ASP.NET Core certificates are self-signed — k6 Docker container has no trust store for them
  insecureSkipTLSVerify: true,
};

// ── Environment ───────────────────────────────────────────────────────────────
// Injected by Aspire via WithReference(gateway): resolves to the gateway HTTPS endpoint
const GATEWAY_BASE = __ENV["services__gateway__https__0"];

// Injected by Aspire via WithEnvironment callback reading from ITokenStore.
// Workflow: run 'GetAccessToken' (or gettoken-test*) on the gateway resource in the Aspire dashboard,
// then restart this k6 resource so the token is picked up.
const TOKEN = __ENV["BEARER_TOKEN"];

if (!TOKEN) {
  throw new Error(
    "BEARER_TOKEN env var is empty.\n" +
    "Run the 'GetAccessToken' command on the gateway resource in the Aspire dashboard first,\n" +
    "then restart the k6 resource."
  );
}

// ── Default VU function ───────────────────────────────────────────────────────
// All VUs share one token → one cache key {sub}_{permissionsVersion}.
// First VU iteration: cache miss (DB lookup + HybridCache populate, ~100–300 ms).
// All subsequent iterations: cache hit (~5–30 ms). Intentionally tests bulk cache throughput.
export default function () {
  const headers = { Authorization: `Bearer ${TOKEN}` };

  // ── GET /api/UserInfo ─────────────────────────────────────────────────────
  const uiRes = http.get(`${GATEWAY_BASE}/api/UserInfo`, {
    headers,
    tags: { name: "UserInfo" },
  });
  userinfoDuration.add(uiRes.timings.duration);
  const uiOk = check(uiRes, {
    "UserInfo status 200": (r) => r.status === 200,
    "UserInfo has id":     (r) => r.json("Id") !== undefined,
  });
  errorRate.add(!uiOk);

  sleep(0.2);

  // ── GET /api/UserInfo/Extended ────────────────────────────────────────────
  const exRes = http.get(`${GATEWAY_BASE}/api/UserInfo/Extended`, {
    headers,
    tags: { name: "UserInfo_Extended" },
  });
  extendedDuration.add(exRes.timings.duration);
  const exOk = check(exRes, {
    "UserInfo/Extended status 200":      (r) => r.status === 200,
    "UserInfo/Extended has systemRoles": (r) => Array.isArray(r.json("SystemRoles")),
  });
  errorRate.add(!exOk);

  sleep(0.3);
}

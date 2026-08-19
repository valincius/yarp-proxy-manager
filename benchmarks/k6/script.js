// k6 load script. Usage (see run.ps1):
//   k6 run -e TARGET=http://nginx/hello -e VUS=50 --vus 50 --duration 30s /scripts/script.js
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: __ENV.VUS ? Number(__ENV.VUS) : 50,
  duration: __ENV.DURATION || '30s',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'max'],
};

export default function () {
  const target = __ENV.TARGET || 'http://nginx/hello';
  const params = __ENV.HOST ? { headers: { Host: __ENV.HOST } } : {};
  const res = http.get(target, params);
  check(res, { 'status 200': (r) => r.status === 200 });
}

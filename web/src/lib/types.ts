export interface ProxyHost {
  id: string;
  name: string;
  domainNames: string[];
  enabled: boolean;
  scheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  webSocketsEnabled: boolean;
  blockCommonExploits: boolean;
  forceHttps: boolean;
  http2Support: boolean;
  certificateId: string | null;
  accessListId: string | null;
  requestHeaders: ProxyHeader[];
  responseHeaders: ProxyHeader[];
  locations: ProxyLocation[];
  destinations: ProxyDestination[];
  loadBalancingPolicy: string | null;
  healthCheckEnabled: boolean;
  healthCheckPath: string | null;
  healthCheckIntervalSeconds: number;
  createdAt: string;
  updatedAt: string;
}

export interface ProxyHeader {
  id: string;
  proxyHostId: string;
  target: 'Request' | 'Response';
  action: 'Set' | 'Append' | 'Remove';
  name: string;
  value: string;
}

export interface ProxyLocation {
  id: string;
  proxyHostId: string;
  pathPrefix: string;
  stripPrefix: boolean;
  scheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  order: number;
}

export interface ProxyDestination {
  id: string;
  proxyHostId: string;
  forwardHost: string;
  forwardPort: number;
}

export interface ProxyHeaderInput {
  target: 'Request' | 'Response';
  action: 'Set' | 'Append' | 'Remove';
  name: string;
  value: string;
}

export interface ProxyLocationInput {
  pathPrefix: string;
  stripPrefix: boolean;
  scheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  order: number;
}

export interface ProxyDestinationInput {
  forwardHost: string;
  forwardPort: number;
}

export interface ProxyHostInput {
  name: string;
  domainNames: string[];
  enabled: boolean;
  scheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  webSocketsEnabled: boolean;
  blockCommonExploits: boolean;
  forceHttps: boolean;
  http2Support: boolean;
  certificateId: string | null;
  accessListId: string | null;
  requestHeaders: ProxyHeaderInput[];
  responseHeaders: ProxyHeaderInput[];
  locations: ProxyLocationInput[];
  destinations: ProxyDestinationInput[];
  loadBalancingPolicy: string | null;
  healthCheckEnabled: boolean;
  healthCheckPath: string | null;
  healthCheckIntervalSeconds: number;
}

export interface Session {
  authenticated: true;
  email: string;
  displayName: string;
  roles: string[];
}

export interface CertificateDto {
  id: string;
  name: string;
  domains: string[];
  provider: 'Manual' | 'Acme';
  status: 'Pending' | 'Issued' | 'Failed' | 'Revoked';
  notBefore: string | null;
  notAfter: string | null;
  challengeType: 'Http01' | 'Dns01' | null;
  dnsCredentialId: string | null;
  lastRenewalAttempt: string | null;
  lastRenewalError: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface DnsCredentialDto {
  id: string;
  name: string;
  provider: string;
  createdAt: string;
}

export interface AcmeSettings {
  email: string;
  directoryUrl: string;
  staging: boolean;
}

export interface RedirectHost {
  id: string;
  name: string;
  domainNames: string[];
  enabled: boolean;
  statusCode: 301 | 302;
  preservePath: boolean;
  forwardScheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  certificateId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface RedirectHostInput {
  name: string;
  domainNames: string[];
  enabled: boolean;
  statusCode: 301 | 302;
  preservePath: boolean;
  forwardScheme: 'http' | 'https';
  forwardHost: string;
  forwardPort: number;
  certificateId: string | null;
}

export interface AccessListRule {
  id: string;
  accessListId: string;
  action: 'Allow' | 'Deny';
  pattern: string;
}

export interface AccessList {
  id: string;
  name: string;
  satisfyAny: boolean;
  rules: AccessListRule[];
  createdAt: string;
  updatedAt: string;
}

export interface AccessListInput {
  name: string;
  satisfyAny: boolean;
  rules: { action: 'Allow' | 'Deny'; pattern: string }[];
}

export interface AuditLogDto {
  id: string;
  timestamp: string;
  userId: string | null;
  entityType: string;
  entityId: string | null;
  action: string;
  details: string;
}

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  lockoutEnd: string | null;
}

export interface StreamInput {
  name: string;
  enabled: boolean;
  protocol: 'Tcp' | 'Udp';
  listenPort: number;
  forwardHost: string;
  forwardPort: number;
}

export interface NotFoundSettings {
  mode: 'Default' | 'Empty' | 'Custom';
  template: string;
}

export interface DockerSettings {
  enabled: boolean;
  host: string | null;
  network: string | null;
  lastSyncAt: string | null;
  lastError: string | null;
  managedHosts: number;
  discoveredContainers: number;
}

export interface DiagnosticsOverview {
  startedAt: string;
  totalRequests: number;
  totalFailed: number;
  trackedHosts: number;
  bufferedSamples: number;
  captureEnabled: boolean;
  captureSize: number;
  traceEndpoint: string | null;
  routes: number;
  clusters: number;
  proxyHosts: number;
  streams: {
    streamId: string;
    listening: boolean;
    activeSessions: number;
    bytesIn: number;
    bytesOut: number;
    error: string | null;
    updatedAt: string;
  }[];
  certificates: { total: number; failed: number; expiringSoon: number };
}

export interface TrafficRow {
  host: string;
  hostId: string | null;
  hostName: string | null;
  requests: number;
  failed: number;
  active: number;
  bytesIn: number;
  bytesOut: number;
  averageMs: number;
  p50Ms: number;
  p95Ms: number;
  p99Ms: number;
  class2xx: number;
  class3xx: number;
  class4xx: number;
  class5xx: number;
  classOther: number;
  lastError: string | null;
  firstSeen: string;
  lastSeen: string;
}

export interface RecentRequest {
  timestamp: string;
  host: string;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  bytesIn: number;
  bytesOut: number;
  clientIp: string | null;
  error: string | null;
  requestBody: string | null;
  responseBody: string | null;
}

export interface DiagnosticsSettings {
  captureEnabled: boolean;
  captureSize: number;
}

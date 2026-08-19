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

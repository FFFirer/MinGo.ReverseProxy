export interface RouteConfig {
  id: string;
  name: string;
  clusterId: string;
  match: RouteMatch;
  transforms?: Record<string, string>[] | null;
  enabled: boolean;
}

export interface RouteMatch {
  path: string;
  host?: string;
  headers?: Record<string, string>;
}

// ── Transform Schema（从 GET /api/transforms/schemas 获取） ──

export interface TransformFieldSchema {
  key: string;
  label: string;
  type: 'text' | 'select';
  options?: string[];
  required: boolean;
  placeholder?: string;
  defaultValue?: string;
  description?: string;
}

export interface TransformSchema {
  type: string;
  displayName: string;
  category: string;
  description?: string;
  order: number;
  isList: boolean;
  fields: TransformFieldSchema[];
  defaultEntries?: Record<string, string>[];
}

export interface ClusterConfig {
  id: string;
  destinations: DestinationConfig[];
  loadBalancingPolicy: string;
  healthCheck: HealthCheckConfig;
}

export interface DestinationConfig {
  id: string;
  address: string;
  healthy: boolean;
}

export interface HealthCheckConfig {
  active: HealthCheckActiveConfig;
  passive: HealthCheckPassiveConfig;
}

export interface HealthCheckActiveConfig {
  enabled: boolean;
  interval: string;
  timeout: string;
  path: string;
}

export interface HealthCheckPassiveConfig {
  enabled: boolean;
  policy: string;
  reactivationPeriod: string;
}

export interface CertificateConfig {
  id: string;
  domainName: string;
  certificateType: string;
  createdAt: string;
  expiresAt?: string;
  subject?: string;
  issuer?: string;
  thumbprint: string;
  isValid: boolean;
}

export interface CertificateParseResult {
  domainName: string;
  subject: string;
  issuer: string;
  thumbprint: string;
  certificateType: string;
  notBefore: string;
  notAfter: string;
  isValid: boolean;
  sanNames: string[];
  existingCertificateId?: string;
  existingDomainName?: string;
}

export interface GatewayInstance {
  instanceId: string;
  name: string;
  version: string;
  address: string;
  listenerAddresses?: string[];
  status: InstanceStatus;
  isHealthy: boolean;
  lastHeartbeat: string;
  registeredAt: string;
  uptime: string;
  cpuUsage: number;
  memoryUsage: number;
  totalRequests: number;
  errorRequests: number;
  errorRate: number;
  metadata?: Record<string, string>;
}

export type InstanceStatus = 'Online' | 'Offline' | 'HeartbeatTimeout' | 'Unhealthy' | 'Starting' | 'Unknown';

export interface MetricsSummary {
  averageResponseTime: number;
  totalRequests: number;
  errorRequests: number;
  errorRate: number;
  activeConnections: number;
  cpuUsage: number;
  memoryUsage: number;
  lastUpdated: string;
}

export interface RequestMetrics {
  timestamp: string;
  count: number;
  errorCount: number;
  averageResponseTime: number;
}

export interface ServiceMetrics {
  serviceId: string;
  serviceName: string;
  totalRequests: number;
  errorRequests: number;
  errorRate: number;
  averageResponseTime: number;
  lastUpdated: string;
}

export interface ErrorMetrics {
  timestamp: string;
  errorCode: string;
  errorMessage: string;
  count: number;
  routeId?: string;
  clusterId?: string;
}

export interface AccessLog {
  id: string;
  timestamp: string;
  method: string;
  path: string;
  statusCode: number;
  durationMs: number;
  clientIp: string;
}

export interface UserInfo {
  id: string;
  email: string;
  userName: string;
  emailConfirmed: boolean;
}

// 实例配置查询
export interface InstanceConfigResponse {
  version: number;
  changeTime: string;
  routes: InstanceRouteInfo[];
  clusters: InstanceClusterInfo[];
}

export interface InstanceRouteInfo {
  routeId: string;
  clusterId: string;
  match?: {
    path?: string;
    hosts?: string[];
  };
  enabled?: boolean;
}

export interface InstanceClusterInfo {
  clusterId: string;
  loadBalancingPolicy: string;
  destinations: InstanceDestinationInfo[];
}

export interface InstanceDestinationInfo {
  id: string;
  address: string;
  healthy: boolean;
}

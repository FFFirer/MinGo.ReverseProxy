export interface RouteConfig {
  id: string;
  name: string;
  clusterId: string;
  match: RouteMatch;
  transforms?: RouteTransforms;
  enabled: boolean;
}

export interface RouteMatch {
  path: string;
  host?: string;
  headers?: Record<string, string>;
}

export interface RouteTransforms {
  pathPattern?: Record<string, string>;
  pathPrefix?: Record<string, string>;
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

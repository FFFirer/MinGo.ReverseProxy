export interface RouteConfig {
  id: string;
  name: string;
  clusterId: string;
  /** 接入通道：'local' 或 Cloudflare 隧道 ID */
  connectionId?: string;
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

// ── Portainer 多实例管理 ──

export interface PortainerInstance {
  id: string;
  name: string;
  enabled: boolean;
  baseUrl: string;
  apiKey: string;
  timeoutSeconds: number;
  cacheSeconds: number;
  hostAddress?: string | null;
  dataPlaneService: string;
  selfStackNames: string[];
  selfImagePatterns: string[];
  extraExcludedStacks: string[];
  createdAt: string;
  updatedAt: string;
}

export interface PortainerSelfIdentity {
  detected: boolean;
  source: 'config' | 'hostname' | 'image' | 'none' | string;
  stacks: string[];
  containerCount: number;
}

export interface PortainerStatus {
  instanceId: string;
  enabled: boolean;
  baseUrl?: string | null;
  reachable: boolean;
  version?: string | null;
  endpointCount?: number | null;
  apiKeyConfigured: boolean;
  selfDetection: PortainerSelfIdentity;
  error?: string | null;
}

export interface PortainerDiscovery {
  instanceId: string;
  connected: boolean;
  version?: string | null;
  self: PortainerSelfIdentity;
  endpoints: PortainerEndpoint[];
  excluded: PortainerExcludedContainer[];
}

export interface PortainerEndpoint {
  id: number;
  name: string;
  engine: string;
  status: string;
  error?: string | null;
  networks: PortainerNetwork[];
}

export interface PortainerNetwork {
  name: string;
  driver?: string | null;
  isDefaultBridge: boolean;
  dataPlaneAttached: boolean;
  supportsContainerName: boolean;
  groups: PortainerGroup[];
}

export interface PortainerGroup {
  name: string;
  source: 'compose' | 'standalone' | string;
  containers: PortainerContainer[];
}

export interface PortainerContainer {
  id: string;
  name: string;
  image: string;
  service?: string | null;
  ports: PortainerPort[];
}

export interface PortainerPort {
  private: number;
  public?: number | null;
  bindIp?: string | null;
  protocol: string;
  addresses: PortainerAddressCandidates;
}

export interface PortainerAddressCandidates {
  containerName?: string | null;
  host?: string | null;
}

export interface PortainerExcludedContainer {
  name: string;
  reason: string;
  stack?: string | null;
  endpoint?: string | null;
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
  /** 分位数与总同时序同口径（nearest-rank），平均值会稀释尾延迟，看板应以分位数为主 */
  p50ResponseTime: number;
  p95ResponseTime: number;
  p99ResponseTime: number;
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
  p95ResponseTime: number;
  p99ResponseTime: number;
}

export interface ServiceMetrics {
  serviceId: string;
  serviceName: string;
  clusterId?: string;
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
  ttfbMs: number;
  downloadMs: number;
  clientIp: string;
  /** 访问域名（Host 头），多域名网关区分流量来源 */
  host?: string;
  instanceId?: string;
  routeId?: string;
  /** 后端未上报时为 undefined（老数据面）；不要用 'none' 判断，那是采集侧的哨兵 */
  clusterId?: string;
  destinationId?: string;
  /** 链路关联键：在 OTLP / Aspire 后端按此 id 定位该请求的链路 */
  traceId?: string;
  spanId?: string;
}

export interface UserInfo {
  id: string;
  email: string;
  userName: string;
  emailConfirmed: boolean;
}

// ── Cloudflare Tunnels 接入通道 ──

export interface CloudflareStatus {
  configured: boolean;
  accountId?: string | null;
  accountName?: string | null;
  zonesCount?: number | null;
  error?: string | null;
}

export interface CloudflareSettings {
  /** 脱敏 Token；保存时传空或含 *** 表示不修改 */
  apiToken: string;
  accountId: string;
  timeoutSeconds: number;
  configured: boolean;
  updatedAt?: string | null;
}

export interface CloudflareZone {
  id: string;
  name: string;
  status: string;
}

export interface CloudflareTunnel {
  id: string;
  name: string;
  status?: string | null;
  configSrc?: string | null;
  createdAt?: string | null;
  connsActiveAt?: string | null;
  connsInactiveAt?: string | null;
}

export interface TunnelConnection {
  id: string;
  coloName?: string | null;
  openedAt?: string | null;
  originIp?: string | null;
  clientVersion?: string | null;
}

export interface IngressRule {
  hostname: string;
  service: string;
  path?: string | null;
}

export interface TunnelIngressBinding {
  routeId: string;
  routeName: string;
  host: string;
  enabled: boolean;
}

export interface TunnelIngressView {
  tunnel: CloudflareTunnel;
  rules: IngressRule[];
  localBindings: TunnelIngressBinding[];
  missingHosts: string[];
  unmanagedHosts: string[];
}

export interface ReconcileReport {
  tunnelId: string;
  ingressUpdated: boolean;
  dnsCreated: string[];
  dnsDeleted: string[];
  dnsKept: string[];
  conflicts: string[];
}

export interface ConnectionSummary {
  id: string;
  name: string;
  kind: 'local' | 'cloudflare-tunnel' | string;
  status?: string | null;
}

export interface RoutePublishInfo {
  connectionId: string;
  published: boolean;
  error?: string | null;
  dnsCreated: string[];
  dnsDeleted: string[];
  conflicts: string[];
}

export interface RouteSaveResponse {
  route: RouteConfig;
  publish?: RoutePublishInfo | null;
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

// ── 网络拓扑（GET /api/topology） ──

export type TopologyNodeKind = 'connection' | 'route' | 'cluster' | 'destination';
export type TopologyNodeStatus = 'ok' | 'warning' | 'danger' | 'disabled';
export type TopologySeverity = 'info' | 'warning' | 'danger';

/** 详情面板的一条键值，后端按展示顺序返回数组 */
export interface TopologyDetailItem {
  label: string;
  value: string | null;
}

export interface TopologyNode {
  /** conn:{id} / route:{id} / cluster:{id} / dest:{clusterId}:{destId} */
  id: string;
  kind: TopologyNodeKind;
  label: string;
  subLabel?: string | null;
  status: TopologyNodeStatus;
  details: TopologyDetailItem[];
  issueCodes: string[];
}

/** 有向边，方向即流量方向 */
export interface TopologyEdge {
  source: string;
  target: string;
  kind: string;
}

/** 配置一致性问题，nodeIds 为需要着色的节点 */
export interface TopologyIssue {
  code: string;
  severity: TopologySeverity;
  message: string;
  nodeIds: string[];
}

export interface TopologySummary {
  connectionCount: number;
  routeCount: number;
  enabledRouteCount: number;
  clusterCount: number;
  destinationCount: number;
  unhealthyDestinationCount: number;
  issueCount: number;
}

export interface TopologyResponse {
  generatedAt: string;
  summary: TopologySummary;
  nodes: TopologyNode[];
  edges: TopologyEdge[];
  issues: TopologyIssue[];
}

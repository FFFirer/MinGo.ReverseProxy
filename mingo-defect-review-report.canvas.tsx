import {
  Banner,
  Callout,
  Divider,
  Grid,
  H1,
  H2,
  H3,
  MetricsGrid,
  ReportSection,
  ReportShell,
  Stack,
  Stat,
  Table,
  Text,
} from "qoder/canvas";

const headerMetrics = [
  { label: "Defects Fixed", value: "15", tone: "success" as const },
  { label: "Build Errors", value: "0", tone: "success" as const },
  { label: "Tests Passed", value: "16 / 16", tone: "success" as const },
  { label: "Files Modified", value: "26", tone: "neutral" as const },
];

const securityFixes = [
  ["1.1", "Management API Authorization", "Added [Authorize] to 8 controllers", "ApiManagementController, CertificatesController, InstancesController, AuthController, TelemetryController, MonitoringController, LogsController, TransformsController"],
  ["1.2", "Registration + Password Policy", "Protected Register endpoint; 8+ chars, upper/lower/digit/special", "AuthController.cs, Program.cs, DbInitializer.cs"],
  ["1.3", "Certificate Password Encryption", "IDataProtectionProvider encrypt/decrypt for cert passwords", "ApiDbService.cs"],
];

const concurrencyFixes = [
  ["2.1", "TelemetryStore Thread Safety", "List<MetricPoint> \u2192 ConcurrentQueue<MetricPoint>; thread-safe AddMetric", "TelemetryStore.cs, MonitoringService.cs, HeartbeatReporter.cs, TelemetryController.cs"],
  ["2.2", "DataPlaneConfigProvider Duplicate Registration", "Removed manual DI registration, kept extension method only", "DataPlane/Program.cs"],
  ["2.3", "ConfigSyncService Async Anti-pattern", "Replaced ContinueWith().Unwrap() with standard async/await", "ConfigSyncService.cs"],
  ["2.4", "GetStatus Logic Error", "Fixed condition order: >120s Offline before >30s HeartbeatTimeout", "DataPlaneConnectionManager.cs"],
  ["2.5", "ConfigReplicationService Redundant Query", "Removed duplicate GetCertificatesAsync() call", "ConfigReplicationService.cs"],
  ["2.6", "Monotonic Version Counter", "Interlocked.CompareExchange-based counter prevents duplicate versions", "ConfigReplicationService.cs"],
];

const qualityFixes = [
  ["3.1", "Deleted Orphaned Code", "Removed unused ProxyDbContext.cs and ProxyEntityConfigurations.cs", "ProxyDbContext.cs, ProxyEntityConfigurations.cs (deleted)"],
  ["3.2", "TelemetryStore Encapsulation", "Public fields \u2192 properties with getters", "TelemetryStore.cs"],
  ["3.3", "Silent Catch Logging", "catch {} \u2192 catch(JsonException) with LogWarning", "DataPlaneConfigProvider.cs, ProxyConfigEntities.cs"],
  ["3.4", "API Input Validation", "[Required] and [StringLength] DataAnnotation on models", "GatewayConfig.cs"],
  ["3.5", "Fixed Test Execution", "Added test project to .slnx; all 16 tests discovered and passing", "MinGo.ReverseProxy.slnx"],
];

const changedFiles = [
  ["src/MinGo.ControlPlane.Api/Controllers/", "8 files", "Added [Authorize] to all management controllers"],
  ["src/MinGo.ControlPlane.Api/Program.cs", "1 file", "Password policy: 8 chars + complexity requirements"],
  ["src/MinGo.ControlPlane.Api/Data/DbInitializer.cs", "1 file", "Default password updated to Admin@123"],
  ["src/MinGo.ControlPlane.Api/GrpcServices/", "2 files", "GetStatus logic fix + MonotonicVersionCounter + removed redundant query"],
  ["src/MinGo.Infrastructure/Data/ApiDbService.cs", "1 file", "IDataProtectionProvider for cert password encryption"],
  ["src/MinGo.Core/Services/TelemetryStore.cs", "1 file", "ConcurrentQueue + properties + thread-safe AddMetric"],
  ["src/MinGo.Core/Models/GatewayConfig.cs", "1 file", "DataAnnotation validation attributes"],
  ["src/MinGo.Core/Entities/ProxyConfigEntities.cs", "1 file", "catch(JsonException) with logging"],
  ["src/MinGo.Shared/Models/ProxyConfigEntities.cs", "1 file", "catch(JsonException) with logging"],
  ["src/MinGo.Shared/Data/", "2 files deleted", "Removed orphaned ProxyDbContext + ProxyEntityConfigurations"],
  ["src/MinGo.DataPlane/", "4 files", "Async fix, DI dedup, ConcurrentQueue adaptation, logging"],
  ["src/MinGo.Application/Services/MonitoringService.cs", "1 file", "Adapted ConcurrentQueue API (.ToList())"],
  ["MinGo.ReverseProxy.slnx", "1 file", "Added test project to solution"],
];

export default function MinGoDefectReviewReport() {
  return (
    <ReportShell width="wide" ariaLabel="MinGo API Gateway Defect Review Report">
      <Stack gap="section">
        <Stack gap="component">
          <H1>MinGo API Gateway &#8212; Defect Review &amp; Improvement Report</H1>
          <Text tone="secondary">
            ASP.NET Core 10.0 &middot; YARP &middot; gRPC &middot; EF Core (SQLite) &middot; ASP.NET Identity &middot; Serilog
          </Text>
          <MetricsGrid variant="header" columns={4} items={headerMetrics} />
        </Stack>

        <Banner tone="success" title="All 15 planned fixes completed and verified">
          Build: 0 errors &middot; Tests: 16 passed, 0 failed, 0 skipped &middot; 14 NuGet security warnings (pre-existing third-party vulnerabilities, not introduced by this work)
        </Banner>

        <Divider />

        <ReportSection title="Batch 1: Security Vulnerability Fixes" description="3 high-priority items &#8212; authorization, password policy, certificate encryption" divided>
          <Stack gap="component">
            <Table
              headers={["ID", "Issue", "Fix", "Affected Files"]}
              rows={securityFixes}
              density="compact"
            />
            <Callout tone="info">
              <Text size="small">
                All 8 management API controllers now require authentication. The Register endpoint is protected with [Authorize] to prevent arbitrary account creation. Certificate passwords are encrypted at rest using ASP.NET Core IDataProtectionProvider.
              </Text>
            </Callout>
          </Stack>
        </ReportSection>

        <ReportSection title="Batch 2: Concurrency &amp; Logic Defect Fixes" description="6 medium-high priority items &#8212; thread safety, DI registration, async patterns, logic errors" divided>
          <Stack gap="component">
            <Table
              headers={["ID", "Issue", "Fix", "Affected Files"]}
              rows={concurrencyFixes}
              density="compact"
            />
            <Callout tone="info">
              <Text size="small">
                Key changes: TelemetryStore uses ConcurrentQueue for thread-safe metric collection. ConfigSyncService follows standard async/await pattern. GetStatus correctly checks Offline (&gt;120s) before HeartbeatTimeout (&gt;30s). Version numbers are now monotonically increasing via lock-free CAS.
              </Text>
            </Callout>
          </Stack>
        </ReportSection>

        <ReportSection title="Batch 3: Code Quality Improvements" description="5 medium priority items &#8212; dead code removal, encapsulation, logging, validation, test execution" divided>
          <Stack gap="component">
            <Table
              headers={["ID", "Issue", "Fix", "Affected Files"]}
              rows={qualityFixes}
              density="compact"
            />
            <Callout tone="info">
              <Text size="small">
                Orphaned ProxyDbContext and its entity configurations were deleted. TelemetryStore public fields are now read-only properties. Silent catch blocks now log warnings. RouteConfig and ClusterConfig models have DataAnnotation validation. The test project was added to the solution and all 16 xUnit tests are discovered and passing.
              </Text>
            </Callout>
          </Stack>
        </ReportSection>

        <Divider />

        <ReportSection title="Changed Files Summary" description="26 files modified or deleted across 5 projects" divided>
          <Table
            headers={["Path", "Count", "Description"]}
            rows={changedFiles}
            density="compact"
          />
        </ReportSection>

        <Divider />

        <ReportSection title="Verification Evidence" divided>
          <Grid columns={2} gap="component">
            <Stack gap="component">
              <H3>Build Result</H3>
              <Stat value="0" label="Compilation Errors" tone="success" />
              <Stat value="14" label="NuGet Warnings (pre-existing)" tone="warning" />
              <Text size="small" tone="secondary">
                All warnings are NU1903 &#8212; known vulnerabilities in SQLitePCLRaw.lib.e_sqlite3 2.1.11 and System.Security.Cryptography.Xml 10.0.8. None introduced by this work.
              </Text>
            </Stack>
            <Stack gap="component">
              <H3>Test Result</H3>
              <Stat value="16" label="Tests Passed" tone="success" />
              <Stat value="0" label="Tests Failed" tone="success" />
              <Text size="small" tone="secondary">
                All 16 xUnit test cases in MinGo.DataPlane.Tests pass successfully. Tests cover DataPlaneConfigProvider configuration validation, snapshot application, and version management.
              </Text>
            </Stack>
          </Grid>
        </ReportSection>

        <Divider />

        <Stack gap="component">
          <H2>Outcome</H2>
          <Text>
            All 15 planned defect fixes from the review have been successfully implemented and verified. The project compiles with zero errors and all 16 unit tests pass. Security posture is significantly improved with authorization on all management endpoints, stronger password policy, and encrypted certificate storage. Concurrency and logic defects have been resolved, and code quality has been enhanced through dead code removal, better encapsulation, logging, and input validation.
          </Text>
        </Stack>
      </Stack>
    </ReportShell>
  );
}

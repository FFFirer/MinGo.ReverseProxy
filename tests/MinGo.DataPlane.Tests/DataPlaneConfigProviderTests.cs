using Microsoft.Extensions.Logging;
using MinGo.DataPlane.ConfigSync;
using MinGo.DataPlane.Grpc;
using Moq;

namespace MinGo.DataPlane.Tests;

public class DataPlaneConfigProviderTests
{
    private readonly DataPlaneConfigProvider _provider;

    public DataPlaneConfigProviderTests()
    {
        var logger = new Mock<ILogger<DataPlaneConfigProvider>>();
        _provider = new DataPlaneConfigProvider(logger.Object);
    }

    private static ConfigSnapshot CreateValidSnapshot(int version = 1) => new()
    {
        Version = version,
        UpdateType = UpdateType.FullSync,
        Routes =
        {
            new RouteConfig
            {
                Id = "route-1",
                Name = "Route 1",
                ClusterId = "cluster-1",
                MatchPath = "/api/{**catchall}",
                Enabled = true
            }
        },
        Clusters =
        {
            new ClusterConfig
            {
                Id = "cluster-1",
                Name = "Cluster 1",
                LoadBalancingPolicy = "RoundRobin",
                Destinations =
                {
                    new DestinationConfig
                    {
                        Id = "dest-1",
                        Address = "https://localhost:5001",
                        Healthy = true
                    }
                }
            }
        }
    };

    // === Task 5.1: Valid config passes validation and is applied ===

    [Fact]
    public void ApplyConfig_ValidSnapshot_Succeeds()
    {
        var snapshot = CreateValidSnapshot();

        _provider.ApplyConfig(snapshot);

        var config = _provider.GetConfig();
        Assert.Single(config.Routes);
        Assert.Single(config.Clusters);
        Assert.Equal(1, _provider.CurrentVersion);
        Assert.True(_provider.LastApplySucceeded);
    }

    // === Task 5.2: Empty RouteId is rejected, old config kept ===

    [Fact]
    public void ApplyConfig_EmptyRouteId_Rejected()
    {
        // First apply valid config
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        // Now try invalid config
        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Routes[0].Id = "";

        _provider.ApplyConfig(snapshot);

        // Old config should be kept
        Assert.Equal(1, _provider.CurrentVersion);
        var config = _provider.GetConfig();
        Assert.Equal("route-1", config.Routes[0].RouteId);
    }

    // === Task 5.3: ClusterId referencing non-existent cluster is rejected ===

    [Fact]
    public void ApplyConfig_MissingClusterReference_Rejected()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Routes[0].ClusterId = "non-existent-cluster";

        _provider.ApplyConfig(snapshot);

        Assert.Equal(1, _provider.CurrentVersion);
    }

    // === Task 5.4: Invalid Destination Address is rejected ===

    [Fact]
    public void ApplyConfig_InvalidDestinationAddress_Rejected()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Clusters[0].Destinations[0].Address = "not-a-url";

        _provider.ApplyConfig(snapshot);

        Assert.Equal(1, _provider.CurrentVersion);
    }

    // === Task 5.5: Invalid LoadBalancingPolicy is rejected ===

    [Fact]
    public void ApplyConfig_InvalidLoadBalancingPolicy_Rejected()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Clusters[0].LoadBalancingPolicy = "InvalidPolicy";

        _provider.ApplyConfig(snapshot);

        Assert.Equal(1, _provider.CurrentVersion);
    }

    // === Task 5.6: Duplicate RouteId is rejected ===

    [Fact]
    public void ApplyConfig_DuplicateRouteId_Rejected()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Routes.Add(new RouteConfig
        {
            Id = "route-1", // duplicate
            Name = "Route 1 Duplicate",
            ClusterId = "cluster-1",
            MatchPath = "/duplicate",
            Enabled = true
        });

        _provider.ApplyConfig(snapshot);

        Assert.Equal(1, _provider.CurrentVersion);
    }

    // === Task 5.7: Older version snapshot is discarded ===

    [Fact]
    public void ApplyConfig_OlderVersion_Discarded()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 5));

        _provider.ApplyConfig(CreateValidSnapshot(version: 3)); // older

        Assert.Equal(5, _provider.CurrentVersion);
        var config = _provider.GetConfig();
        Assert.Single(config.Routes);
    }

    // === Task 5.8: Empty ClusterId is rejected ===

    [Fact]
    public void ApplyConfig_EmptyClusterId_Rejected()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));

        var snapshot = CreateValidSnapshot(version: 2);
        snapshot.Clusters[0].Id = "";

        _provider.ApplyConfig(snapshot);

        Assert.Equal(1, _provider.CurrentVersion);
    }

    // === Additional edge case tests ===

    [Fact]
    public void ValidateSnapshot_EmptyAddress_Rejected()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Clusters[0].Destinations[0].Address = "";

        var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("invalid Address"));
    }

    [Fact]
    public void ValidateSnapshot_EmptyAddressString_Rejected()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Clusters[0].Destinations[0].Address = "";

        var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("invalid Address"));
    }

    [Fact]
    public void ValidateSnapshot_EmptyClusterIdInRoute_Rejected()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Routes[0].ClusterId = "";

        var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("empty ClusterId"));
    }

    [Fact]
    public void ValidateSnapshot_ValidConfig_Passes()
    {
        var snapshot = CreateValidSnapshot();

        var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ApplyConfig_SameVersion_Discarded()
    {
        _provider.ApplyConfig(CreateValidSnapshot(version: 1));
        _provider.ApplyConfig(CreateValidSnapshot(version: 1)); // same version

        Assert.Equal(1, _provider.CurrentVersion);
    }

    [Fact]
    public void ApplyConfig_EmptySnapshot_Succeeds()
    {
        var snapshot = new ConfigSnapshot
        {
            Version = 1,
            UpdateType = UpdateType.FullSync
        };

        _provider.ApplyConfig(snapshot);

        var config = _provider.GetConfig();
        Assert.Empty(config.Routes);
        Assert.Empty(config.Clusters);
        Assert.True(_provider.LastApplySucceeded);
    }

    [Fact]
    public void ApplyConfig_AllowsEmptyLoadBalancingPolicy()
    {
        var snapshot = CreateValidSnapshot();
        snapshot.Clusters[0].LoadBalancingPolicy = "";

        var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ApplyConfig_AcceptsAllValidPolicies()
    {
        foreach (var policy in new[] { "RoundRobin", "LeastRequests", "PowerOfTwoChoices", "FirstAlphabetical", "Random" })
        {
            var snapshot = CreateValidSnapshot(version: 1);
            snapshot.Clusters[0].LoadBalancingPolicy = policy;

            var (isValid, errors) = DataPlaneConfigProvider.ValidateSnapshot(snapshot);

            Assert.True(isValid);
            Assert.Empty(errors);
        }
    }
}

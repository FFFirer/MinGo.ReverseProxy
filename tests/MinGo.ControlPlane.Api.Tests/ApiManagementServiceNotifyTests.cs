using Microsoft.Extensions.Logging.Abstractions;
using MinGo.Application.Services;
using MinGo.Core.Entities;
using MinGo.Core.Interfaces;
using MinGo.Core.Models;
using Moq;

namespace MinGo.ControlPlane.Api.Tests;

/// <summary>路由写操作后的通知广播 + 登记证书级联删除（批量一次、广播一次）。</summary>
public class ApiManagementServiceNotifyTests
{
    private readonly Mock<IApiDbService> _db = new();
    private readonly Mock<IGatewayEventService> _events = new();

    private ApiManagementService CreateService() =>
        new(NullLogger<ApiManagementService>.Instance, _db.Object, _events.Object);

    private static RouteConfig Route() => new()
    {
        Id = "r1",
        Name = "r",
        ClusterId = "c",
        Match = new RouteMatch { Path = "/" },
    };

    [Fact]
    public async Task CreateRoute_EmitsConfigUpdateAndRoutesChanged()
    {
        _db.Setup(d => d.CreateRouteAsync(It.IsAny<RouteConfig>())).ReturnsAsync(Route());

        await CreateService().CreateRouteAsync(Route());

        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.ConfigUpdate, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.RoutesChanged, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRoute_EmitsConfigUpdateAndRoutesChanged()
    {
        _db.Setup(d => d.UpdateRouteAsync("r1", It.IsAny<RouteConfig>())).ReturnsAsync(Route());

        await CreateService().UpdateRouteAsync("r1", Route());

        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.RoutesChanged, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRoute_EmitsConfigUpdateAndRoutesChanged()
    {
        await CreateService().DeleteRouteAsync("r1");

        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.ConfigUpdate, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.RoutesChanged, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCertificatesByEnrollment_DeletedRows_BroadcastsConfigUpdateOnce()
    {
        _db.Setup(d => d.DeleteCertificatesByEnrollmentAsync("e1")).ReturnsAsync(2);

        var deleted = await CreateService().DeleteCertificatesByEnrollmentAsync("e1");

        Assert.Equal(2, deleted);
        _events.Verify(e => e.SendEventToAllInstancesAsync(
            GatewayEventType.ConfigUpdate, It.IsAny<object>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCertificatesByEnrollment_NothingDeleted_DoesNotBroadcast()
    {
        _db.Setup(d => d.DeleteCertificatesByEnrollmentAsync("e1")).ReturnsAsync(0);

        await CreateService().DeleteCertificatesByEnrollmentAsync("e1");

        _events.Verify(e => e.SendEventToAllInstancesAsync(
            It.IsAny<GatewayEventType>(), It.IsAny<object>(), It.IsAny<int>()), Times.Never);
    }
}
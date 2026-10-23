using MinGo.Core.Portainer;

namespace MinGo.Core.Tests.Portainer;

internal static class Snap
{
    public static ContainerSnapshot Container(
        string id,
        string name,
        string state = "running",
        string image = "example/app:latest",
        string? stack = null,
        string? service = null,
        string[]? networks = null,
        PortBinding[]? ports = null,
        bool portainerSystem = false)
    {
        return new ContainerSnapshot
        {
            Id = id,
            Name = name,
            State = state,
            Image = image,
            Stack = stack,
            Service = service,
            Networks = networks ?? [],
            Ports = ports ?? [],
            IsPortainerSystem = portainerSystem
        };
    }

    public static PortBinding Port(int privatePort, int? publicPort = null, string? bindIp = null) =>
        new(privatePort, publicPort, bindIp, "tcp");

    /// <summary>debian13 EP4 的真实容器名与 compose project。</summary>
    public static ContainerSnapshot StaticPages() =>
        Container(
            "6d92ee9bb7ef66f3ffed22d7adc9ad4582aeeead2c8393815b93b616f0a0c46a",
            "static-pages",
            image: "ghcr.io/fffirer/static-pages:latest",
            stack: "staticpages",
            service: "static-pages",
            networks: ["default_bridge"],
            ports: [Port(8080, 5218)]);

    public static ContainerSnapshot SelfControlPlane() =>
        Container(
            "d69dc8afd9d4aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "reverseproxy-control-plane-1",
            image: "registry.private.fffirer.top:9081/1mingo/reverseproxy-cp:latest",
            stack: "reverseproxy",
            service: "control-plane",
            networks: ["default_bridge"],
            ports: [Port(5000, 5000)]);

    public static ContainerSnapshot SelfDataPlane() =>
        Container(
            "aaaa00000000aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "reverseproxy-data-plane-1",
            image: "registry.private.fffirer.top:9081/1mingo/reverseproxy-dp:latest",
            stack: "reverseproxy",
            service: "data-plane",
            networks: ["default_bridge"],
            ports: [Port(8080, 9080)]);
}

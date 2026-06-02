# ControlPlane.Dockerfile
# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 安装 ef 工具
RUN dotnet tool install -g dotnet-ef --version 10.0.4
ENV PATH="$PATH:/root/.dotnet/tools"

# 缓存 NuGet 包
COPY Directory.Packages.props .
COPY nuget.config .
COPY src/MinGo.Core/MinGo.Core.csproj src/MinGo.Core/
COPY src/MinGo.Application/MinGo.Application.csproj src/MinGo.Application/
COPY src/MinGo.Infrastructure/MinGo.Infrastructure.csproj src/MinGo.Infrastructure/
COPY src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj src/MinGo.ControlPlane.Api/
COPY src/MinGo.ControlPlane.Api/GrpcServices/Protos/dataplane.proto src/MinGo.ControlPlane.Api/GrpcServices/Protos/
RUN dotnet restore src/MinGo.ControlPlane.Api/MinGo.ControlPlane.Api.csproj

# 构建
COPY . .
RUN dotnet publish src/MinGo.ControlPlane.Api -c Release -o /app/publish

# 生成 EF Core 迁移 Bundle
WORKDIR /src/src/MinGo.Infrastructure
RUN dotnet ef migrations bundle -c ApiDbContext --configuration Release --no-build -o /app/publish/efbundle

# 运行阶段
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000 5001
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "MinGo.ControlPlane.Api.dll"]

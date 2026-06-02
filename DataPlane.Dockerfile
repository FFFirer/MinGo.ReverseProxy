# DataPlane.Dockerfile
# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 缓存 NuGet 包
COPY Directory.Packages.props .
COPY nuget.config .
COPY src/MinGo.Core/MinGo.Core.csproj src/MinGo.Core/
COPY src/MinGo.DataPlane/MinGo.DataPlane.csproj src/MinGo.DataPlane/
COPY src/MinGo.ControlPlane.Api/GrpcServices/Protos/dataplane.proto src/MinGo.ControlPlane.Api/GrpcServices/Protos/
RUN dotnet restore src/MinGo.DataPlane/MinGo.DataPlane.csproj

# 构建
COPY . .
RUN dotnet publish src/MinGo.DataPlane -c Release -o /app/publish

# 运行阶段
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MinGo.DataPlane.dll"]

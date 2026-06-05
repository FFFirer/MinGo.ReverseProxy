## 1. Backend - Model & Entity

- [x] 1.1 Remove `Name` property from `ApiClusterEntity`
- [x] 1.2 Remove `Name` property from `ClusterConfig` model
- [x] 1.3 Remove `Name` property from frontend `ClusterConfig` type

## 2. Backend - Service

- [x] 2.1 Update `CreateClusterAsync` — use `cluster.Id` directly, stop generating GUID
- [x] 2.2 Update `UpdateClusterAsync` — remove Name update, ignore `id` from body (URL path is identifier)
- [x] 2.3 Update `MapToClusterEntity` — remove Name mapping
- [x] 2.4 Update `MapToClusterModel` — remove Name mapping
- [x] 2.5 Update sample data — use readable names as cluster IDs (e.g., "user-cluster")

## 3. Frontend - Cluster Form & Display

- [x] 3.1 Update `ClusterConfig` type — remove `name` field
- [x] 3.2 Remove `name` signal from form, use `id` directly as the cluster name input
- [x] 3.3 In edit mode, show cluster Id as read-only text
- [x] 3.4 Update submit — pass `id` as the user-entered name
- [x] 3.5 Update cluster card display — show `cluster.id` as title

## 4. Frontend - Route Form

- [x] 4.1 Update cluster dropdown — show `c.id` as label (remove `c.name || c.id`)

## 5. Cleanup

- [x] 5.1 Delete old database file to regenerate schema
- [x] 5.2 Build and verify

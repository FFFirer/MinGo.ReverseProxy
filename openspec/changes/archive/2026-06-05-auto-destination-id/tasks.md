## 1. Backend - Interface & Service changes

- [x] 1.1 Simplify `IApiManagementService.AddDestinationAsync` — remove `destinationId` parameter
- [x] 1.2 Simplify `IApiDbService.AddDestinationAsync` — remove `destinationId` parameter
- [x] 1.3 Remove `IApiManagementService.UpdateDestinationAsync` and `IApiDbService.UpdateDestinationAsync` (address change = delete + add)
- [x] 1.4 Implement `GetNextDestinationSequenceAsync` in `ApiDbService` — query max seq suffix for cluster
- [x] 1.5 Update `ApiDbService.CreateClusterAsync` — auto-generate destination IDs using `{clusterId}-{seq}`
- [x] 1.6 Update `ApiDbService.UpdateClusterAsync` — auto-generate IDs for new destinations, keep existing IDs, reset health to true
- [x] 1.7 Update `ApiManagementService` — adapt to new interface signatures

## 2. Backend - Controller & Validation

- [x] 2.1 Remove `UpdateDestination` endpoint from `ApiManagementController` (no longer needed)
- [x] 2.2 Update `AddDestination` endpoint — accept `DestinationConfig` without requiring `id` field

## 3. Frontend - Types & Cluster Form

- [x] 3.1 Update `Clusters.tsx` form — remove "目标名称" input, only keep address input per destination row
- [x] 3.2 Change form signal from `{id, address}[]` to `{_internalId?, address}[]`
- [x] 3.3 Simplify validation — remove duplicate ID check, only validate address non-empty
- [x] 3.4 Update submit logic — new destinations send without `id`, existing send with hidden `_internalId`
- [x] 3.5 Update cluster card display — show address as primary info, remove destination name display

## 4. Sample Data & Cleanup

- [x] 4.1 Update `InitializeSampleDataAsync` — use address-only destinations (no user-facing IDs)
- [x] 4.2 Delete old database file to regenerate schema (BREAKING — no data migration)

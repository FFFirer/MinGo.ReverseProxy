## 1. Create local docker-compose file

- [x] 1.1 Create `docker-compose.local.yml` with migrate service - local build, named volume, Production env, Debug log level override
- [x] 1.2 Add control-plane service - depends on migrate, named volume shared, expose 5000/5001, Production env with Debug logging
- [x] 1.3 Add data-plane service - depends on control-plane, expose 8080/8443, Production env with Debug logging, gRPC endpoint pointing to control-plane:5001
- [x] 1.4 Add `mingocp-data` named volume declaration
- [x] 1.5 Verify all services use `restart: "no"` and no registry references

## 2. Verify final file

- [x] 2.1 Review file for correctness - compare against production docker-compose.yml
- [x] 2.2 Confirm no registry/image references remain
- [x] 2.3 Confirm volume paths and environment variables match existing Dockerfile expectations

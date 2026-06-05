import { createSignal, onMount, For, Index, type Component } from "solid-js";
import { api } from "../api/client";
import { addToast } from "../store/toast";
import type {
  ClusterConfig,
  DestinationConfig,
  HealthCheckConfig,
} from "../types";
import { FaSolidPlus, FaSolidEdit, FaSolidTrash, FaSolidTimes, FaSolidChevronRight, FaSolidChevronDown } from "solid-icons/fa";

export default function ClustersPage() {
  const [clusters, setClusters] = createSignal<ClusterConfig[]>([]);
  const [loading, setLoading] = createSignal(true);
  const [editingCluster, setEditingCluster] =
    createSignal<ClusterConfig | null>(null);
  const [showModal, setShowModal] = createSignal(false);

  onMount(async () => {
    try {
      setClusters(await api.get<ClusterConfig[]>("/apimanagement/clusters"));
    } catch (err) {
      addToast("error", "加载集群列表失败");
    } finally {
      setLoading(false);
    }
  });

  const handleSave = async (cluster: ClusterConfig) => {
    try {
      if (editingCluster()) {
        await api.put(`/apimanagement/clusters/${cluster.id}`, cluster);
        addToast("success", "集群已更新");
      } else {
        await api.post("/apimanagement/clusters", cluster);
        addToast("success", "集群已创建");
      }
      setClusters(await api.get<ClusterConfig[]>("/apimanagement/clusters"));
      setShowModal(false);
    } catch (err) {
      addToast("error", `保存集群失败: ${(err as Error).message}`);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm("确定删除此集群？")) return;
    try {
      await api.delete(`/apimanagement/clusters/${id}`);
      setClusters(await api.get<ClusterConfig[]>("/apimanagement/clusters"));
      addToast("success", "集群已删除");
    } catch (err) {
      addToast("error", `删除集群失败: ${(err as Error).message}`);
    }
  };

  const allHealthy = (cluster: ClusterConfig) => {
    if (!cluster.destinations) return true;
    return cluster.destinations.every((d) => d.healthy);
  };

  return (
    <div>
      <div class="mb-6 flex items-center justify-between">
        <div>
          <h2 class="text-2xl font-bold mb-2">集群管理</h2>
          <p class="text-secondary">配置和管理后端服务集群</p>
        </div>
        <button
          class="btn btn-primary"
          onClick={() => {
            setEditingCluster(null);
            setShowModal(true);
          }}
        >
          <FaSolidPlus class="mr-2" />添加集群
        </button>
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <For each={clusters()}>
          {(cluster) => (
            <div class="card">
              <div class="flex items-center justify-between mb-4">
                <h3 class="font-semibold">{cluster.id}</h3>
                <span
                  class={`badge ${allHealthy(cluster) ? "badge-success" : "badge-warning"}`}
                >
                  {allHealthy(cluster) ? "健康" : "异常"}
                </span>
              </div>
              <div class="space-y-3">
                <For each={cluster.destinations ?? []}>
                  {(dest) => (
                    <div class="flex items-center justify-between p-3 bg-gray-50 dark:bg-dark-200 rounded-lg">
                      <p class="font-medium text-sm break-all">{dest.address}</p>
                      <span
                        class={`badge shrink-0 ml-2 ${dest.healthy ? "badge-success" : "badge-warning"}`}
                      >
                        {dest.healthy ? "在线" : "离线"}
                      </span>
                    </div>
                  )}
                </For>
              </div>
              <div class="mt-4 flex justify-between items-center">
                <div class="flex items-center gap-2">
                  <span class="text-sm text-secondary">
                    策略: {cluster.loadBalancingPolicy || "RoundRobin"}
                  </span>
                  {cluster.healthCheck?.active?.enabled && (
                    <span class="badge badge-success text-xs">健康检查</span>
                  )}
                </div>
                <div class="flex space-x-2">
                  <button
                    class="btn-text btn-text-primary"
                    onClick={() => {
                      setEditingCluster(cluster);
                      setShowModal(true);
                    }}
                  >
                    <FaSolidEdit class="mr-1" />编辑
                  </button>
                  <button
                    class="btn-text btn-text-danger"
                    onClick={() => handleDelete(cluster.id)}
                  >
                    <FaSolidTrash class="mr-1" />删除
                  </button>
                </div>
              </div>
            </div>
          )}
        </For>
        {!loading() && clusters().length === 0 && (
          <div class="col-span-2 text-center py-12 text-secondary">
            暂无集群
          </div>
        )}
        {loading() && (
          <div class="col-span-2 text-center py-12 text-secondary">
            加载中...
          </div>
        )}
      </div>

      {showModal() && (
        <ClusterFormModal
          cluster={editingCluster()}
          onSave={handleSave}
          onClose={() => setShowModal(false)}
        />
      )}
    </div>
  );
}

function ClusterFormModal(props: {
  cluster: ClusterConfig | null;
  onSave: (c: ClusterConfig) => void;
  onClose: () => void;
}) {
  const [clusterId, setClusterId] = createSignal(props.cluster?.id || "");
  const [policy, setPolicy] = createSignal(
    props.cluster?.loadBalancingPolicy || "RoundRobin",
  );
  const [destinations, setDestinations] = createSignal<
    { _internalId?: string; address: string }[]
  >(
    props.cluster?.destinations
      ? props.cluster.destinations.map((d) => ({
          _internalId: d.id,
          address: d.address,
        }))
      : [{ address: "" }],
  );
  const [activeEnabled, setActiveEnabled] = createSignal(
    props.cluster?.healthCheck?.active?.enabled ?? false,
  );
  const [activeInterval, setActiveInterval] = createSignal(
    props.cluster?.healthCheck?.active?.interval || "00:00:10",
  );
  const [activeTimeout, setActiveTimeout] = createSignal(
    props.cluster?.healthCheck?.active?.timeout || "00:00:05",
  );
  const [activePath, setActivePath] = createSignal(
    props.cluster?.healthCheck?.active?.path || "/health",
  );
  const [passiveEnabled, setPassiveEnabled] = createSignal(
    props.cluster?.healthCheck?.passive?.enabled ?? false,
  );
  const [passivePolicy, setPassivePolicy] = createSignal(
    props.cluster?.healthCheck?.passive?.policy || "FailureRate",
  );
  const [passiveReactivation, setPassiveReactivation] = createSignal(
    props.cluster?.healthCheck?.passive?.reactivationPeriod || "00:01:00",
  );
  const [errors, setErrors] = createSignal<Record<string, string>>({});
  const [showActive, setShowActive] = createSignal(activeEnabled());
  const [showPassive, setShowPassive] = createSignal(passiveEnabled());

  const addDestination = () =>
    setDestinations((prev) => [...prev, { address: "" }]);
  const removeDestination = (idx: number) =>
    setDestinations((prev) => prev.filter((_, i) => i !== idx));
  const updateDestinationAddress = (idx: number, value: string) =>
    setDestinations((prev) =>
      prev.map((d, i) => (i === idx ? { ...d, address: value } : d)),
    );

  const validate = (): boolean => {
    const errs: Record<string, string> = {};
    if (!clusterId().trim()) errs.clusterId = "集群名称不能为空";
    const validDests = destinations().filter((d) => d.address.trim());
    if (validDests.length === 0) {
      errs.destinations = "至少添加一个目标";
    }
    setErrors(errs);
    return Object.keys(errs).length === 0;
  };

  const handleSubmit = (e: Event) => {
    e.preventDefault();
    if (!validate()) return;

    const destList: DestinationConfig[] = destinations()
      .filter((d) => d.address.trim())
      .map((d) => ({
        id: d._internalId ?? "",
        address: d.address,
        healthy: true,
      }));

    const healthCheck: HealthCheckConfig = {
      active: {
        enabled: activeEnabled(),
        interval: activeInterval(),
        timeout: activeTimeout(),
        path: activePath(),
      },
      passive: {
        enabled: passiveEnabled(),
        policy: passivePolicy(),
        reactivationPeriod: passiveReactivation(),
      },
    };

    props.onSave({
      id: clusterId(),
      destinations: destList,
      loadBalancingPolicy: policy(),
      healthCheck,
    } as ClusterConfig);
  };

  return (
    <div
      class="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
      onClick={props.onClose}
    >
      <div
        class="card w-full max-w-2xl mx-4 max-h-[90vh] overflow-y-auto"
        onClick={(e) => e.stopPropagation()}
      >
        <h3 class="font-semibold mb-4">
          {props.cluster ? "编辑集群" : "添加集群"}
        </h3>
        <form onSubmit={handleSubmit} class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1">集群名称（即集群 ID）</label>
            {props.cluster ? (
              <p class="input bg-gray-100 dark:bg-dark-200 cursor-not-allowed">{clusterId()}</p>
            ) : (
              <input
                class="input"
                value={clusterId()}
                onInput={(e) => setClusterId(e.currentTarget.value)}
                required
              />
            )}
            {errors().clusterId && (
              <p class="text-danger text-xs mt-1">{errors().clusterId}</p>
            )}
          </div>

          <div>
            <label class="block text-sm font-medium mb-1">负载均衡策略</label>
            <select
              class="input"
              value={policy()}
              onChange={(e) => setPolicy(e.currentTarget.value)}
            >
              <option value="RoundRobin">轮询 (RoundRobin)</option>
              <option value="LeastRequests">最少连接 (LeastRequests)</option>
              <option value="Random">随机 (Random)</option>
              <option value="PowerOfTwoChoices">
                幂次选择 (PowerOfTwoChoices)
              </option>
            </select>
          </div>

          <div>
            <div class="flex items-center justify-between mb-2">
              <label class="block text-sm font-medium">目标地址</label>
              <button
                type="button"
                class="btn-icon btn-icon-primary"
                onClick={addDestination}
                title="添加目标"
              >
                <FaSolidPlus />
              </button>
            </div>
            <div class="space-y-2">
              <Index each={destinations()}>
                {(dest, idx) => (
                  <div class="flex items-center gap-2">
                    <input
                      class="input flex-1"
                      placeholder="http://localhost:5001"
                      value={dest().address}
                      onInput={(e) =>
                        updateDestinationAddress(idx, e.currentTarget.value)
                      }
                    />
                    <button
                      type="button"
                      class="btn-icon btn-icon-danger"
                      onClick={() => removeDestination(idx)}
                      disabled={destinations().length <= 1}
                      title="删除"
                    >
                      <FaSolidTimes />
                    </button>
                  </div>
                )}
              </Index>
            </div>
            {errors().destinations && (
              <p class="text-danger text-xs mt-1">{errors().destinations}</p>
            )}
          </div>

          <div class="border-t border-gray-200 dark:border-dark-200 pt-4">
            <button
              type="button"
              class="flex items-center gap-2 text-sm font-medium"
              onClick={() => setShowActive(!showActive())}
            >
              {showActive() ? <FaSolidChevronDown class="text-xs" /> : <FaSolidChevronRight class="text-xs" />}
              主动健康检查
            </button>
            {showActive() && (
              <div class="mt-3 grid grid-cols-2 gap-4">
                <div class="flex items-center gap-2 col-span-2">
                  <input
                    type="checkbox"
                    id="activeEnabled"
                    checked={activeEnabled()}
                    onChange={(e) => setActiveEnabled(e.currentTarget.checked)}
                  />
                  <label for="activeEnabled">启用主动健康检查</label>
                </div>
                <div>
                  <label class="block text-xs text-secondary mb-1">
                    检查间隔
                  </label>
                  <input
                    class="input"
                    value={activeInterval()}
                    onInput={(e) => setActiveInterval(e.currentTarget.value)}
                    placeholder="00:00:10"
                    disabled={!activeEnabled()}
                  />
                </div>
                <div>
                  <label class="block text-xs text-secondary mb-1">
                    超时时间
                  </label>
                  <input
                    class="input"
                    value={activeTimeout()}
                    onInput={(e) => setActiveTimeout(e.currentTarget.value)}
                    placeholder="00:00:05"
                    disabled={!activeEnabled()}
                  />
                </div>
                <div class="col-span-2">
                  <label class="block text-xs text-secondary mb-1">
                    健康检查路径
                  </label>
                  <input
                    class="input"
                    value={activePath()}
                    onInput={(e) => setActivePath(e.currentTarget.value)}
                    placeholder="/health"
                    disabled={!activeEnabled()}
                  />
                </div>
              </div>
            )}
          </div>

          <div class="border-t border-gray-200 dark:border-dark-200 pt-4">
            <button
              type="button"
              class="flex items-center gap-2 text-sm font-medium"
              onClick={() => setShowPassive(!showPassive())}
            >
              {showPassive() ? <FaSolidChevronDown class="text-xs" /> : <FaSolidChevronRight class="text-xs" />}
              被动健康检查
            </button>
            {showPassive() && (
              <div class="mt-3 grid grid-cols-2 gap-4">
                <div class="flex items-center gap-2 col-span-2">
                  <input
                    type="checkbox"
                    id="passiveEnabled"
                    checked={passiveEnabled()}
                    onChange={(e) => setPassiveEnabled(e.currentTarget.checked)}
                  />
                  <label for="passiveEnabled">启用被动健康检查</label>
                </div>
                <div>
                  <label class="block text-xs text-secondary mb-1">策略</label>
                  <select
                    class="input"
                    value={passivePolicy()}
                    onChange={(e) => setPassivePolicy(e.currentTarget.value)}
                    disabled={!passiveEnabled()}
                  >
                    <option value="FailureRate">失败率 (FailureRate)</option>
                    <option value="ConsecutiveFailures">
                      连续失败 (ConsecutiveFailures)
                    </option>
                  </select>
                </div>
                <div>
                  <label class="block text-xs text-secondary mb-1">
                    重新激活周期
                  </label>
                  <input
                    class="input"
                    value={passiveReactivation()}
                    onInput={(e) =>
                      setPassiveReactivation(e.currentTarget.value)
                    }
                    placeholder="00:01:00"
                    disabled={!passiveEnabled()}
                  />
                </div>
              </div>
            )}
          </div>

          <div class="flex justify-end space-x-2 pt-4 border-t border-gray-200 dark:border-dark-200">
            <button
              type="button"
              class="btn btn-secondary"
              onClick={props.onClose}
            >
              取消
            </button>
            <button type="submit" class="btn btn-primary">
              保存
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

import { For, Index, Show, createSignal, onMount } from "solid-js";
import type {
  ClusterConfig,
  DestinationConfig,
  HealthCheckConfig,
  PortainerContainer,
  PortainerDiscovery,
  PortainerEndpoint,
  PortainerGroup,
  PortainerInstance,
  PortainerNetwork,
  PortainerPort,
} from "../../types";
import { api } from "../../api/client";
import { addToast } from "../../store/toast";
import {
  FaSolidChevronLeft,
  FaSolidChevronRight,
  FaSolidTimes,
} from "solid-icons/fa";

interface Props {
  clusters: ClusterConfig[];
  defaultTargetClusterId?: string;
  onClose: () => void;
  onImported: () => void;
}

interface Selection {
  key: string;
  instanceId: string;
  endpointId: number;
  endpointName: string;
  networkName: string;
  groupName: string;
  standalone: boolean;
  containerId: string;
  containerName: string;
  privatePort: number;
  publicPort: number | null;
  form: "containerName" | "host";
  addresses: PortainerPort["addresses"];
  address: string;
}

const STEPS = ["Portainer 实例", "Endpoint", "网络", "容器与端口", "目标与确认"];

function portKey(
  instanceId: string,
  endpointId: number,
  networkName: string,
  containerId: string,
  port: PortainerPort,
): string {
  return `${instanceId}|${endpointId}|${networkName}|${containerId}|${port.private}|${port.public ?? "x"}`;
}

function slugify(value: string | null | undefined, fallback: string): string {
  const transform = (input: string) =>
    input
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9._-]+/g, "-")
      .replace(/-+/g, "-")
      .replace(/^-+|-+$/g, "");

  const slug = transform(value ?? "");
  if (slug) return slug;
  return transform(fallback) || "cluster";
}

function defaultHealthCheck(): HealthCheckConfig {
  return {
    active: {
      enabled: false,
      interval: "00:00:10",
      timeout: "00:00:05",
      path: "/health",
    },
    passive: {
      enabled: false,
      policy: "FailureRate",
      reactivationPeriod: "00:01:00",
    },
  };
}

function selfSourceLabel(source: string): string {
  switch (source) {
    case "config":
      return "配置覆盖";
    case "hostname":
      return "主机名自动识别";
    case "image":
      return "镜像兜底识别";
    default:
      return "未识别";
  }
}

function excludedReasonLabel(reason: string): string {
  switch (reason) {
    case "self-stack":
      return "自身 stack";
    case "portainer-system":
      return "Portainer 系统容器";
    case "not-running":
      return "未运行";
    case "extra-excluded":
      return "额外排除";
    default:
      return reason;
  }
}

function containerNameUnavailableReason(
  network: PortainerNetwork,
): string {
  if (network.isDefaultBridge) return "默认 bridge 不支持容器名解析";
  if (!network.dataPlaneAttached) return "data-plane 未接入此网络";
  if (!network.supportsContainerName) {
    return `网络驱动 ${network.driver ?? "未知"} 不支持容器名解析`;
  }
  return "容器名形态不可用";
}

export default function PortainerImportModal(props: Props) {
  const [instances, setInstances] = createSignal<PortainerInstance[]>([]);
  const [instancesLoading, setInstancesLoading] = createSignal(true);

  const [discovery, setDiscovery] = createSignal<PortainerDiscovery | null>(null);
  const [loading, setLoading] = createSignal(false);
  const [loadError, setLoadError] = createSignal<string | null>(null);

  const [step, setStep] = createSignal(1);
  const [selectedInstanceId, setSelectedInstanceId] = createSignal<string | null>(null);
  const [selectedEndpointId, setSelectedEndpointId] = createSignal<number | null>(null);
  const [selectedNetworkName, setSelectedNetworkName] = createSignal<string | null>(null);
  const [selectionMap, setSelectionMap] = createSignal<Record<string, Selection>>({});

  const [targetMode, setTargetMode] = createSignal<"new" | "append">(
    props.defaultTargetClusterId ? "append" : "new",
  );
  const [clusterName, setClusterName] = createSignal("");
  const [clusterNameTouched, setClusterNameTouched] = createSignal(false);
  const [targetClusterId, setTargetClusterId] = createSignal(
    props.defaultTargetClusterId ?? "",
  );

  const [submitting, setSubmitting] = createSignal(false);
  const [submitError, setSubmitError] = createSignal<string | null>(null);
  const [showExcluded, setShowExcluded] = createSignal(false);

  let nameInput: HTMLInputElement | undefined;

  const selectedList = () => Object.values(selectionMap());

  const selectedInstance = () =>
    instances().find((i) => i.id === selectedInstanceId()) ?? null;

  const selectedEndpoint = () =>
    discovery()?.endpoints.find((e) => e.id === selectedEndpointId()) ?? null;

  const selectedNetwork = () =>
    selectedEndpoint()?.networks.find((n) => n.name === selectedNetworkName()) ?? null;

  const loadInstances = async () => {
    setInstancesLoading(true);
    try {
      const all = await api.getPortainerInstances();
      setInstances(all.filter((i) => i.enabled));
    } catch (err) {
      addToast("error", `加载 Portainer 实例列表失败: ${(err as Error).message}`);
    } finally {
      setInstancesLoading(false);
    }
  };

  const loadDiscovery = async (instanceId: string) => {
    setLoading(true);
    setLoadError(null);
    setDiscovery(null);
    try {
      const data = await api.getPortainerDiscovery(instanceId);
      setDiscovery(data);
      if (data.endpoints.length === 1) {
        selectEndpoint(data.endpoints[0].id);
      }
    } catch (err) {
      setLoadError((err as Error).message || "加载 Portainer 发现结果失败");
    } finally {
      setLoading(false);
    }
  };

  onMount(loadInstances);

  const clearSelections = () => setSelectionMap({});

  const selectInstance = (id: string) => {
    if (selectedInstanceId() === id) return;
    setSelectedInstanceId(id);
    setSelectedEndpointId(null);
    setSelectedNetworkName(null);
    setDiscovery(null);
    clearSelections();
    void loadDiscovery(id);
  };

  const selectEndpoint = (id: number) => {
    if (selectedEndpointId() === id) return;
    setSelectedEndpointId(id);
    setSelectedNetworkName(null);
    clearSelections();

    const endpoint = discovery()?.endpoints.find((e) => e.id === id);
    if (endpoint && endpoint.networks.length === 1) {
      selectNetwork(endpoint.networks[0].name);
    }
  };

  const selectNetwork = (name: string) => {
    if (selectedNetworkName() === name) return;
    setSelectedNetworkName(name);
    clearSelections();
  };

  const selectionFor = (
    endpoint: PortainerEndpoint,
    network: PortainerNetwork,
    container: PortainerContainer,
    port: PortainerPort,
  ) => selectionMap()[portKey(selectedInstanceId()!, endpoint.id, network.name, container.id, port)];

  const togglePort = (
    endpoint: PortainerEndpoint,
    network: PortainerNetwork,
    group: PortainerGroup,
    container: PortainerContainer,
    port: PortainerPort,
  ) => {
    const instanceId = selectedInstanceId()!;
    const key = portKey(instanceId, endpoint.id, network.name, container.id, port);
    const current = selectionMap();

    if (current[key]) {
      const next = { ...current };
      delete next[key];
      setSelectionMap(next);
      return;
    }

    const form: Selection["form"] | null = port.addresses.containerName
      ? "containerName"
      : port.addresses.host
        ? "host"
        : null;
    if (!form) return;

    const address =
      form === "containerName"
        ? port.addresses.containerName!
        : port.addresses.host!;

    setSelectionMap({
      ...current,
      [key]: {
        key,
        instanceId,
        endpointId: endpoint.id,
        endpointName: endpoint.name,
        networkName: network.name,
        groupName: group.name,
        standalone: group.source === "standalone",
        containerId: container.id,
        containerName: container.name,
        privatePort: port.private,
        publicPort: port.public ?? null,
        form,
        addresses: port.addresses,
        address,
      },
    });
  };

  const setSelectionForm = (key: string, form: Selection["form"]) => {
    const current = selectionMap();
    const selection = current[key];
    if (!selection) return;

    const address =
      form === "containerName"
        ? selection.addresses.containerName
        : selection.addresses.host;
    if (!address) return;

    setSelectionMap({
      ...current,
      [key]: { ...selection, form, address },
    });
  };

  const updateSelectionAddress = (key: string, address: string) => {
    const current = selectionMap();
    const selection = current[key];
    if (!selection) return;

    setSelectionMap({
      ...current,
      [key]: { ...selection, address },
    });
  };

  const removeSelection = (key: string) => {
    const current = selectionMap();
    if (!current[key]) return;
    const next = { ...current };
    delete next[key];
    setSelectionMap(next);
  };

  const groupSelectedCount = (network: PortainerNetwork, group: PortainerGroup) =>
    selectedList().filter(
      (s) =>
        s.instanceId === selectedInstanceId() &&
        s.endpointId === selectedEndpointId() &&
        s.networkName === network.name &&
        s.groupName === group.name,
    ).length;

  const suggestClusterName = () => {
    const first = selectedList()[0];
    if (!first) return "";
    const fallback = `stack-${first.endpointId}`;
    return slugify(first.standalone ? "" : first.groupName, fallback);
  };

  const canNext = () => {
    switch (step()) {
      case 1:
        return selectedInstanceId() !== null && !instancesLoading() && discovery() !== null;
      case 2:
        return selectedEndpoint() !== null;
      case 3:
        return selectedNetwork() !== null;
      case 4:
        return selectedList().length > 0;
      default:
        return selectedList().length > 0;
    }
  };

  const goNext = () => {
    if (!canNext() || step() >= 5) return;
    if (step() === 4 && !clusterNameTouched()) {
      setClusterName(suggestClusterName());
    }
    setStep(step() + 1);
  };

  const goBack = () => {
    if (step() > 1) setStep(step() - 1);
  };

  const handleSubmit = async () => {
    if (submitting()) return;
    setSubmitError(null);

    const destinations = selectedList()
      .map((s) => s.address.trim())
      .filter((address) => address.length > 0);

    if (destinations.length === 0) {
      setSubmitError("请至少保留一个有效地址");
      return;
    }

    setSubmitting(true);
    try {
      if (targetMode() === "new") {
        const name = clusterName().trim();
        if (!name) {
          setSubmitError("请输入集群名称");
          queueMicrotask(() => nameInput?.focus());
          return;
        }

        const payload: ClusterConfig = {
          id: name,
          destinations: destinations.map((address) => ({
            id: "",
            address,
            healthy: true,
          })),
          loadBalancingPolicy: "RoundRobin",
          healthCheck: defaultHealthCheck(),
        };

        await api.post("/apimanagement/clusters", payload);
        addToast("success", `已创建集群 ${name}，包含 ${destinations.length} 个目标`);
      } else {
        const targetId = targetClusterId();
        if (!targetId) {
          setSubmitError("请选择要追加的集群");
          return;
        }

        const current = await api.get<ClusterConfig>(
          `/apimanagement/clusters/${encodeURIComponent(targetId)}`,
        );
        const existing = new Set(
          (current.destinations ?? []).map((d) => d.address.trim()),
        );
        const appended: DestinationConfig[] = [];

        for (const address of destinations) {
          if (existing.has(address)) continue;
          existing.add(address);
          appended.push({ id: "", address, healthy: true });
        }

        if (appended.length === 0) {
          setSubmitError("所选地址均已存在于目标集群");
          return;
        }

        await api.put(`/apimanagement/clusters/${encodeURIComponent(targetId)}`, {
          ...current,
          destinations: [
            ...(current.destinations ?? []).map((d) => ({
              id: d.id,
              address: d.address,
              healthy: d.healthy,
            })),
            ...appended,
          ],
        });
        addToast("success", `已向 ${targetId} 追加 ${appended.length} 个目标`);
      }

      props.onImported();
    } catch (err) {
      const message = (err as Error).message || "导入失败";
      setSubmitError(message);
      if (targetMode() === "new" && message.includes("已存在")) {
        queueMicrotask(() => nameInput?.focus());
      }
      addToast("error", message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      onClick={(e) => {
        if (e.target === e.currentTarget) props.onClose();
      }}
    >
      <div class="bg-white dark:bg-dark-100 rounded-xl shadow-2xl w-full max-w-5xl max-h-[90vh] flex flex-col mx-4">
        <div class="flex items-start justify-between px-6 py-4 border-b border-gray-200 dark:border-dark-200">
          <div>
            <h3 class="text-lg font-bold flex items-center gap-2">
              从 Portainer 导入
              <Show when={discovery()?.version}>
                <span class="badge badge-success text-xs">v{discovery()!.version}</span>
              </Show>
            </h3>
            <p class="text-xs text-secondary mt-1">
              选择 Portainer 实例，发现容器并生成集群目标
            </p>
          </div>
          <button
            onClick={props.onClose}
            class="text-secondary hover:text-gray-800 dark:hover:text-gray-200 text-xl leading-none"
            title="关闭"
          >
            <FaSolidTimes />
          </button>
        </div>

        <div class="px-6 pt-4">
          <div class="flex flex-wrap items-center gap-2 mb-4">
            <For each={STEPS}>
              {(label, index) => {
                const number = index() + 1;
                return (
                  <div class="flex items-center gap-2">
                    <span
                      class={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold ${
                        step() === number
                          ? "bg-primary text-white"
                          : step() > number
                            ? "bg-success/10 text-success"
                            : "bg-gray-100 dark:bg-dark-200 text-secondary"
                      }`}
                    >
                      {step() > number ? "✓" : number}
                    </span>
                    <span
                      class={`text-sm ${
                        step() === number ? "font-medium" : "text-secondary"
                      }`}
                    >
                      {label}
                    </span>
                    <Show when={number < STEPS.length}>
                      <span class="text-gray-300 dark:text-dark-200 mx-1">/</span>
                    </Show>
                  </div>
                );
              }}
            </For>
          </div>
        </div>

        <div class="flex-1 overflow-y-auto px-6 pb-4">
          {/* Step 0: instance selection (step === 1) */}
          <Show when={step() === 1}>
            <Show when={instancesLoading()}>
              <div class="flex items-center justify-center py-16 text-secondary text-sm">
                正在加载 Portainer 实例列表...
              </div>
            </Show>

            <Show when={!instancesLoading() && instances().length === 0}>
              <div class="text-center py-16 text-secondary text-sm">
                <p>没有可用的 Portainer 实例</p>
                <p class="text-xs mt-2">请先在设置页面添加并启用 Portainer 实例</p>
              </div>
            </Show>

            <Show when={!instancesLoading() && instances().length > 0}>
              <div class="space-y-3">
                <For each={instances()}>
                  {(instance) => (
                    <label
                      class={`flex items-start gap-3 p-4 rounded-lg border transition cursor-pointer ${
                        selectedInstanceId() === instance.id
                          ? "border-primary bg-primary/5"
                          : "border-gray-200 dark:border-dark-200 hover:border-primary/50"
                      }`}
                    >
                      <input
                        type="radio"
                        name="portainer-instance"
                        class="mt-1"
                        checked={selectedInstanceId() === instance.id}
                        onChange={() => selectInstance(instance.id)}
                      />
                      <div class="flex-1 min-w-0">
                        <div class="flex flex-wrap items-center gap-2">
                          <span class="font-medium">{instance.name || instance.id}</span>
                          <span class="text-xs text-secondary font-mono">{instance.id}</span>
                          <span class={`badge text-xs ${instance.enabled ? "badge-success" : "badge-warning"}`}>
                            {instance.enabled ? "启用" : "禁用"}
                          </span>
                        </div>
                        <p class="text-xs text-secondary mt-1 font-mono break-all">
                          {instance.baseUrl}
                        </p>
                      </div>
                    </label>
                  )}
                </For>
              </div>

              <Show when={loading()}>
                <div class="flex items-center justify-center py-8 text-secondary text-sm mt-4">
                  正在连接 Portainer 并枚举容器...
                </div>
              </Show>

              <Show when={!loading() && loadError()}>
                <div class="bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-3 text-sm mt-4">
                  {loadError()}
                </div>
              </Show>

              <Show when={!loading() && discovery()}>
                <Show when={discovery()!.self.detected}>
                  <div class="bg-success/10 border border-success/30 text-success rounded-lg px-4 py-2 text-xs mt-4">
                    已识别自身 stack：
                    <span class="font-mono">{discovery()!.self.stacks.join(", ")}</span>
                    （{selfSourceLabel(discovery()!.self.source)}，
                    {discovery()!.self.containerCount} 个容器已排除）
                  </div>
                </Show>
                <Show when={!discovery()!.self.detected}>
                  <div class="bg-warning/10 border border-warning/30 text-warning rounded-lg px-4 py-2 text-xs mt-4">
                    未能自动识别自身 stack。若当前控制面就在此 Portainer 中，请核对主机名，或通过
                    <span class="font-mono mx-1">SelfStackNames</span>
                    显式指定，否则可能把自身容器导入为代理目标。
                  </div>
                </Show>
              </Show>
            </Show>
          </Show>

          {/* Step 1: endpoint (step === 2) */}
          <Show when={step() === 2}>
            <Show when={loading()}>
              <div class="flex items-center justify-center py-16 text-secondary text-sm">
                正在连接 Portainer 并枚举容器...
              </div>
            </Show>

            <Show when={!loading() && loadError()}>
              <div class="bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-3 text-sm">
                <p>{loadError()}</p>
                <button
                  class="btn-text-primary text-sm mt-2"
                  onClick={() => selectedInstanceId() && loadDiscovery(selectedInstanceId()!)}
                >
                  重试
                </button>
              </div>
            </Show>

            <Show when={!loading() && !loadError() && discovery()}>
              <Show
                when={discovery()!.endpoints.length > 0}
                fallback={
                  <div class="text-center py-12 text-secondary text-sm">
                    未发现任何 Endpoint
                  </div>
                }
              >
                <div class="space-y-3">
                  <For each={discovery()!.endpoints}>
                    {(endpoint) => (
                      <label
                        class={`flex items-start gap-3 p-4 rounded-lg border transition ${
                          endpoint.error
                            ? "opacity-60 border-gray-200 dark:border-dark-200"
                            : selectedEndpointId() === endpoint.id
                              ? "border-primary bg-primary/5 cursor-pointer"
                              : "border-gray-200 dark:border-dark-200 hover:border-primary/50 cursor-pointer"
                        }`}
                      >
                        <input
                          type="radio"
                          name="portainer-endpoint"
                          class="mt-1"
                          checked={selectedEndpointId() === endpoint.id}
                          disabled={!!endpoint.error}
                          onChange={() => selectEndpoint(endpoint.id)}
                        />
                        <div class="flex-1 min-w-0">
                          <div class="flex flex-wrap items-center gap-2">
                            <span class="font-medium">{endpoint.name}</span>
                            <span class="text-xs text-secondary">
                              #{endpoint.id} · {endpoint.engine}
                            </span>
                            <span
                              class={`badge text-xs ${
                                endpoint.status === "up"
                                  ? "badge-success"
                                  : "badge-warning"
                              }`}
                            >
                              {endpoint.status === "up" ? "在线" : "离线"}
                            </span>
                          </div>
                          <Show when={endpoint.error}>
                            <p class="text-danger text-xs mt-1 break-all">
                              {endpoint.error}
                            </p>
                          </Show>
                          <Show when={!endpoint.error}>
                            <p class="text-xs text-secondary mt-1">
                              {endpoint.networks.length} 个可导入网络
                            </p>
                          </Show>
                        </div>
                      </label>
                    )}
                  </For>
                </div>
              </Show>
            </Show>
          </Show>

          {/* Step 2: network (step === 3) */}
          <Show when={step() === 3}>
            <Show
              when={selectedEndpoint()}
              fallback={<EmptyHint text="请先选择 Endpoint" />}
            >
              <Show
                when={selectedEndpoint()!.networks.length > 0}
                fallback={
                  <EmptyHint text="该 Endpoint 没有可导入的容器（可能全部被排除或未运行）" />
                }
              >
                <div class="space-y-3">
                  <For each={selectedEndpoint()!.networks}>
                    {(network) => {
                      const containerCount = () =>
                        network.groups.reduce(
                          (sum, group) => sum + group.containers.length,
                          0,
                        );
                      return (
                        <label
                          class={`flex items-start gap-3 p-4 rounded-lg border cursor-pointer transition ${
                            selectedNetworkName() === network.name
                              ? "border-primary bg-primary/5"
                              : "border-gray-200 dark:border-dark-200 hover:border-primary/50"
                          }`}
                        >
                          <input
                            type="radio"
                            name="portainer-network"
                            class="mt-1"
                            checked={selectedNetworkName() === network.name}
                            onChange={() => selectNetwork(network.name)}
                          />
                          <div class="flex-1 min-w-0">
                            <div class="flex flex-wrap items-center gap-2">
                              <span class="font-medium font-mono text-sm">
                                {network.name}
                              </span>
                              <Show when={network.driver}>
                                <span class="text-xs text-secondary">
                                  {network.driver}
                                </span>
                              </Show>
                              <Show when={network.isDefaultBridge}>
                                <span class="badge badge-warning text-xs">
                                  默认 bridge
                                </span>
                              </Show>
                              <Show when={network.dataPlaneAttached}>
                                <span class="badge badge-success text-xs">
                                  data-plane 已接入
                                </span>
                              </Show>
                              <Show
                                when={
                                  !network.isDefaultBridge &&
                                  !network.dataPlaneAttached
                                }
                              >
                                <span class="badge badge-warning text-xs">
                                  容器名不可用
                                </span>
                              </Show>
                            </div>
                            <p class="text-xs text-secondary mt-1">
                              {network.groups.length} 个分组 · {containerCount()} 个容器
                            </p>
                          </div>
                        </label>
                      );
                    }}
                  </For>
                </div>
                <p class="text-xs text-secondary mt-3">
                  容器名形态只在 user-defined 网络上、且 data-plane
                  与该网络同网时可用；否则只能使用宿主地址。
                </p>
              </Show>
            </Show>
          </Show>

          {/* Step 3: containers × ports (step === 4) */}
          <Show when={step() === 4}>
            <Show
              when={selectedNetwork()}
              fallback={<EmptyHint text="请先选择网络" />}
            >
              <div class="flex items-center justify-between mb-3">
                <div class="text-sm">
                  <span class="text-secondary">网络：</span>
                  <span class="font-mono">{selectedNetwork()!.name}</span>
                </div>
                <span class="badge badge-success text-xs">
                  已选 {selectedList().length} 个目标
                </span>
              </div>

              <Show
                when={selectedNetwork()!.groups.length > 0}
                fallback={<EmptyHint text="该网络下没有可导入的容器" />}
              >
                <div class="space-y-3">
                  <For each={selectedNetwork()!.groups}>
                    {(group) => (
                      <div class="border border-gray-200 dark:border-dark-200 rounded-lg p-3">
                        <div class="flex items-center justify-between mb-2">
                          <div class="flex items-center gap-2">
                            <span class="font-medium text-sm">{group.name}</span>
                            <span class="badge text-xs bg-gray-100 dark:bg-dark-200 text-secondary">
                              {group.source === "standalone" ? "standalone" : "compose"}
                            </span>
                          </div>
                          <span class="text-xs text-secondary">
                            {groupSelectedCount(selectedNetwork()!, group)} 已选
                          </span>
                        </div>

                        <For each={group.containers}>
                          {(container) => (
                            <div class="py-2 border-t border-gray-100 dark:border-dark-200 first:border-t-0">
                              <div class="flex flex-wrap items-baseline gap-2">
                                <span class="font-medium text-sm">
                                  {container.name}
                                </span>
                                <Show when={container.service}>
                                  <span class="text-xs text-secondary font-mono">
                                    {container.service}
                                  </span>
                                </Show>
                              </div>
                              <p class="text-[11px] text-secondary break-all">
                                {container.image}
                              </p>

                              <div class="mt-1 space-y-1">
                                <For each={container.ports}>
                                  {(port) => {
                                    const key = portKey(
                                      selectedInstanceId()!,
                                      selectedEndpoint()!.id,
                                      selectedNetwork()!.name,
                                      container.id,
                                      port,
                                    );
                                    return (
                                      <PortChoice
                                        endpoint={selectedEndpoint()!}
                                        network={selectedNetwork()!}
                                        group={group}
                                        container={container}
                                        port={port}
                                        selected={selectionFor(
                                          selectedEndpoint()!,
                                          selectedNetwork()!,
                                          container,
                                          port,
                                        )}
                                        onToggle={() =>
                                          togglePort(
                                            selectedEndpoint()!,
                                            selectedNetwork()!,
                                            group,
                                            container,
                                            port,
                                          )
                                        }
                                        onFormChange={(form) =>
                                          setSelectionForm(key, form)
                                        }
                                      />
                                    );
                                  }}
                                </For>
                              </div>
                            </div>
                          )}
                        </For>
                      </div>
                    )}
                  </For>
                </div>
              </Show>

              <Show when={(discovery()?.excluded.length ?? 0) > 0}>
                <div class="mt-4">
                  <button
                    type="button"
                    class="btn-text text-secondary text-xs"
                    onClick={() => setShowExcluded(!showExcluded())}
                  >
                    {showExcluded() ? "收起" : "查看"}已排除的{" "}
                    {discovery()!.excluded.length} 个容器
                  </button>
                  <Show when={showExcluded()}>
                    <div class="mt-2 max-h-40 overflow-y-auto border border-gray-200 dark:border-dark-200 rounded-lg divide-y divide-gray-100 dark:divide-dark-200">
                      <For each={discovery()!.excluded}>
                        {(item) => (
                          <div class="flex items-center justify-between gap-3 px-3 py-1.5 text-xs">
                            <span class="font-mono break-all">{item.name}</span>
                            <span class="text-secondary shrink-0">
                              {excludedReasonLabel(item.reason)}
                              <Show when={item.endpoint}> · {item.endpoint}</Show>
                            </span>
                          </div>
                        )}
                      </For>
                    </div>
                  </Show>
                </div>
              </Show>
            </Show>
          </Show>

          {/* Step 4: target and preview (step === 5) */}
          <Show when={step() === 5}>
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
              <div class="space-y-3">
                <h4 class="text-sm font-semibold">目标集群</h4>
                <div class="space-y-2">
                  <label class="flex items-center gap-2 text-sm cursor-pointer">
                    <input
                      type="radio"
                      name="portainer-target"
                      checked={targetMode() === "new"}
                      onChange={() => setTargetMode("new")}
                    />
                    新建集群
                  </label>
                  <label
                    class={`flex items-center gap-2 text-sm ${
                      props.clusters.length > 0
                        ? "cursor-pointer"
                        : "text-secondary cursor-not-allowed"
                    }`}
                  >
                    <input
                      type="radio"
                      name="portainer-target"
                      checked={targetMode() === "append"}
                      disabled={props.clusters.length === 0}
                      onChange={() => setTargetMode("append")}
                    />
                    追加到已有集群
                    <span class="text-xs text-secondary">
                      （按地址去重）
                    </span>
                  </label>
                </div>

                <Show when={targetMode() === "new"}>
                  <div>
                    <label class="block text-sm font-medium mb-1">
                      集群名称（即 ID，创建后不可修改）
                    </label>
                    <input
                      ref={(el) => (nameInput = el)}
                      class="input font-mono"
                      value={clusterName()}
                      placeholder="例如 staticpages"
                      onInput={(e) => {
                        setClusterName(e.currentTarget.value);
                        setClusterNameTouched(true);
                      }}
                    />
                    <p class="text-xs text-secondary mt-1">
                      预填为分组名 slug，可修改；重名时不会自动改名。
                    </p>
                  </div>
                </Show>

                <Show when={targetMode() === "append"}>
                  <div>
                    <label class="block text-sm font-medium mb-1">
                      目标集群
                    </label>
                    <select
                      class="input"
                      value={targetClusterId()}
                      onChange={(e) => setTargetClusterId(e.currentTarget.value)}
                    >
                      <option value="">选择要追加的集群...</option>
                      <For each={props.clusters}>
                        {(cluster) => (
                          <option value={cluster.id}>
                            {cluster.id}（{cluster.destinations?.length ?? 0} 个目标）
                          </option>
                        )}
                      </For>
                    </select>
                    <Show when={props.clusters.length === 0}>
                      <p class="text-warning text-xs mt-1">
                        当前没有可追加的集群，请选择新建。
                      </p>
                    </Show>
                  </div>
                </Show>

                <div class="text-xs text-secondary">
                  本次将写入 {selectedList().length} 个目标；地址可在右侧逐条编辑或移除。
                </div>
              </div>

              <div class="space-y-2">
                <h4 class="text-sm font-semibold">
                  目标地址预览（{selectedList().length}）
                </h4>
                <Show
                  when={selectedList().length > 0}
                  fallback={<EmptyHint text="尚未勾选任何端口" />}
                >
                  <div class="max-h-[46vh] overflow-y-auto space-y-2 pr-1">
                    <Index each={selectedList()}>
                      {(selection) => (
                        <div class="p-3 rounded-lg border border-gray-200 dark:border-dark-200">
                          <div class="flex items-start justify-between gap-2">
                            <div class="min-w-0">
                              <p class="text-sm font-medium truncate">
                                {selection().groupName} / {selection().containerName}
                              </p>
                              <p class="text-[11px] text-secondary">
                                {selection().instanceId} · {selection().endpointName} ·{" "}
                                {selection().networkName} · 端口{" "}
                                {selection().privatePort}
                                {selection().publicPort
                                  ? ` → ${selection().publicPort}`
                                  : ""}
                              </p>
                            </div>
                            <button
                              type="button"
                              class="btn-icon btn-icon-danger shrink-0"
                              title="移除"
                              onClick={() => removeSelection(selection().key)}
                            >
                              <FaSolidTimes />
                            </button>
                          </div>
                          <input
                            class="input mt-2 font-mono text-sm"
                            value={selection().address}
                            onInput={(e) =>
                              updateSelectionAddress(
                                selection().key,
                                e.currentTarget.value,
                              )
                            }
                          />
                          <div class="text-[11px] text-secondary mt-1">
                            {selection().form === "containerName"
                              ? "容器名形态"
                              : "宿主形态"}
                          </div>
                        </div>
                      )}
                    </Index>
                  </div>
                </Show>
              </div>
            </div>

            <Show when={submitError()}>
              <div class="mt-4 bg-danger/10 border border-danger/30 text-danger rounded-lg px-4 py-2 text-sm break-all">
                {submitError()}
              </div>
            </Show>
          </Show>
        </div>

        <div class="flex items-center justify-between gap-3 px-6 py-3 border-t border-gray-200 dark:border-dark-200">
          <div class="text-xs text-secondary">
            <Show when={!loading() && !loadError()}>
              <Show when={step() === 4}>
                已选 {selectedList().length} 个目标
              </Show>
              <Show when={step() === 5}>
                {targetMode() === "new" ? "新建集群" : "追加到已有集群"}
              </Show>
            </Show>
          </div>
          <div class="flex items-center gap-2">
            <button
              class="btn btn-secondary"
              onClick={props.onClose}
              disabled={submitting()}
            >
              取消
            </button>
            <Show when={step() > 1}>
              <button
                class="btn btn-secondary"
                onClick={goBack}
                disabled={submitting()}
              >
                <FaSolidChevronLeft class="mr-1" />
                上一步
              </button>
            </Show>
            <Show when={step() < 5}>
              <button
                class="btn btn-primary"
                onClick={goNext}
                disabled={!canNext()}
              >
                下一步
                <FaSolidChevronRight class="ml-1" />
              </button>
            </Show>
            <Show when={step() === 5}>
              <button
                class="btn btn-primary"
                onClick={handleSubmit}
                disabled={submitting() || selectedList().length === 0}
              >
                {submitting()
                  ? "提交中..."
                  : targetMode() === "new"
                    ? "创建集群"
                    : `追加 ${selectedList().length} 个目标`}
              </button>
            </Show>
          </div>
        </div>
      </div>
    </div>
  );
}

function EmptyHint(props: { text: string }) {
  return (
    <div class="text-center py-10 text-secondary text-sm border border-dashed border-gray-200 dark:border-dark-200 rounded-lg">
      {props.text}
    </div>
  );
}

function PortChoice(props: {
  endpoint: PortainerEndpoint;
  network: PortainerNetwork;
  group: PortainerGroup;
  container: PortainerContainer;
  port: PortainerPort;
  selected: Selection | undefined;
  onToggle: () => void;
  onFormChange: (form: Selection["form"]) => void;
}) {
  const containerAddress = () => props.port.addresses.containerName ?? null;
  const hostAddress = () => props.port.addresses.host ?? null;
  const selectable = () => Boolean(containerAddress() || hostAddress());
  const bothForms = () => Boolean(containerAddress() && hostAddress());

  return (
    <div
      class={`flex items-start gap-2 rounded-lg px-2 py-1.5 ${
        props.selected ? "bg-primary/5" : ""
      }`}
    >
      <input
        type="checkbox"
        class="mt-1"
        checked={!!props.selected}
        disabled={!selectable()}
        onChange={() => props.onToggle()}
      />
      <div class="flex-1 min-w-0">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <span class="font-mono text-sm">
            :{props.port.private}
            {props.port.public ? ` → ${props.port.public}` : ""}
          </span>
          <span class="text-[11px] text-secondary">
            {props.port.public ? "已发布" : "未发布"}
            {props.port.bindIp ? ` · bind ${props.port.bindIp}` : ""}
          </span>
        </div>

        <div class="text-[11px] font-mono text-secondary mt-0.5 space-y-0.5">
          <Show when={containerAddress()}>
            <div>容器名 {containerAddress()}</div>
          </Show>
          <Show when={hostAddress()}>
            <div>宿主 {hostAddress()}</div>
          </Show>
          <Show when={!selectable()}>
            <div class="text-warning">
              无可用地址（端口未发布且容器名形态不可用）
            </div>
          </Show>
        </div>

        <Show when={!containerAddress()}>
          <p class="text-[11px] text-warning mt-0.5">
            {containerNameUnavailableReason(props.network)}
          </p>
        </Show>

        <Show when={props.selected}>
          <div class="flex flex-wrap items-center gap-1 mt-1">
            <Show when={bothForms()}>
              <span class="text-[11px] text-secondary mr-1">地址形态</span>
              <button
                type="button"
                class={`btn-text text-[11px] px-2 py-0.5 ${
                  props.selected!.form === "containerName"
                    ? "bg-primary/10 text-primary"
                    : "text-secondary"
                }`}
                onClick={() => props.onFormChange("containerName")}
              >
                容器名
              </button>
              <button
                type="button"
                class={`btn-text text-[11px] px-2 py-0.5 ${
                  props.selected!.form === "host"
                    ? "bg-primary/10 text-primary"
                    : "text-secondary"
                }`}
                onClick={() => props.onFormChange("host")}
              >
                宿主
              </button>
            </Show>
            <span class="font-mono text-[11px] text-primary break-all">
              {props.selected!.address}
            </span>
          </div>
        </Show>
      </div>
    </div>
  );
}

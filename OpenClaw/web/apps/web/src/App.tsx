import { useEffect, useMemo, useState } from "react"
import { Button } from "@workspace/ui/components/button"

type User = {
  id: string
  userName: string
  displayName: string
  role: string
}

type ServerNode = {
  id: string
  name: string
  healthStatus?: string
  lastCheckedAt?: string | null
  lastHealthMessage?: string | null
  baseUrl?: string
  apiToken?: string
  persistentRootPath?: string
  isEnabled?: boolean
}

type Template = {
  id: string
  name: string
  description: string
  currentVersionId?: string | null
  versions?: TemplateVersion[]
}

type TemplateVersion = {
  id: string
  version: string
  image: string
  containerPort: number
  commandJson: string
  configMountPath: string
  configFileName: string
  workspaceMountPath: string
}

type Deployment = {
  id: string
  sandboxId?: string | null
  apiEndpoint: string
  apiType: string
  model: string
  createdAt?: string
  updatedAt?: string
  serverName?: string
  userName?: string
}

type DeploymentDetail = {
  id: string
  sandboxId?: string | null
  containerId?: string | null
  apiEndpoint: string
  apiType: string
  model: string
  persistentDirectory: string
  configFilePath: string
  createdAt?: string
  updatedAt?: string
  status?: string | null
  cpuPercent?: number | null
  memoryPercent?: number | null
  memoryUsage?: string | null
  memoryLimit?: string | null
  templateSnapshot?: unknown
  server?: { id: string; name: string }
  user?: { id: string; userName: string; displayName: string }
}

const jsonHeaders = { "Content-Type": "application/json" }

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    credentials: "include",
    ...init,
    headers: {
      ...(init?.headers ?? {}),
    },
  })

  if (response.status === 401) {
    throw new Error("UNAUTHORIZED")
  }

  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || response.statusText)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

function formatJson(value: unknown) {
  return JSON.stringify(value, null, 2)
}

function buildWsUrl(path: string) {
  const protocol = window.location.protocol === "https:" ? "wss:" : "ws:"
  return `${protocol}//${window.location.host}${path}`
}

export function App() {
  const [me, setMe] = useState<User | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState("")
  const [loginForm, setLoginForm] = useState({ userName: "", password: "" })
  const [servers, setServers] = useState<ServerNode[]>([])
  const [templates, setTemplates] = useState<Template[]>([])
  const [deployments, setDeployments] = useState<Deployment[]>([])
  const [selectedDeploymentId, setSelectedDeploymentId] = useState<string>("")
  const [selectedDeployment, setSelectedDeployment] = useState<DeploymentDetail | null>(null)
  const [logs, setLogs] = useState<string[]>([])
  const [terminalLines, setTerminalLines] = useState<string[]>([])
  const [terminalInput, setTerminalInput] = useState("")
  const [terminalSocket, setTerminalSocket] = useState<WebSocket | null>(null)
  const [logSocket, setLogSocket] = useState<WebSocket | null>(null)
  const [deployForm, setDeployForm] = useState({ sandboxServerId: "", templateId: "", apiEndpoint: "", apiType: "chat", model: "", apiKey: "" })
  const [adminUserForm, setAdminUserForm] = useState({ userName: "", displayName: "", password: "", role: "Employee" })
  const [adminServerForm, setAdminServerForm] = useState({ name: "", baseUrl: "", apiToken: "", persistentRootPath: "", isEnabled: true })
  const [adminTemplateForm, setAdminTemplateForm] = useState({ name: "", description: "", isEnabled: true })
  const [adminVersionForm, setAdminVersionForm] = useState({ templateId: "", version: "v1", image: "ghcr.io/aidotnet/openclaw:latest", containerPort: 3000, command: "[]", configMountPath: "/app/config", configFileName: "openclaw.json", workspaceMountPath: "/app/data", isActive: true })
  const [adminUsers, setAdminUsers] = useState<Array<{ id: string; userName: string; displayName: string; role: string; status: string }>>([])
  const [settings, setSettings] = useState({ defaultCpu: "1000m", defaultMemory: "1Gi", defaultLogTailLines: 200 })

  const selectedServerName = useMemo(() => servers.find((item) => item.id === deployForm.sandboxServerId)?.name ?? "", [servers, deployForm.sandboxServerId])

  useEffect(() => {
    void bootstrap()
    return () => {
      terminalSocket?.close()
      logSocket?.close()
    }
  }, [])

  useEffect(() => {
    if (!selectedDeploymentId || !me) {
      return
    }

    void loadDeploymentDetail(selectedDeploymentId)
    const timer = window.setInterval(() => {
      void loadDeploymentDetail(selectedDeploymentId)
    }, 5000)
    return () => window.clearInterval(timer)
  }, [selectedDeploymentId, me])

  async function bootstrap() {
    try {
      const current = await request<User>("/api/auth/me")
      setMe(current)
      await loadDashboard(current)
    } catch {
      setMe(null)
    } finally {
      setLoading(false)
    }
  }

  async function loadDashboard(currentUser: User) {
    const [serverItems, templateItems, deploymentItems] = await Promise.all([
      request<ServerNode[]>(currentUser.role === "Admin" ? "/api/admin/sandbox-servers" : "/api/sandbox-servers"),
      request<Template[]>(currentUser.role === "Admin" ? "/api/admin/templates" : "/api/templates"),
      request<Deployment[]>("/api/deployments"),
    ])

    setServers(serverItems)
    setTemplates(templateItems)
    setDeployments(deploymentItems)

    if (currentUser.role === "Admin") {
      const [userItems, settingItem] = await Promise.all([
        request<Array<{ id: string; userName: string; displayName: string; role: string; status: string }>>("/api/admin/users"),
        request<{ defaultCpu: string; defaultMemory: string; defaultLogTailLines: number }>("/api/admin/settings"),
      ])
      setAdminUsers(userItems)
      setSettings(settingItem)
    }
  }

  async function login() {
    setError("")
    try {
      const current = await request<User>("/api/auth/login", {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify(loginForm),
      })
      setMe(current)
      await loadDashboard(current)
    } catch (err) {
      setError(err instanceof Error ? err.message : "登录失败")
    }
  }

  async function logout() {
    await request("/api/auth/logout", { method: "POST" })
    terminalSocket?.close()
    logSocket?.close()
    setMe(null)
    setDeployments([])
    setSelectedDeployment(null)
    setLogs([])
    setTerminalLines([])
  }

  async function createUser() {
    await request("/api/admin/users", {
      method: "POST",
      headers: jsonHeaders,
      body: JSON.stringify(adminUserForm),
    })
    setAdminUserForm({ userName: "", displayName: "", password: "", role: "Employee" })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function saveServer() {
    await request("/api/admin/sandbox-servers", {
      method: "POST",
      headers: jsonHeaders,
      body: JSON.stringify(adminServerForm),
    })
    setAdminServerForm({ name: "", baseUrl: "", apiToken: "", persistentRootPath: "", isEnabled: true })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function checkServer(serverId: string) {
    await request(`/api/admin/sandbox-servers/${serverId}/health`, { method: "POST" })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function saveSettings() {
    await request("/api/admin/settings", {
      method: "PUT",
      headers: jsonHeaders,
      body: JSON.stringify(settings),
    })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function saveTemplate() {
    await request("/api/admin/templates", {
      method: "POST",
      headers: jsonHeaders,
      body: JSON.stringify(adminTemplateForm),
    })
    setAdminTemplateForm({ name: "", description: "", isEnabled: true })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function saveTemplateVersion() {
    await request(`/api/admin/templates/${adminVersionForm.templateId}/versions`, {
      method: "POST",
      headers: jsonHeaders,
      body: JSON.stringify({
        ...adminVersionForm,
        command: JSON.parse(adminVersionForm.command),
      }),
    })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function deploy() {
    await request("/api/deployments", {
      method: "POST",
      headers: jsonHeaders,
      body: JSON.stringify(deployForm),
    })
    if (me) {
      await loadDashboard(me)
    }
  }

  async function loadDeploymentDetail(id: string) {
    const detail = await request<DeploymentDetail>(`/api/deployments/${id}`)
    setSelectedDeployment(detail)
    const logResponse = await request<{ lines: string[] }>(`/api/deployments/${id}/logs`)
    setLogs(logResponse.lines)
  }

  function connectLogs() {
    if (!selectedDeploymentId) {
      return
    }

    logSocket?.close()
    const socket = new WebSocket(buildWsUrl(`/api/deployments/${selectedDeploymentId}/logs/ws`))
    socket.onmessage = (event) => {
      setLogs((current) => [...current, event.data].slice(-500))
    }
    setLogSocket(socket)
  }

  function connectTerminal() {
    if (!selectedDeploymentId) {
      return
    }

    terminalSocket?.close()
    const socket = new WebSocket(buildWsUrl(`/api/deployments/${selectedDeploymentId}/terminal/ws`))
    socket.onmessage = (event) => {
      setTerminalLines((current) => [...current, event.data].slice(-1000))
    }
    setTerminalSocket(socket)
  }

  function sendTerminal() {
    if (!terminalSocket || terminalSocket.readyState !== WebSocket.OPEN || !terminalInput.trim()) {
      return
    }

    terminalSocket.send(`${terminalInput}\n`)
    setTerminalInput("")
  }

  if (loading) {
    return <div className="p-6 text-sm">加载中...</div>
  }

  if (!me) {
    return (
      <div className="mx-auto flex min-h-svh max-w-sm flex-col justify-center gap-3 p-6">
        <h1 className="text-xl font-semibold">OpenClaw</h1>
        <input className="rounded border px-3 py-2" placeholder="用户名" value={loginForm.userName} onChange={(e) => setLoginForm((s) => ({ ...s, userName: e.target.value }))} />
        <input className="rounded border px-3 py-2" placeholder="密码" type="password" value={loginForm.password} onChange={(e) => setLoginForm((s) => ({ ...s, password: e.target.value }))} />
        <Button onClick={login}>登录</Button>
        {error ? <div className="text-sm text-red-600">{error}</div> : null}
      </div>
    )
  }

  return (
    <div className="min-h-svh bg-background text-foreground">
      <div className="flex items-center justify-between border-b px-6 py-4">
        <div>
          <div className="text-xl font-semibold">OpenClaw 控制台</div>
          <div className="text-sm text-muted-foreground">{me.displayName} · {me.role}</div>
        </div>
        <Button onClick={logout}>退出</Button>
      </div>

      <div className="grid gap-4 p-6 lg:grid-cols-[1.1fr_0.9fr]">
        <div className="space-y-4">
          <section className="rounded border p-4">
            <div className="mb-3 text-lg font-semibold">员工部署</div>
            <div className="grid gap-3 md:grid-cols-2">
              <select className="rounded border px-3 py-2" value={deployForm.sandboxServerId} onChange={(e) => setDeployForm((s) => ({ ...s, sandboxServerId: e.target.value }))}>
                <option value="">选择沙盒服务端</option>
                {servers.map((server) => <option key={server.id} value={server.id}>{server.name}</option>)}
              </select>
              <select className="rounded border px-3 py-2" value={deployForm.templateId} onChange={(e) => setDeployForm((s) => ({ ...s, templateId: e.target.value }))}>
                <option value="">选择模板</option>
                {templates.map((template) => <option key={template.id} value={template.id}>{template.name}</option>)}
              </select>
              <input className="rounded border px-3 py-2" placeholder="API Endpoint" value={deployForm.apiEndpoint} onChange={(e) => setDeployForm((s) => ({ ...s, apiEndpoint: e.target.value }))} />
              <select className="rounded border px-3 py-2" value={deployForm.apiType} onChange={(e) => setDeployForm((s) => ({ ...s, apiType: e.target.value }))}>
                <option value="chat">chat</option>
                <option value="messages">messages</option>
              </select>
              <input className="rounded border px-3 py-2" placeholder="模型" value={deployForm.model} onChange={(e) => setDeployForm((s) => ({ ...s, model: e.target.value }))} />
              <input className="rounded border px-3 py-2" placeholder="API Key" type="password" value={deployForm.apiKey} onChange={(e) => setDeployForm((s) => ({ ...s, apiKey: e.target.value }))} />
            </div>
            <div className="mt-3 flex items-center gap-3 text-sm text-muted-foreground">
              <span>默认 CPU: {settings.defaultCpu}</span>
              <span>默认内存: {settings.defaultMemory}</span>
              <span>目标服务端: {selectedServerName || "未选择"}</span>
            </div>
            <Button className="mt-4" onClick={deploy}>一键部署 / 更新并重启</Button>
          </section>

          <section className="rounded border p-4">
            <div className="mb-3 text-lg font-semibold">部署列表</div>
            <div className="space-y-2">
              {deployments.map((item) => (
                <button key={item.id} className="flex w-full items-center justify-between rounded border px-3 py-2 text-left" onClick={() => setSelectedDeploymentId(item.id)}>
                  <div>
                    <div className="font-medium">{item.serverName} · {item.model}</div>
                    <div className="text-xs text-muted-foreground">{item.apiType} · {item.apiEndpoint}</div>
                  </div>
                  <div className="text-xs text-muted-foreground">{item.userName}</div>
                </button>
              ))}
            </div>
          </section>

          {me.role === "Admin" ? (
            <>
              <section className="rounded border p-4">
                <div className="mb-3 text-lg font-semibold">用户管理</div>
                <div className="grid gap-3 md:grid-cols-4">
                  <input className="rounded border px-3 py-2" placeholder="用户名" value={adminUserForm.userName} onChange={(e) => setAdminUserForm((s) => ({ ...s, userName: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="显示名" value={adminUserForm.displayName} onChange={(e) => setAdminUserForm((s) => ({ ...s, displayName: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="密码" type="password" value={adminUserForm.password} onChange={(e) => setAdminUserForm((s) => ({ ...s, password: e.target.value }))} />
                  <select className="rounded border px-3 py-2" value={adminUserForm.role} onChange={(e) => setAdminUserForm((s) => ({ ...s, role: e.target.value }))}>
                    <option value="Employee">员工</option>
                    <option value="Admin">管理员</option>
                  </select>
                </div>
                <Button className="mt-3" onClick={createUser}>创建用户</Button>
                <pre className="mt-3 overflow-auto rounded bg-muted p-3 text-xs">{formatJson(adminUsers)}</pre>
              </section>

              <section className="rounded border p-4">
                <div className="mb-3 text-lg font-semibold">沙盒服务端</div>
                <div className="grid gap-3 md:grid-cols-2">
                  <input className="rounded border px-3 py-2" placeholder="名称" value={adminServerForm.name} onChange={(e) => setAdminServerForm((s) => ({ ...s, name: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="BaseUrl" value={adminServerForm.baseUrl} onChange={(e) => setAdminServerForm((s) => ({ ...s, baseUrl: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="ApiToken" value={adminServerForm.apiToken} onChange={(e) => setAdminServerForm((s) => ({ ...s, apiToken: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="持久化根目录" value={adminServerForm.persistentRootPath} onChange={(e) => setAdminServerForm((s) => ({ ...s, persistentRootPath: e.target.value }))} />
                </div>
                <Button className="mt-3" onClick={saveServer}>保存服务端</Button>
                <div className="mt-3 space-y-2">
                  {servers.map((server) => (
                    <div key={server.id} className="flex items-center justify-between rounded border px-3 py-2">
                      <div>
                        <div className="font-medium">{server.name}</div>
                        <div className="text-xs text-muted-foreground">{server.baseUrl} · {server.healthStatus}</div>
                      </div>
                      <Button onClick={() => checkServer(server.id)}>健康检查</Button>
                    </div>
                  ))}
                </div>
              </section>

              <section className="rounded border p-4">
                <div className="mb-3 text-lg font-semibold">模板与版本</div>
                <div className="grid gap-3 md:grid-cols-3">
                  <input className="rounded border px-3 py-2" placeholder="模板名" value={adminTemplateForm.name} onChange={(e) => setAdminTemplateForm((s) => ({ ...s, name: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="描述" value={adminTemplateForm.description} onChange={(e) => setAdminTemplateForm((s) => ({ ...s, description: e.target.value }))} />
                  <Button onClick={saveTemplate}>创建模板</Button>
                </div>
                <div className="mt-3 grid gap-3 md:grid-cols-4">
                  <select className="rounded border px-3 py-2" value={adminVersionForm.templateId} onChange={(e) => setAdminVersionForm((s) => ({ ...s, templateId: e.target.value }))}>
                    <option value="">选择模板</option>
                    {templates.map((template) => <option key={template.id} value={template.id}>{template.name}</option>)}
                  </select>
                  <input className="rounded border px-3 py-2" placeholder="版本" value={adminVersionForm.version} onChange={(e) => setAdminVersionForm((s) => ({ ...s, version: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="镜像" value={adminVersionForm.image} onChange={(e) => setAdminVersionForm((s) => ({ ...s, image: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="端口" type="number" value={adminVersionForm.containerPort} onChange={(e) => setAdminVersionForm((s) => ({ ...s, containerPort: Number(e.target.value) }))} />
                  <input className="rounded border px-3 py-2 md:col-span-2" placeholder="命令 JSON" value={adminVersionForm.command} onChange={(e) => setAdminVersionForm((s) => ({ ...s, command: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="配置挂载目录" value={adminVersionForm.configMountPath} onChange={(e) => setAdminVersionForm((s) => ({ ...s, configMountPath: e.target.value }))} />
                  <input className="rounded border px-3 py-2" placeholder="配置文件名" value={adminVersionForm.configFileName} onChange={(e) => setAdminVersionForm((s) => ({ ...s, configFileName: e.target.value }))} />
                  <input className="rounded border px-3 py-2 md:col-span-2" placeholder="数据挂载目录" value={adminVersionForm.workspaceMountPath} onChange={(e) => setAdminVersionForm((s) => ({ ...s, workspaceMountPath: e.target.value }))} />
                </div>
                <Button className="mt-3" onClick={saveTemplateVersion}>发布模板版本</Button>
                <pre className="mt-3 overflow-auto rounded bg-muted p-3 text-xs">{formatJson(templates)}</pre>
              </section>

              <section className="rounded border p-4">
                <div className="mb-3 text-lg font-semibold">系统设置</div>
                <div className="grid gap-3 md:grid-cols-3">
                  <input className="rounded border px-3 py-2" value={settings.defaultCpu} onChange={(e) => setSettings((s) => ({ ...s, defaultCpu: e.target.value }))} />
                  <input className="rounded border px-3 py-2" value={settings.defaultMemory} onChange={(e) => setSettings((s) => ({ ...s, defaultMemory: e.target.value }))} />
                  <input className="rounded border px-3 py-2" type="number" value={settings.defaultLogTailLines} onChange={(e) => setSettings((s) => ({ ...s, defaultLogTailLines: Number(e.target.value) }))} />
                </div>
                <Button className="mt-3" onClick={saveSettings}>保存设置</Button>
              </section>
            </>
          ) : null}
        </div>

        <div className="space-y-4">
          <section className="rounded border p-4">
            <div className="mb-3 text-lg font-semibold">实例详情</div>
            {selectedDeployment ? (
              <div className="space-y-2 text-sm">
                <div>状态：{selectedDeployment.status || "未知"}</div>
                <div>容器 ID：{selectedDeployment.containerId || "-"}</div>
                <div>CPU：{selectedDeployment.cpuPercent ?? "-"}%</div>
                <div>内存：{selectedDeployment.memoryPercent ?? "-"}% ({selectedDeployment.memoryUsage || "-"} / {selectedDeployment.memoryLimit || "-"})</div>
                <div>创建时间：{selectedDeployment.createdAt || "-"}</div>
                <div>配置文件：{selectedDeployment.configFilePath}</div>
                <div>持久化目录：{selectedDeployment.persistentDirectory}</div>
                <pre className="overflow-auto rounded bg-muted p-3 text-xs">{formatJson(selectedDeployment.templateSnapshot)}</pre>
              </div>
            ) : <div className="text-sm text-muted-foreground">先选择一个部署</div>}
          </section>

          <section className="rounded border p-4">
            <div className="mb-3 flex items-center justify-between text-lg font-semibold">
              <span>日志</span>
              <Button onClick={connectLogs}>连接实时日志</Button>
            </div>
            <pre className="h-72 overflow-auto rounded bg-black p-3 text-xs text-green-400">{logs.join("\n")}</pre>
          </section>

          <section className="rounded border p-4">
            <div className="mb-3 flex items-center justify-between text-lg font-semibold">
              <span>终端</span>
              <Button onClick={connectTerminal}>连接终端</Button>
            </div>
            <pre className="h-72 overflow-auto rounded bg-black p-3 text-xs text-white">{terminalLines.join("")}</pre>
            <div className="mt-3 flex gap-2">
              <input className="flex-1 rounded border px-3 py-2" placeholder="输入命令并发送" value={terminalInput} onChange={(e) => setTerminalInput(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") sendTerminal() }} />
              <Button onClick={sendTerminal}>发送</Button>
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}

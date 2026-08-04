# Industrial Platform Gateway Host

`src/Industrial.Gateway.Host` 是 Industrial Platform 的宿主程序，已经与 Aneiang.Yarp 放在同一个 Git 仓库中。

启动：

```powershell
dotnet run --project .\src\Industrial.Gateway.Host
```

默认地址：`http://localhost:5202`

主要入口：

- `/platform`：YARP 控制台
- `/platform/security`：IAM 安全中心
- `/api/iam/**`：通过 Gateway 访问 IAM
- `/api/mes/**`：MOL
- `/api/iot/**`：IoTSharp
- `/api/wcs/**`：WCS ENG
- `/health/ready`：Gateway 自身健康检查

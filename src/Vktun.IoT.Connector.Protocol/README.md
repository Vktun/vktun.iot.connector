# Vktun.IoT.Connector.Protocol

工业协议解析器库。当前已验证路径聚焦 Modbus RTU/TCP、自定义协议和模板兼容性回归；S7/IEC104 为受限可用，OPC UA/BACnet/CANopen 为实验性实现。

## 安装

```bash
dotnet add package Vktun.IoT.Connector.Protocol
```

## 功能

- Modbus RTU/TCP 协议解析
- IEC104 协议解析
- S7 协议解析
- 自定义协议支持

## 当前成熟度

| 协议 | 状态 | 说明 |
| --- | --- | --- |
| Modbus RTU/TCP | 已验证完整 | 解析、打包、模板和测试闭环相对完整。 |
| 自定义协议 | 受限可用 | JSON 驱动解析可用，仍需真实样例报文验证。 |
| S7 | 受限可用 | 需要使用专用读写命令入口，不应依赖通用 `BuildRequest`。 |
| IEC104 | 受限可用 | 解析入口存在，但完整指令建模仍有限。 |
| OPC UA / BACnet / CANopen | 实验性 | 当前在代码中存在，但不应按生产级协议支持宣称。 |

## 接入与模板文档

- [新增协议标准接入指南](../../Docs/新增协议标准接入指南.md)
- [协议模板字段说明](../../Docs/协议模板字段说明.md)
- [实现状态与桩功能说明](../../Docs/实现状态与桩功能说明.md)

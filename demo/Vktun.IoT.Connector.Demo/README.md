# Vktun.IoT.Connector.Demo

Console demos for quick local validation of SDK acquisition paths.

## Quick Local Loop

Start DeviceMock on development-friendly high ports:

```powershell
dotnet run --project demo\Vktun.IoT.Connector.DeviceMock -- --modbus-port 1502 --s7-port 1102
```

In another terminal, run the SDK collector demo against the mock Modbus TCP server:

```powershell
dotnet run --project demo\Vktun.IoT.Connector.Demo -- collector 127.0.0.1 1502
```

The demo should connect to `127.0.0.1:1502` and collect points from `MODBUS_TCP_DEMO`.

## Commands

```powershell
dotnet run --project demo\Vktun.IoT.Connector.Demo -- --help
dotnet run --project demo\Vktun.IoT.Connector.Demo -- collector [ip] [port]
dotnet run --project demo\Vktun.IoT.Connector.Demo -- modbus-tcp [ip] [port] [slaveId]
dotnet run --project demo\Vktun.IoT.Connector.Demo -- s7 [ip] [rack] [slot]
dotnet run --project demo\Vktun.IoT.Connector.Demo -- serial [portName] [baudRate]
```

## Notes

- `collector` is the recommended minimal SDK path for local verification.
- `modbus-tcp`, `s7`, and `serial` are protocol-specific console test utilities.
- If you use the DeviceMock default ports `502` and `102`, your environment may require administrator privileges or firewall changes.
- DeviceMock is for development and regression validation, not a production gateway service.

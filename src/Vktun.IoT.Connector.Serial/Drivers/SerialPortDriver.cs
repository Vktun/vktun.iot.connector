using System.Runtime.InteropServices;
using Vktun.IoT.Connector.Core.Interfaces;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Serial.Drivers;

public interface ISerialPortDriver : IAsyncDisposable
{
    string PortName { get; }
    int BaudRate { get; }
    bool IsOpen { get; }
    
    Task<bool> OpenAsync();
    Task CloseAsync();
    Task<int> WriteAsync(byte[] data, int offset, int count);
    Task<int> ReadAsync(byte[] buffer, int offset, int count);
    void DiscardInBuffer();
    void DiscardOutBuffer();
}

public class SerialPortDriver : ISerialPortDriver
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly int _dataBits;
    private readonly Parity _parity;
    private readonly StopBits _stopBits;
    private readonly int _readTimeout;
    private readonly int _writeTimeout;
    private readonly ILogger _logger;
    
    private IntPtr _handle;
    private bool _isOpen;
    private bool _isDisposed;

    public string PortName => _portName;
    public int BaudRate => _baudRate;
    public bool IsOpen => _isOpen;

    public SerialPortDriver(
        string portName,
        int baudRate,
        IConfigurationProvider configProvider,
        ILogger logger,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One)
    {
        _portName = portName;
        _baudRate = baudRate;
        _dataBits = dataBits;
        _parity = parity;
        _stopBits = stopBits;
        _logger = logger;
        
        var config = configProvider.GetConfig();
        _readTimeout = config.Serial.ReadWriteTimeout;
        _writeTimeout = config.Serial.ReadWriteTimeout;
    }

    public Task<bool> OpenAsync()
    {
        if (_isOpen)
        {
            return Task.FromResult(true);
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _handle = NativeMethods.CreateFile(
                    $"\\\\.\\{_portName}",
                    NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    NativeMethods.FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (_handle == NativeMethods.INVALID_HANDLE_VALUE)
                {
                    throw new IOException($"无法打开串口: {_portName}");
                }

                var dcb = new NativeMethods.DCB();
                dcb.DCBlength = Marshal.SizeOf(typeof(NativeMethods.DCB));
                dcb.BaudRate = (uint)_baudRate;
                dcb.ByteSize = (byte)_dataBits;
                dcb.Parity = (byte)_parity;
                dcb.StopBits = (byte)_stopBits;

                NativeMethods.SetCommState(_handle, ref dcb);
            }
            
            _isOpen = true;
            _logger.Info($"串口打开成功: {_portName}, 波特率: {_baudRate}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"串口打开失败: {ex.Message}", ex);
            return Task.FromResult(false);
        }
    }

    public Task CloseAsync()
    {
        if (!_isOpen)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _handle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            
            _isOpen = false;
            _logger.Info($"串口已关闭: {_portName}");
        }
        catch (Exception ex)
        {
            _logger.Error($"串口关闭失败: {ex.Message}", ex);
        }
        
        return Task.CompletedTask;
    }

    public async Task<int> WriteAsync(byte[] data, int offset, int count)
    {
        if (!_isOpen)
        {
            return 0;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var bytesWritten = 0u;
                NativeMethods.WriteFile(_handle, data, (uint)count, out bytesWritten, IntPtr.Zero);
                return (int)bytesWritten;
            }

            throw new PlatformNotSupportedException("Serial port is only supported on Windows");
        }
        catch (Exception ex)
        {
            _logger.Error($"串口写入失败: {ex.Message}", ex);
            return 0;
        }
    }

    public async Task<int> ReadAsync(byte[] buffer, int offset, int count)
    {
        if (!_isOpen)
        {
            return 0;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var bytesRead = 0u;
                NativeMethods.ReadFile(_handle, buffer, (uint)count, out bytesRead, IntPtr.Zero);
                return (int)bytesRead;
            }

            throw new PlatformNotSupportedException("Serial port is only supported on Windows");
        }
        catch (Exception ex)
        {
            _logger.Error($"串口读取失败: {ex.Message}", ex);
            return 0;
        }
    }

    public void DiscardInBuffer()
    {
        if (_isOpen && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NativeMethods.PurgeComm(_handle, NativeMethods.PURGE_RXCLEAR);
        }
    }

    public void DiscardOutBuffer()
    {
        if (_isOpen && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            NativeMethods.PurgeComm(_handle, NativeMethods.PURGE_TXCLEAR);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        await CloseAsync();
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

public enum Parity
{
    None = 0,
    Odd = 1,
    Even = 2,
    Mark = 3,
    Space = 4
}

public enum StopBits
{
    One = 0,
    OnePointFive = 1,
    Two = 2
}

internal static class NativeMethods
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    
    public const uint PURGE_RXCLEAR = 0x0008;
    public const uint PURGE_TXCLEAR = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetCommState(IntPtr hFile, ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PurgeComm(IntPtr hFile, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct DCB
    {
        public int DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }
}

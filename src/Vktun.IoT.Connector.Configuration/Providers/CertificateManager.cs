using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Vktun.IoT.Connector.Core.Interfaces;

namespace Vktun.IoT.Connector.Configuration.Providers;

/// <summary>
/// 证书管理器 - 负责TLS证书的加载、验证和轮换
/// </summary>
public class CertificateManager : IDisposable
{
    private readonly ILogger _logger;
    private X509Certificate2? _serverCertificate;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private readonly string _certificatePath;
    private readonly string _certificatePassword;

    public CertificateManager(string certificatePath, string certificatePassword, ILogger logger)
    {
        _certificatePath = certificatePath ?? throw new ArgumentNullException(nameof(certificatePath));
        _certificatePassword = certificatePassword ?? throw new ArgumentNullException(nameof(certificatePassword));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadCertificate();
        SetupFileWatcher();
    }

    /// <summary>
    /// 获取当前服务器证书
    /// </summary>
    public X509Certificate2? ServerCertificate
    {
        get
        {
            lock (_lock)
            {
                return _serverCertificate;
            }
        }
    }

    /// <summary>
    /// 加载证书
    /// </summary>
    private void LoadCertificate()
    {
        try
        {
            if (!File.Exists(_certificatePath))
            {
                _logger.Warning($"Certificate file not found: {_certificatePath}");
                return;
            }

            var certificate = new X509Certificate2(
                _certificatePath,
                _certificatePassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

            // 验证证书有效期
            if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
            {
                _logger.Warning($"Certificate is expired or not yet valid. NotBefore: {certificate.NotBefore}, NotAfter: {certificate.NotAfter}");
                certificate.Dispose();
                return;
            }

            lock (_lock)
            {
                _serverCertificate?.Dispose();
                _serverCertificate = certificate;
            }

            _logger.Info($"Certificate loaded successfully. Subject: {certificate.Subject}, Expiry: {certificate.NotAfter}");
        }
        catch (CryptographicException ex)
        {
            _logger.Error($"Failed to load certificate: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 设置文件监视器，支持证书自动轮换
    /// </summary>
    private void SetupFileWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_certificatePath);
            if (string.IsNullOrEmpty(directory))
                return;

            _watcher = new FileSystemWatcher(directory)
            {
                Filter = Path.GetFileName(_certificatePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _watcher.Changed += (sender, e) =>
            {
                _logger.Info("Certificate file changed, reloading...");
                // 延迟加载，避免文件写入未完成
                Task.Delay(1000).ContinueWith(_ => LoadCertificate());
            };

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to setup certificate file watcher: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证客户端证书
    /// </summary>
    public bool ValidateClientCertificate(X509Certificate2? clientCertificate)
    {
        if (clientCertificate == null)
            return false;

        try
        {
            // 检查证书有效期
            if (DateTime.Now < clientCertificate.NotBefore || DateTime.Now > clientCertificate.NotAfter)
            {
                _logger.Warning($"Client certificate expired or not yet valid: {clientCertificate.Subject}");
                return false;
            }

            // 检查证书是否被吊销（可选，需要配置CRL）
            // TODO: 实现CRL检查

            _logger.Debug($"Client certificate validated: {clientCertificate.Subject}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Client certificate validation failed: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 生成自签名证书（用于开发/测试环境）
    /// </summary>
    public static X509Certificate2 GenerateSelfSignedCertificate(
        string subjectName,
        int validityDays = 365,
        string? password = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // TLS Web Server Authentication
                false));

        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(validityDays));

        if (!string.IsNullOrEmpty(password))
        {
            var bytes = certificate.Export(X509ContentType.Pfx, password);
            return new X509Certificate2(bytes, password, X509KeyStorageFlags.MachineKeySet);
        }

        return certificate;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        lock (_lock)
        {
            _serverCertificate?.Dispose();
        }
    }
}

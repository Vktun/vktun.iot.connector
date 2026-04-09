namespace Vktun.IoT.Connector.Core.Models;

/// <summary>
/// 认证配置
/// </summary>
public class AuthenticationConfig
{
    /// <summary>
    /// 是否启用认证
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 认证方式：ApiKey, Certificate, Token
    /// </summary>
    public AuthMethod Method { get; set; } = AuthMethod.ApiKey;

    /// <summary>
    /// API Key 列表（当 Method = ApiKey 时使用）
    /// </summary>
    public List<ApiKeyCredential> ApiKeys { get; set; } = new();

    /// <summary>
    /// IP 白名单（为空表示不限制）
    /// </summary>
    public List<string> IpWhitelist { get; set; } = new();

    /// <summary>
    /// IP 黑名单
    /// </summary>
    public List<string> IpBlacklist { get; set; } = new();

    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 1000;

    /// <summary>
    /// 连接速率限制（每秒新连接数，0表示不限制）
    /// </summary>
    public int ConnectionRateLimit { get; set; } = 0;

    /// <summary>
    /// TLS 配置
    /// </summary>
    public TlsConfig? Tls { get; set; }
}

/// <summary>
/// 认证方式
/// </summary>
public enum AuthMethod
{
    /// <summary>
    /// 无认证
    /// </summary>
    None,

    /// <summary>
    /// API Key 认证
    /// </summary>
    ApiKey,

    /// <summary>
    /// 客户端证书认证
    /// </summary>
    Certificate,

    /// <summary>
    /// JWT Token 认证
    /// </summary>
    Token
}

/// <summary>
/// API Key 凭证
/// </summary>
public class ApiKeyCredential
{
    /// <summary>
    /// API Key（加密存储）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 密钥描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 关联的设备ID列表（为空表示可访问所有设备）
    /// </summary>
    public List<string> AllowedDevices { get; set; } = new();

    /// <summary>
    /// 过期时间（null表示永不过期）
    /// </summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// TLS 配置
/// </summary>
public class TlsConfig
{
    /// <summary>
    /// 是否启用 TLS
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 证书文件路径（.pfx 或 .pem）
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// 证书密码
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>
    /// 是否要求客户端证书
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>
    /// 允许的 TLS 版本（默认 TLS 1.2+）
    /// </summary>
    public List<TlsVersion> AllowedVersions { get; set; } = new()
    {
        TlsVersion.Tls12,
        TlsVersion.Tls13
    };

    /// <summary>
    /// 加密套件（为空则使用系统默认）
    /// </summary>
    public List<string> CipherSuites { get; set; } = new();

    /// <summary>
    /// 是否允许不安全的证书（仅用于开发环境，默认 false）
    /// </summary>
    public bool AllowInsecureCertificate { get; set; } = false;

    /// <summary>
    /// 是否检查证书吊销状态（默认 true）
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;
}

/// <summary>
/// TLS 版本
/// </summary>
public enum TlsVersion
{
    Tls10,
    Tls11,
    Tls12,
    Tls13
}

/// <summary>
/// 认证结果
/// </summary>
public class AuthenticationResult
{
    /// <summary>
    /// 是否认证成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 认证的设备ID
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// 会话令牌
    /// </summary>
    public string? SessionToken { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AuthenticationResult CreateSuccess(string deviceId, string? sessionToken = null)
    {
        return new AuthenticationResult
        {
            Success = true,
            DeviceId = deviceId,
            SessionToken = sessionToken
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AuthenticationResult CreateFailure(string reason)
    {
        return new AuthenticationResult
        {
            Success = false,
            FailureReason = reason
        };
    }
}

/// <summary>
/// 加密配置
/// </summary>
public class EncryptionConfig
{
    /// <summary>
    /// 是否启用配置加密
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 加密算法：AES256
    /// </summary>
    public string Algorithm { get; set; } = "AES256";

    /// <summary>
    /// 加密密钥（Base64编码，建议从环境变量读取）
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 需要加密的配置项路径
    /// </summary>
    public List<string> EncryptedFields { get; set; } = new()
    {
        "Tls.CertificatePassword",
        "ApiKeys[].Key"
    };
}

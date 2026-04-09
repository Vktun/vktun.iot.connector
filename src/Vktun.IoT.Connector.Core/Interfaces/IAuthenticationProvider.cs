using System.Net;
using Vktun.IoT.Connector.Core.Models;

namespace Vktun.IoT.Connector.Core.Interfaces
{
    /// <summary>
    /// 认证提供者接口
    /// </summary>
    public interface IAuthenticationProvider
    {
        /// <summary>
        /// 验证连接请求
        /// </summary>
        /// <param name="remoteEndPoint">远程端点</param>
        /// <param name="credentials">凭证信息（API Key、证书等）</param>
        /// <returns>认证结果</returns>
        Task<AuthenticationResult> AuthenticateAsync(IPEndPoint remoteEndPoint, Dictionary<string, string>? credentials = null);

        /// <summary>
        /// 验证设备是否有权限访问指定资源
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="resource">资源标识</param>
        /// <param name="authenticatedIdentity">已认证的身份标识（API Key等）</param>
        /// <returns>是否授权</returns>
        Task<bool> AuthorizeAsync(string deviceId, string resource, string? authenticatedIdentity = null);

        /// <summary>
        /// 检查IP是否在白名单中
        /// </summary>
        /// <param name="ipAddress">IP地址</param>
        /// <returns>是否允许</returns>
        bool IsIpAllowed(IPAddress ipAddress);

        /// <summary>
        /// 检查连接速率是否超限
        /// </summary>
        /// <param name="remoteEndPoint">远程端点</param>
        /// <returns>是否允许连接</returns>
        bool CheckConnectionRateLimit(IPEndPoint remoteEndPoint);

        /// <summary>
        /// 获取当前活跃连接数
        /// </summary>
        int ActiveConnectionCount { get; }

        /// <summary>
        /// 注册新连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        /// <param name="remoteEndPoint">远程端点</param>
        void RegisterConnection(string connectionId, IPEndPoint remoteEndPoint);

        /// <summary>
        /// 注销连接
        /// </summary>
        /// <param name="connectionId">连接ID</param>
        void UnregisterConnection(string connectionId);
    }
}

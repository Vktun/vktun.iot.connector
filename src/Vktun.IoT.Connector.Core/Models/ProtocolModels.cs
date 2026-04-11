using System.Text.Json;
using System.Text.Json.Serialization;
using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models
{
    public class ProtocolConfig
    {
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; }
        public string ProtocolVersion { get; set; } = "1.0.0";
        public string ChannelId { get; set; } = string.Empty;
        public string Vendor { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string TemplateSource { get; set; } = string.Empty;
        public string DefinitionJson { get; set; } = string.Empty;
        public Dictionary<string, string> ParseRules { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, object?> AdditionalSettings { get; set; } = new Dictionary<string, object?>();
        public List<PointConfig> Points { get; set; } = new List<PointConfig>();
        public int ConfigVersion { get; set; } = 1;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public T? GetDefinition<T>()
        {
            if (!string.IsNullOrWhiteSpace(DefinitionJson))
            {
                return JsonSerializer.Deserialize<T>(DefinitionJson, SerializerOptions);
            }

            return MigrateFromParseRules<T>();
        }

        public void SetDefinition<T>(T definition)
        {
            DefinitionJson = JsonSerializer.Serialize(definition, SerializerOptions);
        }

        public ProtocolConfigValidationResult Validate()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(ProtocolId))
            {
                errors.Add("ProtocolId is required.");
            }

            if (string.IsNullOrWhiteSpace(ProtocolName))
            {
                errors.Add("ProtocolName is required.");
            }

            if (string.IsNullOrWhiteSpace(DefinitionJson) && ParseRules.Count == 0)
            {
                warnings.Add("Neither DefinitionJson nor ParseRules is set. Protocol parsing may not work correctly.");
            }

            if (!string.IsNullOrWhiteSpace(DefinitionJson) && ParseRules.Count > 0)
            {
                warnings.Add("Both DefinitionJson and ParseRules are set. DefinitionJson takes precedence.");
            }

            if (Points.Count == 0)
            {
                warnings.Add("No data points are configured.");
            }

            foreach (var point in Points)
            {
                if (string.IsNullOrWhiteSpace(point.PointName))
                {
                    errors.Add($"Point at offset {point.Offset} has no PointName.");
                }

                if (point.Length <= 0)
                {
                    errors.Add($"Point '{point.PointName}' has invalid Length: {point.Length}.");
                }

                if (point.MinValue > point.MaxValue && point.MinValue != double.MinValue && point.MaxValue != double.MaxValue)
                {
                    warnings.Add($"Point '{point.PointName}' has MinValue > MaxValue.");
                }
            }

            if (ProtocolType == ProtocolType.ModbusRtu || ProtocolType == ProtocolType.ModbusTcp)
            {
                ValidateModbusConfig(errors, warnings);
            }
            else if (ProtocolType == ProtocolType.S7)
            {
                ValidateS7Config(errors, warnings);
            }
            else if (ProtocolType == ProtocolType.Custom)
            {
                ValidateCustomConfig(errors, warnings);
            }

            return new ProtocolConfigValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Warnings = warnings
            };
        }

        private T? MigrateFromParseRules<T>()
        {
            if (ParseRules.TryGetValue("CustomProtocolJson", out var customJson) && !string.IsNullOrWhiteSpace(customJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(customJson, SerializerOptions);
                }
                catch
                {
                    return default;
                }
            }

            if (ParseRules.TryGetValue("ModbusConfig", out var modbusJson) && !string.IsNullOrWhiteSpace(modbusJson))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(modbusJson, SerializerOptions);
                }
                catch
                {
                    return default;
                }
            }

            if (ParseRules.TryGetValue("S7Config", out var s7Json) && !string.IsNullOrWhiteSpace(s7Json))
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(s7Json, SerializerOptions);
                }
                catch
                {
                    return default;
                }
            }

            return default;
        }

        private void ValidateModbusConfig(List<string> errors, List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(DefinitionJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(DefinitionJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("SlaveId", out var slaveIdProp))
                    {
                        var slaveId = slaveIdProp.GetInt32();
                        if (slaveId < 1 || slaveId > 247)
                        {
                            warnings.Add($"Modbus SlaveId {slaveId} is outside the typical range (1-247).");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid ModbusConfig JSON in DefinitionJson: {ex.Message}");
                }
            }
        }

        private void ValidateS7Config(List<string> errors, List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(DefinitionJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(DefinitionJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Rack", out var rackProp) && rackProp.GetInt32() < 0)
                    {
                        errors.Add("S7 Rack value must be non-negative.");
                    }

                    if (root.TryGetProperty("Slot", out var slotProp) && slotProp.GetInt32() < 0)
                    {
                        errors.Add("S7 Slot value must be non-negative.");
                    }
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid S7Config JSON in DefinitionJson: {ex.Message}");
                }
            }
        }

        private void ValidateCustomConfig(List<string> errors, List<string> warnings)
        {
            if (!string.IsNullOrWhiteSpace(DefinitionJson))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<CustomProtocolConfig>(DefinitionJson, SerializerOptions);
                    if (config != null)
                    {
                        if (config.FrameType == FrameType.FixedLength && config.FrameLength?.FixedLength <= 0)
                        {
                            errors.Add("FixedLength frame type requires a positive FixedLength value.");
                        }

                        if (config.FrameHeader?.Value == null || config.FrameHeader.Value.Length == 0)
                        {
                            warnings.Add("Custom protocol has no frame header configured. Frame detection may be unreliable.");
                        }

                        if (config.FrameCheck?.CheckType != CheckType.None && config.FrameLength == null)
                        {
                            warnings.Add("Frame check is configured but frame length is not. Checksum validation may not work correctly.");
                        }
                    }
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid CustomProtocolConfig JSON in DefinitionJson: {ex.Message}");
                }
            }
        }
    }

    public class ProtocolConfigValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class PointConfig
    {
        public string PointName { get; set; } = string.Empty;
        public int Offset { get; set; }
        public int Length { get; set; } = 1;
        public DataType DataType { get; set; }
        public double Ratio { get; set; } = 1.0;
        public double OffsetValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;
        public bool IsReadOnly { get; set; } = true;
        public string Description { get; set; } = string.Empty;
    }

    public class CustomProtocolConfig
    {
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public FrameType FrameType { get; set; }
        public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
        public FrameHeaderConfig? FrameHeader { get; set; }
        public FrameLengthConfig? FrameLength { get; set; }
        public FrameDeviceIdConfig? DeviceId { get; set; }
        public FrameCheckConfig? FrameCheck { get; set; }
        public FrameTailConfig? FrameTail { get; set; }
        public List<PointConfig> Points { get; set; } = new();
    }

    public class FrameHeaderConfig
    {
        public byte[]? Value { get; set; }
        public int Length { get; set; }
    }

    public class FrameLengthConfig
    {
        public int Offset { get; set; }
        public int Length { get; set; } = 1;
        public string CalcRule { get; set; } = "Self";
        public int FixedLength { get; set; }
    }

    public class FrameDeviceIdConfig
    {
        public int Offset { get; set; }
        public int Length { get; set; } = 1;
        public DataType DataType { get; set; } = DataType.UInt16;
    }

    public class FrameCheckConfig
    {
        public CheckType CheckType { get; set; }
        public int CheckStartOffset { get; set; }
        public int CheckEndOffset { get; set; }
    }

    public class FrameTailConfig
    {
        public byte[]? Value { get; set; }
        public int Length { get; set; }
    }
}

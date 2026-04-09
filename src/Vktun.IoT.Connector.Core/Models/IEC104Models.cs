using Vktun.IoT.Connector.Core.Enums;

namespace Vktun.IoT.Connector.Core.Models
{
    public enum IEC104TypeIdentification : byte
    {
        M_SP_NA_1 = 1,
        M_SP_TA_1 = 2,
        M_DP_NA_1 = 3,
        M_DP_TA_1 = 4,
        M_ST_NA_1 = 5,
        M_ST_TA_1 = 6,
        M_BO_NA_1 = 7,
        M_BO_TA_1 = 8,
        M_ME_NA_1 = 9,
        M_ME_TA_1 = 10,
        M_ME_NB_1 = 11,
        M_ME_TB_1 = 12,
        M_ME_NC_1 = 13,
        M_ME_TC_1 = 14,
        M_IT_NA_1 = 15,
        M_IT_TA_1 = 16,
        M_EP_TA_1 = 17,
        M_EP_TB_1 = 18,
        M_EP_TC_1 = 19,
        M_PS_NA_1 = 20,
        M_ME_ND_1 = 21,
        M_SP_TB_1 = 30,
        M_DP_TB_1 = 31,
        M_ST_TB_1 = 32,
        M_BO_TB_1 = 33,
        M_ME_TD_1 = 34,
        M_ME_TE_1 = 35,
        M_ME_TF_1 = 36,
        M_IT_TB_1 = 37,
        M_EP_TD_1 = 38,
        M_EP_TE_1 = 39,
        M_EP_TF_1 = 40,
        C_SC_NA_1 = 45,
        C_DC_NA_1 = 46,
        C_RC_NA_1 = 47,
        C_SE_NA_1 = 48,
        C_SE_NB_1 = 49,
        C_SE_NC_1 = 50,
        C_BO_NA_1 = 51,
        C_SC_TA_1 = 58,
        C_DC_TA_1 = 59,
        C_RC_TA_1 = 60,
        C_SE_TA_1 = 61,
        C_SE_TB_1 = 62,
        C_SE_TC_1 = 63,
        C_BO_TA_1 = 64,
        M_EI_NA_1 = 70,
        CI_NA_1 = 100,
        CI_NU_1 = 101,
        CI_ND_1 = 102,
        CS_NA_1 = 103,
        CS_NU_1 = 104,
        CS_ND_1 = 105,
        F_FR_NA_1 = 110,
        F_SR_NA_1 = 111,
        F_SC_NA_1 = 112,
        F_LS_NA_1 = 113,
        F_FA_NA_1 = 114,
        P_NA_1 = 120,
        P_NU_1 = 121,
        P_ME_NA_1 = 122,
        P_ME_NB_1 = 123,
        P_ME_NC_1 = 124,
        P_AC_NA_1 = 125,
        U_FR_NA_1 = 130,
        U_SR_NA_1 = 131,
        U_UC_NA_1 = 132,
        U_UR_NA_1 = 133,
        U_UD_NA_1 = 134,
        U_US_NA_1 = 135,
        U_UQ_NA_1 = 136,
        U_UR_NB_1 = 137,
        U_UD_NB_1 = 138
    }

    public enum IEC104CauseOfTransmission : byte
    {
        Per_Cycle = 1,
        Background = 2,
        Spontaneous = 3,
        Initialized = 4,
        Request = 5,
        Activation = 6,
        Activation_Con = 7,
        Deactivation = 8,
        Deactivation_Con = 9,
        Activation_Term = 10,
        Retr_Trans_Rmt = 11,
        Retr_Trans_Local = 12,
        File_Transfer = 13,
        Unknown_Type = 14,
        Unknown_Cause = 15,
        Test_Command = 16,
        Test_Command_Con = 17,
        Reset_Command = 18,
        Reset_Command_Con = 19
    }

    public enum IEC104QualifierOfInterrogation : byte
    {
        Not_Used = 0,
        Station_Interrogation = 20,
        Group_1_Interrogation = 21,
        Group_2_Interrogation = 22,
        Group_3_Interrogation = 23,
        Group_4_Interrogation = 24,
        Group_5_Interrogation = 25,
        Group_6_Interrogation = 26,
        Group_7_Interrogation = 27,
        Group_8_Interrogation = 28,
        Group_9_Interrogation = 29,
        Group_10_Interrogation = 30,
        Group_11_Interrogation = 31,
        Group_12_Interrogation = 32,
        Group_13_Interrogation = 33,
        Group_14_Interrogation = 34,
        Group_15_Interrogation = 35,
        Group_16_Interrogation = 36
    }

    public enum IEC104DoublePointValue : byte
    {
        Intermediate = 0,
        Off = 1,
        On = 2,
        Indeterminate = 3
    }

    public enum IEC104SinglePointValue : byte
    {
        Off = 0,
        On = 1
    }

    public class IEC104Config
    {
        public string ProtocolId { get; set; } = string.Empty;
        public string ProtocolName { get; set; } = "IEC104";
        public string Description { get; set; } = string.Empty;
        public ProtocolType ProtocolType { get; set; } = ProtocolType.IEC104;
        public int CommonAddress { get; set; } = 1;
        public int Port { get; set; } = 2404;
        public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
        public WordOrder WordOrder { get; set; } = WordOrder.HighWordFirst;
        public List<IEC104PointConfig> Points { get; set; } = new List<IEC104PointConfig>();
        public int ResponseTimeout { get; set; } = 5000;
        public int ConnectionTimeout { get; set; } = 10000;
        public int TestFrameInterval { get; set; } = 10000;
        public int AcknowledgeTimeout { get; set; } = 3000;
        public int RetryCount { get; set; } = 3;
        public int RetryDelay { get; set; } = 1000;
    }

    public class IEC104PointConfig
    {
        public string PointName { get; set; } = string.Empty;
        public IEC104TypeIdentification TypeId { get; set; }
        public int IoAddress { get; set; }
        public DataType DataType { get; set; } = DataType.Float;
        public double Ratio { get; set; } = 1.0;
        public double OffsetValue { get; set; } = 0;
        public string Unit { get; set; } = string.Empty;
        public double MinValue { get; set; } = double.MinValue;
        public double MaxValue { get; set; } = double.MaxValue;
        public bool IsReadOnly { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public int ScanRate { get; set; } = 1000;
    }

    public class IEC104Apdu
    {
        public byte StartByte { get; set; } = 0x68;
        public byte Length { get; set; }
        public ushort SendSeqNumber { get; set; }
        public ushort ReceiveSeqNumber { get; set; }
        public IEC104Asdu? Asdu { get; set; }
    }

    public class IEC104Asdu
    {
        public IEC104TypeIdentification TypeId { get; set; }
        public byte VariableStructure { get; set; }
        public byte CauseOfTransmission { get; set; }
        public bool IsNegative { get; set; }
        public bool IsTest { get; set; }
        public byte OriginatorAddress { get; set; }
        public ushort CommonAddress { get; set; }
        public List<IEC104InformationObject> InformationObjects { get; set; } = new List<IEC104InformationObject>();
    }

    public class IEC104InformationObject
    {
        public uint IoAddress { get; set; }
        public byte[]? Data { get; set; }
        public object? Value { get; set; }
        public byte Quality { get; set; }
        public DateTime? TimeStamp { get; set; }
    }

    public class IEC104InformationElement
    {
        public IEC104SinglePointValue SinglePoint { get; set; }
        public IEC104DoublePointValue DoublePoint { get; set; }
        public int StepPosition { get; set; }
        public float NormalizedValue { get; set; }
        public float ScaledValue { get; set; }
        public float FloatValue { get; set; }
        public uint BinaryCounter { get; set; }
        public byte QualityDescriptor { get; set; }
        public DateTime? TimeStamp { get; set; }
    }

    public class IEC104Response
    {
        public bool Success { get; set; }
        public byte ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public IEC104Apdu? Apdu { get; set; }
        public byte[]? RawData { get; set; }
    }

    public class IEC104ControlCommand
    {
        public IEC104TypeIdentification TypeId { get; set; }
        public int IoAddress { get; set; }
        public object? Value { get; set; }
        public byte Qualifier { get; set; }
        public bool Select { get; set; } = false;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;

namespace LZ
{
    public class FileInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Size { get; set; }
        public string ModifiedDate { get; set; }
        public string Permissions { get; set; }
    }

    public class TrackPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Time { get; set; }
        public double Vel { get; set; }
        public double Acc { get; set; }
        public double Theta { get; set; }
        public double Curvature { get; set; }
        public double OtherOne { get; set; }
        public double OtherTwo { get; set; }
    }
    public class OriginInfo
    {
        public double Lat { get; set; }     // 纬度
        public double Lon { get; set; }     // 经度
        public double Height { get; set; }  // 高度
        public double Azimuth { get; set; } // 方位角
    }


    // 辅助数据结构（用于解析 0x95）
    public class LaneMetricsItem
    {
        public float Distance { get; set; }
        public float LateralSpeed { get; set; }
        public float LateralAcc { get; set; }
        public float TTC { get; set; }
    }

    public class Vehicle2LaneInfoC
    {
        public string ID { get; set; }
        public string LaneName { get; set; }
        public int RelativeNum { get; set; }
        public List<LaneMetricsItem> Metrics { get; set; } = new List<LaneMetricsItem>();
    }

    public class LaneRelativeDataC
    {
        public int LaneNum { get; set; }
        public List<Vehicle2LaneInfoC> Infos { get; set; } = new List<Vehicle2LaneInfoC>();
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SysStatData  // 系统状态数据
    {
        public uint uiCtrMdStat;  // 实际系统控制模式
        public uint uiPauseStat;  // 紧急按扭状态  
        public uint uiHandShankStat;  // 手柄状态 
        public uint uiARLearnStat;  // 油门学习状态  
        public uint uiBRLearnStat;  // 制动学习状态  
        public uint uiSRLearnStat;  // 转向学习状态  
        public uint uiSysErrCode;  // 系统错误码  举例：1、转向报错 2、加速踏板报错 3、制动报错
        public uint uiSysRunStat; // 系统运行状态
        public float fBatteryVoltage; // 电池电压
        public float fBatteryCurrent; // 电池电流
        public float fBatterySOC; // 电池SOC
        public float fBatteryTemp; // 电池温度
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SecurityManageData  // 安全管理数据
    {
        public uint uisecurity_level; // 安全等级
        public uint uisecurity_event; // 安全事件
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AlarmData  // 系统报错数据
    {
        public uint uiSysErra;
        public uint uiSysErrb;
        public uint uiSysErrc;
        public uint uiSysErrd;
        public uint uiSysErre;
        public uint uiSysErrf;
        public uint uiSysErrg;
        public uint uiSysErrh;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GenneralData
    {
        public uint Point; /* 记录数据条数 */
        public float Time; /* 文件记录时间 */
        public double Utc_time; /* 由GPS周内秒换算的UTC时间（ms） */
        public uint Soft_Vertion; /* 域控软件版本号 */
        public uint Hardware_SN; /* 硬件序列号 */
        public float SBV; /* 电池电压 */
        public float SOC; /* 电池电量 */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MotorData
    {
        public float position; /* 位置 */
        public float angle; /* 角度 */
        public float speed; /* 速度 */
        public float acc; /* 加速度 */
        public float force; /* 力 */
        public float torque; /* 扭矩 */
        public float column_torque; // 转向柱扭矩
                                    // 20250318
        public uint uiMotEnStat; // 电机使能状态
        public uint uiMotCtrlMode; // 电机控制模式
        public uint uiMotErrCode; // 电机错误码  举例：1、电机过流保护 2、编码器故障
        public ushort holdon;
        public float cmd; // 下发命令
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MotionPackData
    {
        public float X; /* 测试车辆在本地坐标系中的靶点X坐标 */
        public float Y; /* 测试车辆在本地坐标系中的靶点Y坐标 */
        public float INS_X; /* 测试车辆在本地坐标系中定位点X坐标 */
        public float INS_Y; /* 测试车辆在本地坐标系中定位点Y坐标 */
        public float Site_X; /* 测试车辆在场地坐标系中定位点X坐标(需要设置场地定位点) */
        public float Site_Y; /* 测试车辆在场地坐标系中定位点Y坐标(需要设置场地定位点) */
        public float Forward_velocity; /* 测试车辆前向速度 */
        public float Lateral_velocity; /* 测试车辆横向速度 */
        public float Velocity; /* 车速，合速度 */
        public float Forward_acceleration; /* 前进方向加速度 */
        public float Lateral_acceleration; /* 横向方向加速度 */
        public float Acceleration; /* 加速度 */
        public float Roll_angle; /* 横滚角 */
        public float Pitch_angle; /* 俯仰角 */
        public float Yaw_angle; /* 航向角 */
        public float YawRate; /* 航向角速度 */
        public float Slip_angle; /* 侧滑角 */
        public float MP_time; /* MP时间 */
        public float Forward_acceleration_body; /* 前进方向加速度 */
        public float Lateral_acceleration_body; /* 横向方向加速度 */
        public float Distance_travelled_by_wheelbase_mid_point; /* 从测试开始到轴距中点的行驶距离。 */
        public uint INS_Status; /* INS状态 */
        public uint RTK_Status; /* RTK状态 */
        public double Longitude; /* 经度 */
        public double Latitude; /* 纬度 */
        public float Azimuth; /* 方位角 */
        public float Actual_X; /* 实际坐标X */
        public float Actual_Y; /* 实际坐标Y */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ReferenceLineData
    {
        public float Line_offset; /* 到车道线的偏移距离 */
        public float Line_approach_velocity; /* 到车道线的接近速度 */
        public float ttc; /* TTC */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VehicleRelativeData
    {
        public float TTC_longitudinal; /* 纵向TTC */
        public float Relative_longitudinal_distance; /* 相对纵向距离 */
        public float Relative_lateral_distance; /* 相对横向距离 */
        public float Relative_resultant_distance; /* 相对斜距 */
        public float Relative_longitudinal_velocity; /* 相对纵向速度 */
        public float Relative_lateral_velocity; /* 相对横向速度 */
        public float Relative_velocity; /* 相对速度 */
        public float Relative_yaw; /* 相对航向角 */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PathFollowData
    {
        public float Actual_X_front_axle; // 前轮中心点的实际坐标X
        public float Actual_Y_front_axle; // 前轮中心点的实际坐标Y
        public float Desire_X;
        public float Desire_Y;
        public float Desire_Vel;
        public float Desired_X_lead_axle; // 前桥中心点的期望坐标X(规划的INS坐标)
        public float Desired_Y_lead_axle; // 前桥中心点的期望坐标X(规划的INS坐标)
        public float Actual_X_rear_axle; // 后轮中心点的实际坐标X
        public float Actual_Y_rear_axle; // 后轮中心点的实际坐标X
        public float Distance_LCRP; /* LCRP距离 */
        public float Error_Path_following; /* 路径跟踪误差 */
        public float Look_ahead_distance; /* 前视距离 */
        public float Lateral_Err; /* 横向误差 */
        public float Distance_error; /* 距离误差 */
        public float Vel_Err; /* 速度误差 */
        public float Heading_Err; /* 航向误差 */
        public float TTC_interest; /* 到兴趣点的TTC */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RobotMotorData
    {
        public MotorData sSR_MotorState; /* 转向电机状态  46byte */
        public MotorData sBR_MotorState; /* 制动电机状态   46byte */
        public MotorData sAR_MotorState; /* 加速踏板电机状态46byte */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CalculatedData
    {
        public float Calculated_Channel1;
        public float Calculated_Channel2;
        public float Calculated_Channel3;
        public float Calculated_Channel4;
        public float Calculated_Channel5;
        public float Calculated_Channel6;
        public float Calculated_Channel7;
        public float Calculated_Channel8;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct S_ALARM_DATA
    {
        public byte TriggerAudio;       // startbit:0
        public byte TriggerLightLeft;   // startbit:8
        public byte TriggerLightRight;  // startbit:16
        public byte TriggerCamera;      // startbit:24
        public byte TriggerShakeX;      // startbit:32
        public byte TriggerShakeY;      // startbit:40
        public byte TriggerShakeZ;      // startbit:48
        public byte TriggerShakeStatus; // startbit:56
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMDData_V6
    {
        public GenneralData sGenneralData; /* 28 byte */
        public RobotMotorData sRobotData; /* 138 byte */
        public MotionPackData sVUTMotionPackData; /* 120 byte */
        public PathFollowData sVUTPathFollowData; /* 56 byte */
        public MotionPackData sSPTMotionPackData; /* 120 byte */
        public VehicleRelativeData sVehicleToSPTData; /* 32 byte */
        public MotionPackData sSubjectMotionPackData; /* 120 byte */
        public VehicleRelativeData sVehicleToSubjectData; /* 32 byte */
        public ReferenceLineData sReferenceLineData; /* 12 byte */
        public SecurityManageData sSecurityManageData; /* 8 byte */
        public S_ALARM_DATA sUserDefinedData; /* 8 byte */
        public CalculatedData sCalculatedData; /* 32 byte */
        public SysStatData sSysStatData; /* 28 byte */
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DebugDatas
    {
        public byte Header1;  // 对应unsigned char Header1，C#中byte表示无符号字节，范围是0 - 255
        public byte Header2;
        public byte DeviceType;
        public byte FunctionCode;
        public uint Length;  // 对应unsigned int，C#中用uint表示无符号整数
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
        public byte[] FuntionParamter;  // 功能参数，使用byte数组来表示
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    //[StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DebugConfig
    {
        // bottom配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] robot_type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] can_device_name;
        public int can_baud;

        // net配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] local_ip1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] local_ip2;

        // upper配置
        public int tcp_local_port;
        public int tcp_local_port1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] udp_server_ip;
        public int udp_server_port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] default_path_file_name;

        // ins配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] ins_type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] agreement;
        public int message_id;
        public int region;
        public int sys_time_enable;
        public int udp_local_port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] vut_ins_ip;
        public int vut_ins_port;
        // ufo_ins配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spt_ins_type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spt_agreement;
        public int spt_message_id;
        public int spt_region;
        public int spt_sys_time_enable;
        public int spt_udp_local_port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] spt_ins_ip;
        public int spt_ins_port;
        //vt_ins配置
        // ins配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] vt_ins_type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] vt_agreement;
        public int vt_message_id;
        public int vt_region;
        public int vt_sys_time_enable;
        public int vt_udp_local_port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] vt_ins_ip;
        public int vt_ins_port;
        // smd配置
        public int smd_enable_file_header;
        public int smd_enable_circle_buffer;
        public int smd_save_days;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] smd_file_save_path;
        // log配置
        public int log_enable_file_header;
        public int log_enable_circle_buffer;
        public int log_save_days;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] log_file_save_path;
        // data配置
        public int data_udp_local_port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] data_can_device_name;
        public int data_can_baud;
        public int data_save_days;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] data_file_save_path;

        // fcw配置
        public int fcw_enable_file_header;
        public int fcw_enable_circle_buffer;
        public int fcw_save_days;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] fcw_file_save_path;

        // daq配置
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] daq_can_device_name;
        public int daq_can_baud;

        // headtracker配置
        public int headtrakertype;
        public int dbc_version;
        public uint save_mode;
    }



    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RoboteData_0x91_Part1
    {
        public uint Point; /*记录数据条数*/
        public float Time;    /*文件记录时间*/
        public double Utc_time; /*由GPS周内秒换算的UTC时间（ms）*/
        public uint Soft_Vertion;      /*域控软件版本号*/
        public uint Hardware_SN;     /*硬件序列号*/
        public float SBV;      /*电池电压*/
        public float SOC;      /*电池电量*/

        //RobotMotorData sRobotData;                  /*132 byte*/
        public float SR_position; /*位置*/
        public float SR_angle;    /*角度*/
        public float SR_speed; /*速度*/
        public float SR_acc;      /*加速度*/
        public float SR_force;    /*力*/
        public float SR_torque;   /*扭矩*/
        public float SR_column_torque;//  转向柱扭矩
        public uint SR_uiMotEnStat;//电机使能状态
        public uint SR_uiMotCtrlMode;//电机控制模式
        public uint SR_uiMotErrCode;//电机错误码  举例：1、电机过流保护 2、编码器故障
        public ushort SR_holdon;
        public float SR_cmd; //下发命令

        public float BR_position; /*位置*/
        public float BR_angle;    /*角度*/
        public float BR_speed; /*速度*/
        public float BR_acc;      /*加速度*/
        public float BR_force;    /*力*/
        public float BR_torque;   /*扭矩*/
        public float BR_column_torque;//  转向柱扭矩
        public uint BR_uiMotEnStat;//电机使能状态
        public uint BR_uiMotCtrlMode;//电机控制模式
        public uint BR_uiMotErrCode;//电机错误码  举例：1、电机过流保护 2、编码器故障
        public ushort BR_holdon;
        public float BR_cmd; //下发命令

        public float AR_position; /*位置*/
        public float AR_angle;    /*角度*/
        public float AR_speed; /*速度*/
        public float AR_acc;      /*加速度*/
        public float AR_force;    /*力*/
        public float AR_torque;   /*扭矩*/
        public float AR_column_torque;//  转向柱扭矩
        public uint AR_uiMotEnStat;//电机使能状态
        public uint AR_uiMotCtrlMode;//电机控制模式
        public uint AR_uiMotErrCode;//电机错误码  举例：1、电机过流保护 2、编码器故障
        public ushort AR_holdon;
        public float AR_cmd; //下发命令
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RoboteData_0x91_Part2
    {
        // MotionPackData sVUTMotionPackData;          /*120 byte*/
        public float VUTMP_X;                    /*测试车辆在本地坐标系中的靶点X坐标*/
        public float VUTMP_Y;                    /*测试车辆在本地坐标系中的靶点Y坐标*/
        public float VUTMP_INS_X;                /*测试车辆在本地坐标系中定位点X坐标*/
        public float VUTMP_INS_Y;                /*测试车辆在本地坐标系中定位点Y坐标*/
        public float VUTMP_Site_X;               /*测试车辆在场地坐标系中定位点X坐标(需要设置场地定位点)*/
        public float VUTMP_Site_Y;               /*测试车辆在场地坐标系中定位点Y坐标(需要设置场地定位点)*/
        public float VUTMP_Forward_velocity;     /*测试车辆前向速度*/
        public float VUTMP_Lateral_velocity;     /*测试车辆横向速度*/
        public float VUTMP_Vertical_velocity;    /*车速，合速度*/
        public float VUTMP_Forward_acceleration;     /*前进方向加速度*/
        public float VUTMP_Lateral_acceleration;     /*横向方向加速度*/
        public float VUTMP_Vertical_acceleration;    /*加速度*/
        public float VUTMP_Roll_angle;   /*横滚角*/
        public float VUTMP_Pitch_angle;  /*俯仰角*/
        public float VUTMP_Yaw_angle;    /*航向角*/
        public float VUTMP_YawRate;      /*航向角速度*/
        public float VUTMP_Slip_angle;   /*侧滑角*/
        public float VUTMP_MP_time;      /*MP时间*/
        public float VUTMP_Forward_acceleration_body;    /*前进方向加速度*/
        public float VUTMP_Lateral_acceleration_body;    /*横向方向加速度*/
        public float VUTMP_Distance_travelled_by_wheelbase_mid_point; /*从测试开始到轴距中点的行驶距离。*/
        public uint VUTMP_INS_Status;    /*INS状态*/
        public uint VUTMP_RTK_Status;    /*RTK状态*/
        public double VUTMP_Longitude;           /*经度*/
        public double VUTMP_Latitude;            /*纬度*/
        public float VUTMP_Azimuth;              /*方位角*/
        public float VUTMP_Actual_X;             /*实际坐标X*/
        public float VUTMP_Actual_Y;             /*实际坐标Y*/

        //PathFollowData sVUTPathFollowData;         /*56 byte*/
        public float VUTPF_Actual_X_front_axle;  //前轮中心点的实际坐标X
        public float VUTPF_Actual_Y_front_axle;  //前轮中心点的实际坐标Y
        public float VUTPF_Desire_X;
        public float VUTPF_Desire_Y;
        public float VUTPF_Desire_Vel;
        public float VUTPF_Desired_X_lead_axle;  //前桥中心点的期望坐标X(规划的INS坐标)
        public float VUTPF_Desired_Y_lead_axle;  //前桥中心点的期望坐标X(规划的INS坐标)
        public float VUTPF_Actual_X_rear_axle;   //后轮中心点的实际坐标X
        public float VUTPF_Actual_Y_rear_axle;   //后轮中心点的实际坐标X
        public float VUTPF_Distance_LCRP;        /*LCRP距离*/
        public float VUTPF_Error_Path_following; /*路径跟踪误差*/
        public float VUTPF_Look_ahead_distance; /*前视距离*/
        public float VUTPF_Lateral_Err; /*横向误差*/
        public float VUTPF_Distance_error;   /*距离误差*/
        public float VUTPF_Vel_Err; /*速度误差*/
        public float VUTPF_Heading_Err; /*航向误差*/
        public float VUTPF_TTC_interest; /*到兴趣点的TTC*/
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RoboteData_0x91_Part3
    {
        //MotionPackData sSPTMotionPackData;          /*120 byte*/
        public float SPTMP_X;                    /*测试车辆在本地坐标系中的靶点X坐标*/
        public float SPTMP_Y;                    /*测试车辆在本地坐标系中的靶点Y坐标*/
        public float SPTMP_INS_X;                /*测试车辆在本地坐标系中定位点X坐标*/
        public float SPTMP_INS_Y;                /*测试车辆在本地坐标系中定位点Y坐标*/
        public float SPTMP_Site_X;               /*测试车辆在场地坐标系中定位点X坐标(需要设置场地定位点)*/
        public float SPTMP_Site_Y;               /*测试车辆在场地坐标系中定位点Y坐标(需要设置场地定位点)*/
        public float SPTMP_Forward_velocity;     /*测试车辆前向速度*/
        public float SPTMP_Lateral_velocity;     /*测试车辆横向速度*/
        public float SPTMP_Vertical_velocity;    /*车速，合速度*/
        public float SPTMP_Forward_acceleration;     /*前进方向加速度*/
        public float SPTMP_Lateral_acceleration;     /*横向方向加速度*/
        public float SPTMP_Vertical_acceleration;    /*加速度*/
        public float SPTMP_Roll_angle;   /*横滚角*/
        public float SPTMP_Pitch_angle;  /*俯仰角*/
        public float SPTMP_Yaw_angle;    /*航向角*/
        public float SPTMP_YawRate;      /*航向角速度*/
        public float SPTMP_Slip_angle;   /*侧滑角*/
        public float SPTMP_MP_time;      /*MP时间*/
        public float SPTMP_Forward_acceleration_body;    /*前进方向加速度*/
        public float SPTMP_Lateral_acceleration_body;    /*横向方向加速度*/
        public float SPTMP_Distance_travelled_by_wheelbase_mid_point; /*从测试开始到轴距中点的行驶距离。*/
        public uint SPTMP_INS_Status;    /*INS状态*/
        public uint SPTMP_RTK_Status;    /*RTK状态*/
        public double SPTMP_Longitude;           /*经度*/
        public double SPTMP_Latitude;            /*纬度*/
        public float SPTMP_Azimuth;              /*方位角*/
        public float SPTMP_Actual_X;             /*实际坐标X*/
        public float SPTMP_Actual_Y;             /*实际坐标Y*/

        //VehicleRelativeData sVehicleToSPTData;      /*32 byte*/
        public float VehicleToSPT_TTC_longitudinal;               /*纵向TTC*/
        public float VehicleToSPT_Relative_longitudinal_distance; /*相对纵向距离*/
        public float VehicleToSPT_Relative_lateral_distance;      /*相对横向距离*/
        public float VehicleToSPT_Relative_resultant_distance;    /*相对斜距*/
        public float VehicleToSPT_Relative_longitudinal_velocity;   /*相对纵向速度*/
        public float VehicleToSPT_Relative_lateral_velocity;        /*相对横向速度*/
        public float VehicleToSPT_Relative_velocity;                /*相对速度*/
        public float VehicleToSPT_Relative_yaw;                     /*相对航向角*/
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RoboteData_0x91_Part4
    {
        //MotionPackData sSubjectMotionPackData;      /*120 byte*/
        public float SubMP_X;                    /*测试车辆在本地坐标系中的靶点X坐标*/
        public float SubMP_Y;                    /*测试车辆在本地坐标系中的靶点Y坐标*/
        public float SubMP_INS_X;                /*测试车辆在本地坐标系中定位点X坐标*/
        public float SubMP_INS_Y;                /*测试车辆在本地坐标系中定位点Y坐标*/
        public float SubMP_Site_X;               /*测试车辆在场地坐标系中定位点X坐标(需要设置场地定位点)*/
        public float SubMP_Site_Y;               /*测试车辆在场地坐标系中定位点Y坐标(需要设置场地定位点)*/
        public float SubMP_Forward_velocity;     /*测试车辆前向速度*/
        public float SubMP_Lateral_velocity;     /*测试车辆横向速度*/
        public float SubMP_Vertical_velocity;    /*车速，合速度*/
        public float SubMP_Forward_acceleration;     /*前进方向加速度*/
        public float SubMP_Lateral_acceleration;     /*横向方向加速度*/
        public float SubMP_Vertical_acceleration;    /*加速度*/
        public float SubMP_Roll_angle;   /*横滚角*/
        public float SubMP_Pitch_angle;  /*俯仰角*/
        public float SubMP_Yaw_angle;    /*航向角*/
        public float SubMP_YawRate;      /*航向角速度*/
        public float SubMP_Slip_angle;   /*侧滑角*/
        public float SubMP_MP_time;      /*MP时间*/
        public float SubMP_Forward_acceleration_body;    /*前进方向加速度*/
        public float SubMP_Lateral_acceleration_body;    /*横向方向加速度*/
        public float SubMP_Distance_travelled_by_wheelbase_mid_point; /*从测试开始到轴距中点的行驶距离。*/
        public uint SubMP_INS_Status;    /*INS状态*/
        public uint SubMP_RTK_Status;    /*RTK状态*/
        public double SubMP_Longitude;           /*经度*/
        public double SubMP_Latitude;            /*纬度*/
        public float SubMP_Azimuth;              /*方位角*/
        public float SubMP_Actual_X;             /*实际坐标X*/
        public float SubMP_Actual_Y;             /*实际坐标Y*/

        //VehicleRelativeData sVehicleToSubjectData;  /*32 byte*/
        public float VehicleToSub_TTC_longitudinal;               /*纵向TTC*/
        public float VehicleToSub_Relative_longitudinal_distance; /*相对纵向距离*/
        public float VehicleToSub_Relative_lateral_distance;      /*相对横向距离*/
        public float VehicleToSub_Relative_resultant_distance;    /*相对斜距*/
        public float VehicleToSub_Relative_longitudinal_velocity;   /*相对纵向速度*/
        public float VehicleToSub_Relative_lateral_velocity;        /*相对横向速度*/
        public float VehicleToSub_Relative_velocity;                /*相对速度*/
        public float VehicleToSub_Relative_yaw;                     /*相对航向角*/

        //ReferenceLineData sReferenceLineData;       /*12 byte*/
        public float ReferenceLine_Line_offset;           /*到车道线的偏移距离*/
        public float ReferenceLine_Line_approach_velocity;   /*到车道线的接近速度*/
        public float ReferenceLine_TTC;          /*TTC*/

        //SecurityManageData sSecurityManageData;     /*8 byte*/
        public uint security_level; //安全等级
        public uint security_event; //安全事件

        //S_ALARM_DATA sUserDefinedData;              /*8 byte*/
        public char TriggerAudio;       // startbit:0
        public char TriggerLightLeft;   // startbit:8
        public char TriggerLightRight;  // startbit:16
        public char TriggerCamera;      // startbit:24
        public char TriggerShakeX;      // startbit:32
        public char TriggerShakeY;      // startbit:40
        public char TriggerShakeZ;      // startbit:48
        public char TriggerShakeStatus; // startbit:56

        //CalculatedData sCalculatedData;             /*32 byte*/
        public float Calculated_Channel1;
        public float Calculated_Channel2;
        public float Calculated_Channel3;
        public float Calculated_Channel4;
        public float Calculated_Channel5;
        public float Calculated_Channel6;
        public float Calculated_Channel7;
        public float Calculated_Channel8;

        //SysStatData sSysStatData;                   /*28 byte*/
        public uint uiCtrMdStat;  // 实际系统控制模式
        public uint uiPauseStat;  // 紧急按扭状态  
        public uint uiHandShankStat;  // 手柄状态 
        public uint uiARLearnStat;  // 油门学习状态  
        public uint uiBRLearnStat;  // 制动学习状态  
        public uint uiSRLearnStat;  // 转向学习状态  
        public uint uiSysErrCode;  // 系统错误码  举例：1、转向报错 2、加速踏板报错 3、制动报错
        public uint uiSysRunStat; // 系统运行状态
        public float fBatteryVoltage; //电池电压
        public float fBatteryCurrent; //电池电流
        public float fBatterySOC; //电池SOC
        public float fBatteryTemp; //电池温度
    }

    public struct SendBack
    {
        public int ack;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] FuntionParamter;  // 功能参数，使用byte数组来表示
    }

    public struct DebugStatus
    {
        public int robot_status;
        public int vut_ins_status;
        public int spt_ins_status;
        public int vt_ins_status;
    }

    public struct COGStatus
    {
        public int sample_count;
        public float est_cog;
        public char is_ins_ready;
    }

    public struct DebugControlParam
    {
        // VelocityPID
        public float Low_P;
        public float Low_I;
        public float Low_D;
        public float Mid_P;
        public float Mid_I;
        public float Mid_D;
        public float Hig_P;
        public float Hig_I;
        public float Hig_D;
        public float Acc_Max;
        public float Acc_Min;
        //public float SpeedErrLimit;
        // VelocityComp
        public float Heading;
        public float ten;
        public float twenty;
        public float thirty;
        public float forty;
        public float fifty;
        public float sixty;
        public float seventy;
        public float eighty;
        public float ninety;
        public float hundred;
        public float hundred_ten;
        public float hundred_twenty;
        public float head_ref;
        // StanelyPara
        public char CurveFlag;  // 0：直线，    1：转弯或者变道
        public char FcwFlag;    //
        public char ElkFlag;    //
        public float LineKp1;   // 直线场景一阶段Kp1
        public float LineKp2;   // 直线场景一阶段Kp2
        public float CurveKp1;  // 非直线场景一阶段Kp1
        public float CurveKp2;  // 非直线场景一阶段Kp2
        public float CurveKp3;  // 非直线场景一阶段Kp3
        public float FrontBase; // 前轴长度
        public float Preview1;
        public float Preview2;
        public float Preview3;
        //public float Preview_Point;

        public float lf10_stanley;
        public float lf20_stanley;
        public float lf30_stanley;
        public float rf10_stanley;
        public float rf20_stanley;
        public float ccrh_stanley;
        public float steer_angle_limit;
        public float steer_speed_limit;

        public float XActual;
        public float SRTortue;
        public float VehicleSpeed;
        public float YawRateData;
    }

    // 经纬度点
    public struct Point1
    {
        public double Lat { get; set; } // 纬度
        public double Lon { get; set; } // 经度
    }

    // 包含高度、航向和速度的经纬度点
    public struct BlhPoint
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
        public double Altitude { get; set; }
        public double Heading { get; set; }
        public double Velocity { get; set; }
    }

    // 笛卡尔坐标点
    public struct XyzPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SPoint
    {
        public double dLongitude; // 经度 (X)
        public double dLatitude;  // 纬度 (Y)
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SGeofence
    {
        public int nPointCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public SPoint[] vecPoints;
    }

}

using HandyControl.Tools;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace LZ
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private UdpClient udpServer;
        private DispatcherTimer timer;

        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private bool _isConnected;

        double OriginLat = 0.0;
        double OriginLon = 0.0;
        double OriginHeading = 0.0;

        BlhPoint BLH_Origin = new BlhPoint();
        BlhPoint BLH_VUT = new BlhPoint();
        XyzPoint VUT_XYZ = new XyzPoint();


        int udp_port = 8201;
        bool isRunning = false;
        private IPEndPoint remoteEndPoint;
        private Thread receiveThread;
        private NetworkStream stream;
        SMDData_V6 smddata;
        SMDData_V6 SMDDATA;
        DebugDatas debugData;
        DebugConfig debugConfig;
        RoboteData_0x91_Part1 robotData_Part1;
        RoboteData_0x91_Part2 robotData_Part2;
        RoboteData_0x91_Part3 robotData_Part3;
        RoboteData_0x91_Part4 robotData_Part4;
        SendBack sSendBack;
        DebugStatus sDebugStatus;
        DebugControlParam sDebugControlParam;
        OriginInfo origin = new OriginInfo();
        private CoordinateTransformationFactory _ctf;
        private ICoordinateTransformation _wgs84ToUtm;

        // 原始图片参数（line.png：1904×518）
        private const double OriginalImgWidth = 1904;  // 原始图片宽度（像素）
        private const double OriginalImgHeight = 518; // 原始图片高度（像素）
        private const double ActualLaneWidth = 3.5;    // 实际车道宽度（米）

        // 需用户测量的参数（在原始图片中用PS/GIMP测量）
        private const double OriginalLanePixelWidth = 40;  // 原始图片中1条车道的宽度（像素）
        private const double Original4thLaneY = 283;       // 原始图片中“第四条车道线”的Y坐标（像素）
        private const double OriginalStartPointX = 400;    // 原始图片中“第四条车道中间偏左”的X坐标（像素）
        // 动态计算参数（无需手动修改）
        private double ImgScale_X { get; set; }              // 图片Uniform填充后的实际缩放比例
        private double ImgScale_Y { get; set; }              // 图片Uniform填充后的实际缩放比例
        private Point ImgOffset { get; set; }              // 图片在Canvas中的偏移（如居中时的边距）
        private Point CanvasStartPoint { get; set; }       // 轨迹起点在Canvas中的实际坐标
        private double TrackScale_X { get; set; }            // 轨迹米→像素的缩放比例（米/像素）
        private double TrackScale_Y { get; set; }            // 轨迹米→像素的缩放比例（米/像素）
        public MainWindow()
        {
            InitializeComponent();
            ConfigHelper.Instance.SetWindowDefaultStyle();

            debugData = new DebugDatas();
            debugData.FuntionParamter = new byte[1024];
            debugConfig = new DebugConfig();
            smddata = new SMDData_V6();
            SMDDATA = new SMDData_V6();
            robotData_Part1 = new RoboteData_0x91_Part1();
            robotData_Part2 = new RoboteData_0x91_Part2();
            robotData_Part3 = new RoboteData_0x91_Part3();
            robotData_Part4 = new RoboteData_0x91_Part4();
            sSendBack = new SendBack();
            sDebugStatus = new DebugStatus();
            sDebugControlParam = new DebugControlParam();

            timer = new DispatcherTimer(); // 设置定时器间隔为1000毫秒（1秒）
            timer.Interval = TimeSpan.FromSeconds(0.5);
            timer.Tick += UpdateTimer_Tick; // 指定事件触发时调用的方法
            _ctf = new CoordinateTransformationFactory();
            var wgs84 = GeographicCoordinateSystem.WGS84;
            var utm = ProjectedCoordinateSystem.WGS84_UTM(50, true); // Example: UTM zone 50N
            _wgs84ToUtm = _ctf.CreateFromCoordinateSystems(wgs84, utm);

        }

        // 定时器事件处理方法
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (sDebugStatus.robot_status == 0)
            {
                if (log_warning_checkbox.IsChecked == true)
                {
                    AppendLog("机器人CAN心跳异常！\r\n");
                }

            }
            if (sDebugStatus.vut_ins_status == 0)
            {
                if (log_warning_checkbox.IsChecked == true)
                {
                    AppendLog("VUT惯导定位错误！\r\n");
                }

            }
            if (sDebugStatus.spt_ins_status == 0)
            {
                if (log_warning_checkbox.IsChecked == true)
                {
                    AppendLog("SPT惯导定位错误！\r\n");
                }
            }
            if (sDebugStatus.vt_ins_status == 0)
            {
                if (log_warning_checkbox.IsChecked == true)
                {
                    AppendLog("VT惯导定位错误！\r\n");
                }

            }
            // 可选：让TextBox自动滚动到最后一行
            //AppendLog($"robotData_part1:{robotData_Part1.SBV}\r\n");
            //textBox3.AppendText($"robotData_part1:{robotData_Part1.Soft_Vertion}\r\n");
            //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Latitude}\r\n");
            //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Longitude}\r\n");
            //textBox1.SelectionStart = textBox1.TextLength;
            //textBox1.ScrollToCaret();
        }
        private void Connect_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Connect_Button.Content.ToString() == "关闭")
            {
                Connect_Button.IsEnabled = false;
                close_connection();

                //stream.Close();
                //client.Close();
                AppendLog("连接已关闭");
                Connect_Button.Content = "连接";
                Connect_Button.IsEnabled = true;
                timer.Stop();
                return;
            }
            else
            {
                Connect_Button.IsEnabled = false;
                AppendLog("正在连接...");
                try
                {
                    // 初始化UDP服务器
                    udpServer = new UdpClient(udp_port);
                    remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    isRunning = true;

                    // 启动接收线程
                    receiveThread = new Thread(ReceiveUdpData);
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                    // 获取并打印本地IP地址
                    string hostName = Dns.GetHostName();
                    IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
                    List<IPAddress> ipAddresses = hostEntry.AddressList
                        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork) // 只获取IPv4地址
                        .ToList();

                    // 更新UI状态
                    AppendLog($"UDP服务器已启动，监听端口 {udp_port}");
                    //AppendLog($"本机IP地址: {string.Join(", ", ipAddresses)}\r\n");
                    timer.Start();
                    // 更新UI状态
                    // textBox3.AppendText($"UDP服务器已启动，监听端口 {udp_port}\r\n");

                }
                catch (Exception ex)
                {
                    AppendLog($"错误! {ex.Message}\r\n");
                }
                try
                {
                    // 连接服务器
                    _client = new TcpClient();
                    _client.Connect(server_ip.Text, int.Parse(server_port.Text));
                    _stream = _client.GetStream();
                    _isConnected = true;

                    // 启动接收线程
                    _receiveThread = new Thread(receiveData);
                    _receiveThread.IsBackground = true;
                    _receiveThread.Start();

                    AppendLog($"已连接到服务器 {server_ip.Text}:{server_port.Text}");

                    //client = new TcpClient();
                    //await client.ConnectAsync(server_ip.Text, int.Parse(server_port.Text));
                    //AppendLog("连接成功！\r\n");
                    Connect_Button.IsEnabled = true;
                    Connect_Button.Content = "关闭";
                    //stream = client.GetStream();
                    //_ = Task.Run(ReceiveData);
                }
                catch (Exception ex)
                {
                    AppendLog($"连接失败: {ex.Message}\r\n");
                    close_connection();
                    Connect_Button.IsEnabled = true;

                }

            }
        }

        // 接收服务器数据
        private void receiveData()
        {
            byte[] buffer = new byte[1024];
            int bytesRead;

            try
            {
                while (_isConnected && _stream != null && _stream.CanRead)
                {
                    bytesRead = _stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // 连接已关闭
                        AppendLog("服务器已断开连接");
                        break;
                    }
                    if (buffer[0] == 0xAA && buffer[1] == 0x55)
                    {
                        
                        try
                        {
                            byte[] lengthTuple = new byte[4] { buffer[4], buffer[5], buffer[6], buffer[7] };
                            int msgLength = BitConverter.ToInt32(lengthTuple, 0);

                            byte[] msg = new byte[msgLength];
                            Array.Copy(buffer, 8, msg, 0, msgLength);
                            

                            switch (buffer[3])
                            {
                                case 0x91:
                                    break;
                                case 0x92:
                                    debugConfig = ByteArrayToDebugConfig(msg);
                                    AppendLog("配置文件接收成功!\r\n");
                                    break;

                                case 0x95:
                                    AppendLog($"pathfile{Encoding.UTF8.GetString(msg).TrimEnd('\0')}");
                                    Refresh_Track(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    break;

                                case 0x81:
                                    sSendBack = BytesToStruct<SendBack>(msg, 0);
                                    //byte[] ackTuple = new byte[4] { data[8], data[9], data[10], data[11] };
                                    //int ack = BitConverter.ToInt32(ackTuple, 0);
                                    //Array.Copy(data, 12, msg, 0, length-4);
                                    if (sSendBack.ack == 1)
                                    {
                                        if ((debugConfig.vut_ins_ip.SequenceEqual(debugConfig.spt_ins_ip) && debugConfig.vut_ins_port == debugConfig.spt_ins_port) ||
                                            (debugConfig.vut_ins_ip.SequenceEqual(debugConfig.vt_ins_ip) && debugConfig.vut_ins_port == debugConfig.vt_ins_port) ||
                                            (debugConfig.spt_ins_ip.SequenceEqual(debugConfig.vt_ins_ip) && debugConfig.spt_ins_port == debugConfig.vt_ins_port))
                                        {
                                            MessageBox.Show("错误：多个惯导的IP地址和端口不能同时相同！请修改", "配置错误");
                                        }
                                        else
                                        {
                                            MessageBox.Show("配置文件读取成功!");
                                        }
                                        
                                        UpdateConfigUI();
                                        //Dispatcher.Invoke(() => CustomMessageBox.Show("配置写入成功!"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("配置读取失败!", "配置读取"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    //debugConfig = ByteArrayToDebugConfig(data);
                                    break;
                                case 0x82:
                                    sSendBack = BytesToStruct<SendBack>(msg, 0);
                                    //byte[] ackTuple = new byte[4] { data[8], data[9], data[10], data[11] };
                                    //int ack = BitConverter.ToInt32(ackTuple, 0);
                                    //Array.Copy(data, 12, msg, 0, length-4);
                                    if (sSendBack.ack == 1)
                                    {
                                        MessageBox.Show("配置写入成功!", "配置写入");
                                        //Dispatcher.Invoke(() => CustomMessageBox.Show("配置写入成功!"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("配置写入失败!", "配置写入"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    //debugConfig = ByteArrayToDebugConfig(data);
                                    break;
                                case 0x83:
                                    break;
                                default:
                                    break;
                            }

                            

                            
                            //smddata = ByteArrayToSMDData(data);

                            //debugConfig = ByteArrayToDebugConfig(buffer);
                            //debugData = ByteArrayToDebugData(buffer);

                        }
                        catch (Exception ex)
                        {
                            AppendLog($"数据拷贝失败: {ex.Message}\r\n");
                        }

                    }

                }
            }
            catch (Exception ex)
            {
                if (_isConnected) // 只有在连接状态下的错误才显示
                {
                    AppendLog($"接收数据错误: {ex.Message}");
                    close_connection();
                    Dispatcher.Invoke(() =>
                    {
                        Connect_Button.IsEnabled = true;
                        Connect_Button.Content = "连接";
                    });
                }
            }
            finally
            {
                // 在UI线程上更新状态
                //Dispatcher.Invoke(() => UpdateUIState(false));
                //CleanupConnection();
                close_connection();
                Dispatcher.Invoke(() =>
                {
                    Connect_Button.IsEnabled = true;
                    Connect_Button.Content = "连接";
                });
            }
        }

        private void ReceiveUdpData()
        {
            try
            {
                while (isRunning)
                {
                    // 接收数据
                    byte[] data = udpServer.Receive(ref remoteEndPoint);

                    // 拷贝到smddata
                    try
                    {
                        byte[] lengthTuple = new byte[4] { data[4], data[5], data[6], data[7] };
                        int length = BitConverter.ToInt32(lengthTuple, 0);

                        byte[] msg = new byte[length];


                        switch (data[3])
                        {
                            case 0x91:
                                Array.Copy(data, 8, msg, 0, length);
                                //smddata = ByteArrayToSMDData(data);
                                robotData_Part1 = BytesToStruct<RoboteData_0x91_Part1>(msg, 0);
                                robotData_Part2 = BytesToStruct<RoboteData_0x91_Part2>(msg, Marshal.SizeOf(typeof(RoboteData_0x91_Part1)));
                                robotData_Part3 = BytesToStruct<RoboteData_0x91_Part3>(msg, Marshal.SizeOf(typeof(RoboteData_0x91_Part1)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part2)));
                                robotData_Part4 = BytesToStruct<RoboteData_0x91_Part4>(msg, Marshal.SizeOf(typeof(RoboteData_0x91_Part1)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part2)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part3)));
                                sDebugStatus = BytesToStruct<DebugStatus>(msg, Marshal.SizeOf(typeof(RoboteData_0x91_Part1)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part2)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part3)) + Marshal.SizeOf(typeof(RoboteData_0x91_Part4)));
                                
                                updatemsg();
                                break;

                            case 0x93:
                                Array.Copy(data, 8, msg, 0, length);
                                AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                break;

                            default:
                                break;

                        }

                        //debugData = ByteArrayToDebugData(data);
                        //Invoke(new Action(() =>
                        //{
                        //    textBox3.AppendText($"FuctionCode:{data[3]}!\r\n");
                        //}));
                        //if (debugData.FunctionCode == 0x0091)
                        //{
                        //    smddata = ByteArrayToSMDData(debugData.FuntionParamter);
                        //}
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"数据拷贝失败: {ex.Message}\r\n");

                    }

                    //string message = Encoding.UTF8.GetString(data);

                    //// 准备显示的信息
                    //string logMessage = $"[{DateTime.Now:HH:mm:ss}] 从 {remoteEndPoint.Address}:{remoteEndPoint.Port} 接收: {message}";

                    //// 跨线程更新UI
                    //Invoke(new Action(() =>
                    //{
                    //    textBox3.AppendText($"收到 {logMessage}\r\n");
                    //    textBox3.SelectionStart = textBox3.Text.Length;
                    //    textBox3.ScrollToCaret();
                    //}));
                }
            }
            catch (SocketException)
            {
                // 服务器关闭时会抛出此异常，属于正常情况
                if (isRunning)
                {
                    AppendLog($"UDP服务器接收数据时发生错误! \r\n");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"错误! {ex.Message}\r\n");
                close_connection();
                Dispatcher.Invoke(() =>
                {
                    Connect_Button.IsEnabled = true;
                    Connect_Button.Content = "连接";
                });

            }
        }
        // 更新配置文件界面
        private void UpdateConfigUI()
        {
            
            Dispatcher.Invoke(() =>
            {
                

                string robot_type_str = System.Text.Encoding.ASCII.GetString(debugConfig.robot_type).TrimEnd('\0');
                if (robot_type_str == "aid")
                {
                    AppendLog($"机器人类型: {robot_type_str}\r\n");
                    config_rb_type_lz.IsChecked = true;
                }
                else
                {
                    config_rb_type_siasun.IsChecked = true;
                }
                config_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.can_device_name).TrimEnd('\0');
                config_can_baud_textbox.Text = debugConfig.can_baud.ToString();
                config_upper_ip_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.udp_server_ip).TrimEnd('\0');
                config_upper_port_textbox.Text = debugConfig.udp_server_port.ToString();
                string ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.ins_type).TrimEnd('\0');
                AppendLog($"InsType:{ins_type}\r\n");
                if (ins_type == "bynav")
                {
                    config_rb_instype_by.IsChecked = true;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo.IsChecked = true;
                }
                config_ins_ip_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.vut_ins_ip).TrimEnd('\0');
                config_ins_port_textbox.Text = debugConfig.vut_ins_port.ToString();
                string spt_ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.spt_ins_type).TrimEnd('\0');

                if (spt_ins_type == "bynav")
                {
                    config_rb_instype_by1.IsChecked = true;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt1.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo1.IsChecked = true;
                }
                config_ins_ip_textbox1.Text = System.Text.Encoding.ASCII.GetString(debugConfig.spt_ins_ip).TrimEnd('\0');
                config_ins_port_textbox1.Text = debugConfig.spt_ins_port.ToString();

                string vt_ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.vt_ins_type).TrimEnd('\0');
                if (vt_ins_type == "bynav")
                {
                    config_rb_instype_by2.IsChecked = true;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt2.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo2.IsChecked = true;
                }
                config_ins_ip_textbox2.Text = System.Text.Encoding.ASCII.GetString(debugConfig.vt_ins_ip).TrimEnd('\0');
                config_ins_port_textbox2.Text = debugConfig.vt_ins_port.ToString();

                config_smd_savedays_textbox.Text = debugConfig.smd_save_days.ToString();
                config_smd_savepath_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.smd_file_save_path).TrimEnd('\0');

                config_log_savedays_textbox.Text = debugConfig.log_save_days.ToString();
                config_log_savepath_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.log_file_save_path).TrimEnd('\0');

                config_data_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.data_can_device_name).TrimEnd('\0');
                config_data_can_baud_textbox.Text = debugConfig.data_can_baud.ToString();

                config_daq_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.daq_can_device_name).TrimEnd('\0');
                config_daq_can_baud_textbox.Text = debugConfig.daq_can_baud.ToString();

                config_headtraker_textbox.Text = debugConfig.headtrakertype.ToString();
                config_dbc_version_textbox.Text = debugConfig.dbc_version.ToString();

                config_save_textbox.Text = debugConfig.save_mode.ToString();

                
                //}));
            });

            

        }
        // 实时数据显示
        private void updatemsg()
        {

            Dispatcher.Invoke(() =>
            {
                robot_type.Text = robotData_Part1.Soft_Vertion.ToString();
                data_vut_latitude.Text = robotData_Part2.VUTMP_Latitude.ToString("F8");
                data_vut_longitude.Text = robotData_Part2.VUTMP_Longitude.ToString("F8");
                data_vut_azimuth.Text = robotData_Part2.VUTMP_Azimuth.ToString("F2");
                data_vut_velocity.Text = robotData_Part2.VUTMP_Vertical_velocity.ToString("F2");
                //data_vut_actualx.Text = robotData_Part2.VUTMP_Actual_X.ToString("F2");
                //data_vut_actualy.Text = robotData_Part2.VUTMP_Actual_Y.ToString("F2");

                data_spt_latitude1.Text = robotData_Part3.SPTMP_Latitude.ToString("F8");
                data_spt_longitude1.Text = robotData_Part3.SPTMP_Longitude.ToString("F8");
                data_spt_azimuth1.Text = robotData_Part3.SPTMP_Azimuth.ToString("F2");
                data_spt_velocity1.Text = robotData_Part3.SPTMP_Vertical_velocity.ToString("F2");

                data_vt_latitude.Text = robotData_Part4.SubMP_Latitude.ToString("F8");
                data_vt_longitude.Text = robotData_Part4.SubMP_Longitude.ToString("F8");
                data_vt_azimuth.Text = robotData_Part4.SubMP_Azimuth.ToString("F2");
                data_vt_velocity.Text = robotData_Part4.SubMP_Vertical_velocity.ToString("F2");
                //data_spt_actualx1.Text = robotData_Part3.SPTMP_Actual_X.ToString("F2");
                //data_spt_actualy1.Text = robotData_Part3.SPTMP_Actual_Y.ToString("F2");
                //AppendLog($"vut_lat:{robotData_Part2.VUTMP_Vertical_velocity.ToString("F8")}, vut_lon:{robotData_Part2.VUTMP_Longitude.ToString("F8")}\r\n");
                //AppendLog($"spt_lat:{robotData_Part3.SPTMP_Vertical_velocity.ToString("F8")}, spt_lon:{robotData_Part3.SPTMP_Longitude.ToString("F8")}\r\n");
                //AppendLog($"vt_lat:{robotData_Part4.SubMP_Vertical_velocity.ToString("F8")}, vt_lon:{robotData_Part2.VUTMP_Longitude.ToString("F8")}\r\n");
                //AppendLog("-----------------------------------\r\n");

                //AppendLog($"UTC:{robotData_Part1.Utc_time.ToString("F4")}, HandShake:{robotData_Part4.uiHandShankStat.ToString()}, SR_EN_State:{robotData_Part1.SR_uiMotEnStat.ToString()}, BR_EN_State:{robotData_Part1.BR_uiMotEnStat.ToString()}, Speed:{robotData_Part2.VUTMP_Vertical_velocity.ToString("F3")}\r\n");
                //AppendLog($"robotData_part1:{robotData_Part1.SBV}\r\n");
                //textBox3.AppendText($"robotData_part1:{robotData_Part1.Soft_Vertion}\r\n");
                //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Latitude}\r\n");
                //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Longitude}\r\n");
                //textBox1.SelectionStart = textBox1.TextLength;
                //textBox1.ScrollToCaret();
                if (BLH_Origin.Lon != 0)
                {
                    UpdateA1OnCanvas();
                }
            });
        }

        private void close_connection()
        {
            _isConnected = false;
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
            if (_client != null)
            {
                _client.Close();
                _client = null;
            }

            isRunning = false;

            if (udpServer != null)
            {
                udpServer.Close();
            }

            if (receiveThread != null && receiveThread.IsAlive)
            {
                receiveThread.Join(1000); // 等待线程结束
            }
        }

        private T BytesToStruct<T>(byte[] ins, int offset = 6) where T : struct
        {
            return BytesAndStructHelper.BytesToStruct<T>(ins, offset, false);
        }
        public static byte[] ConvertStringToFixedByteArray(string input, int fixedLength, Encoding encoding = null, byte paddingByte = 0)
        {
            if (fixedLength < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedLength), "固定长度不能为负数");

            // 使用指定编码或默认UTF8
            encoding ??= Encoding.UTF8;

            // 如果输入字符串为null，视为空字符串处理
            input ??= string.Empty;

            // 将字符串转换为字节数组
            byte[] originalBytes = encoding.GetBytes(input);

            // 创建目标固定长度数组
            byte[] fixedBytes = new byte[fixedLength];

            // 如果需要填充，先初始化所有字节为填充值
            if (originalBytes.Length < fixedLength)
                Array.Fill(fixedBytes, paddingByte);

            // 复制原始字节（如果过长则截断）
            int copyLength = Math.Min(originalBytes.Length, fixedLength);
            Array.Copy(originalBytes, fixedBytes, copyLength);

            return fixedBytes;
        }

        private DebugConfig ByteArrayToDebugConfig(byte[] data)
        {
            int size = Marshal.SizeOf(typeof(DebugConfig));
            // AppendLog($"DebugConfigLength: {size}\r\n");
            if (data.Length < size)
                throw new ArgumentException("数据长度不足，无法转换为DebugConfig结构体");

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(data, 0, ptr, size);
                return (DebugConfig)Marshal.PtrToStructure(ptr, typeof(DebugConfig));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }


        public void AppendLog(string log)
        {
            //log_textbox.AppendText(log + Environment.NewLine);
            //log_textbox.ScrollToEnd();
            if (log_textbox.Dispatcher.CheckAccess())
            {
                log_textbox.AppendText(log + Environment.NewLine);
                log_textbox.ScrollToEnd();
            }
            else
            {
                log_textbox.Dispatcher.Invoke(() => AppendLog(log));
            }
        }

        // 读取配置文件
        private void read_cfg_Click(object sender, RoutedEventArgs e)
        {
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01; // 假设设备类型为1
            debugData.FunctionCode = 0x0081; // 假设功能码为1
            string paramStr = "CFG_READ";

            // 将字符串转换为字节数组并赋值给FuntionParameter
            byte[] paramBytes = Encoding.UTF8.GetBytes(paramStr);
            debugData.Length = (uint)paramBytes.Length;
            // 确保不超过数组大小限制
            // int copyLength = Math.Min(paramBytes.Length, debugData.FuntionParamter.Length);
            Array.Copy(paramBytes, debugData.FuntionParamter, debugData.Length);
            // debugData.Length = (uint)copyLength; // 设置实际参数长度

            // 通过TCP发送debugData
            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送 {dataToSend.Length} 字节数据\r\n");
                }
                catch (Exception ex)
                {
                    AppendLog($"发送失败: {ex.Message}\r\n");
                }
            }
            else
            {
                AppendLog("TCP连接未建立，无法发送数据\r\n");
            }
        }

        // 写入配置文件
        private void write_cfg_Click(object sender, RoutedEventArgs e)
        {
            read_cfg.IsEnabled = false;
            if (ReadCfg())
            {
                debugData.Header1 = 0xAA;
                debugData.Header2 = 0x55;
                debugData.DeviceType = 0x01; // 假设设备类型为1
                debugData.FunctionCode = 0x0082; // 假设功能码为1

                // 将DebugConfig结构体序列化为字节数组
                byte[] debugConfigBytes = StructStreamReader.StructToByteArray(debugConfig);

                // 确保不超过FuntionParamter的长度
                int copyLength = Math.Min(debugConfigBytes.Length, debugData.FuntionParamter.Length);
                Array.Copy(debugConfigBytes, 0, debugData.FuntionParamter, 0, copyLength);
                debugData.Length = (uint)copyLength; // 设置实际参数长度

                // 通过TCP发送debugData
                if (_client != null && _client.Connected && _stream != null)
                {
                    try
                    {
                        byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                        _stream.Write(dataToSend, 0, dataToSend.Length);
                        AppendLog($"已发送 {dataToSend.Length} 字节数据\r\n");
                        read_cfg.IsEnabled = true;
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"发送失败: {ex.Message}\r\n");
                    }
                }
                else
                {
                    AppendLog("TCP连接未建立，无法发送数据\r\n");
                    close_connection();
                    Connect_Button.IsEnabled = true;
                    Connect_Button.Content = "连接";
                    read_cfg.IsEnabled = true;
                }
            }
            else
            {
                read_cfg.IsEnabled = true;
                return;
            }
                
        }

        // 读取控制参数文件
        private void Read_Control_Param_Button_Click(object sender, RoutedEventArgs e)
        {
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01; // 假设设备类型为1
            debugData.FunctionCode = 0x0083; // 假设功能码为1
            string paramStr = "CFG_Control_READ";

            // 将字符串转换为字节数组并赋值给FuntionParameter
            byte[] paramBytes = Encoding.UTF8.GetBytes(paramStr);
            debugData.Length = (uint)paramBytes.Length;
            // 确保不超过数组大小限制
            // int copyLength = Math.Min(paramBytes.Length, debugData.FuntionParamter.Length);
            Array.Copy(paramBytes, debugData.FuntionParamter, debugData.Length);
            // debugData.Length = (uint)copyLength; // 设置实际参数长度

            // 通过TCP发送debugData
            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送 {dataToSend.Length} 字节数据\r\n");
                }
                catch (Exception ex)
                {
                    AppendLog($"发送失败: {ex.Message}\r\n");
                }
            }
            else
            {
                AppendLog("TCP连接未建立，无法发送数据\r\n");
            }
        }

        private void Write_Control_Param_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Temporary_Rread_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Temporary_Write_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        // 从UI读取配置文件内容到debugConfig结构体
        private bool ReadCfg()
        {
            if (config_rb_type_lz.IsChecked == true)
            {
                debugConfig.robot_type = ConvertStringToFixedByteArray("aid", 8);
                //debugConfig.robot_type = Encoding.ASCII.GetBytes("aid");
            }
            else
            {
                debugConfig.robot_type = ConvertStringToFixedByteArray("siasun", 8);
                //debugConfig.robot_type = Encoding.ASCII.GetBytes("siasun");
            }
            debugConfig.can_device_name = ConvertStringToFixedByteArray(config_can_device_textbox.Text, 16);
            //debugConfig.can_device_name = Encoding.ASCII.GetBytes(config_robot_can_textbox.Text);
            debugConfig.can_baud = int.Parse(config_can_baud_textbox.Text);


            //debugConfig.local_ip1 = ConvertStringToFixedByteArray("192.168.1.210", 16);
            //debugConfig.local_ip2 = ConvertStringToFixedByteArray("192.168.1.210", 16);

            debugConfig.tcp_local_port = 5000;
            debugConfig.tcp_local_port1 = 8201;

            debugConfig.udp_server_ip = ConvertStringToFixedByteArray(config_upper_ip_textbox.Text, 16);
            debugConfig.udp_server_port = int.Parse(config_upper_port_textbox.Text);

            debugConfig.default_path_file_name = ConvertStringToFixedByteArray("/home/root/custapp/configure/CPFA.XYZ", 64);

            /// VUT惯导配置
            if (config_rb_instype_by.IsChecked == true)
            {

                debugConfig.ins_type = ConvertStringToFixedByteArray("bynav", 8);
            }
            else if (config_rb_instype_rt.IsChecked == true)
            {
                debugConfig.ins_type = ConvertStringToFixedByteArray("rt", 8);
            }
            else
            {
                debugConfig.ins_type = ConvertStringToFixedByteArray("shibo", 8);
            }

            debugConfig.vut_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox.Text, 16);
            debugConfig.vut_ins_port = int.Parse(config_ins_port_textbox.Text);
            debugConfig.agreement = ConvertStringToFixedByteArray("udp", 8);
            debugConfig.message_id = 0;
            debugConfig.region = 8;
            debugConfig.sys_time_enable = 0;

            /// SPT 惯导配置
            if (config_rb_instype_by1.IsChecked == true)
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("bynav", 8);
            }
            else if (config_rb_instype_rt1.IsChecked == true)
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("rt", 8);
            }
            else
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("shibo", 8);
            }
            debugConfig.spt_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox1.Text, 16);
            debugConfig.spt_ins_port = int.Parse(config_ins_port_textbox1.Text);
            debugConfig.spt_agreement = ConvertStringToFixedByteArray("udp", 8);
            debugConfig.spt_message_id = 0;
            debugConfig.spt_region = 8;
            debugConfig.spt_sys_time_enable = 0;

            /// VT惯导配置
            if (config_rb_instype_by2.IsChecked == true)
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("bynav", 8);
            }
            else if (config_rb_instype_rt2.IsChecked == true)
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("rt", 8);
            }
            else
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("shibo", 8);
            }
            debugConfig.vt_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox2.Text, 16);
            debugConfig.vt_ins_port = int.Parse(config_ins_port_textbox2.Text);
            debugConfig.vt_agreement = ConvertStringToFixedByteArray("udp", 8);
            debugConfig.vt_message_id = 0;
            debugConfig.vt_region = 8;
            debugConfig.vt_sys_time_enable = 0;

            // 检查IP和端口组合是否相同
            if ((config_ins_ip_textbox.Text == config_ins_ip_textbox1.Text && config_ins_port_textbox.Text == config_ins_port_textbox1.Text) ||
                (config_ins_ip_textbox.Text == config_ins_ip_textbox2.Text && config_ins_port_textbox.Text == config_ins_port_textbox2.Text) ||
                (config_ins_ip_textbox1.Text == config_ins_ip_textbox2.Text && config_ins_port_textbox1.Text == config_ins_port_textbox2.Text))
            {
                MessageBox.Show("错误：VUT、SPT和VT惯导的IP地址和端口不能同时相同！", "配置错误");
                return false;
            }
            /// SMD
            debugConfig.smd_enable_circle_buffer = 1;
            debugConfig.smd_enable_file_header = 1;
            debugConfig.smd_save_days = int.Parse(config_smd_savedays_textbox.Text);
            debugConfig.smd_file_save_path = ConvertStringToFixedByteArray(config_smd_savepath_textbox.Text, 64);

            ///log
            debugConfig.log_enable_circle_buffer = 1;
            debugConfig.log_enable_file_header = 1;
            debugConfig.log_save_days = int.Parse(config_log_savedays_textbox.Text);
            debugConfig.log_file_save_path = ConvertStringToFixedByteArray(config_log_savepath_textbox.Text, 64);

            /// 数据解算
            debugConfig.data_udp_local_port = 9001;
            debugConfig.data_can_device_name = ConvertStringToFixedByteArray(config_data_can_device_textbox.Text, 8);
            debugConfig.data_can_baud = int.Parse(config_data_can_baud_textbox.Text);
            debugConfig.data_save_days = 30;
            debugConfig.data_file_save_path = ConvertStringToFixedByteArray("/home/root/custapp/asc", 64);

            /// FCW
            debugConfig.fcw_enable_circle_buffer = 1;
            debugConfig.fcw_enable_file_header = 1;
            debugConfig.fcw_save_days = 30;
            debugConfig.fcw_file_save_path = ConvertStringToFixedByteArray("/home/root/custapp/fcw", 64);

            /// DAQ数采
            debugConfig.daq_can_device_name = ConvertStringToFixedByteArray(config_daq_can_device_textbox.Text, 8);
            debugConfig.daq_can_baud = int.Parse(config_daq_can_baud_textbox.Text);

            ///Headertraker
            debugConfig.headtrakertype = int.Parse(config_headtraker_textbox.Text);
            debugConfig.dbc_version = int.Parse(config_dbc_version_textbox.Text);

            debugConfig.save_mode = uint.Parse(config_save_textbox.Text);

            AppendLog($"bottom->\tRobot_Type:{System.Text.Encoding.ASCII.GetString(debugConfig.robot_type).TrimEnd('\0')}");
            AppendLog($"\tCan_Device:{System.Text.Encoding.ASCII.GetString(debugConfig.can_device_name).TrimEnd('\0')}");
            AppendLog($"\tCan_Baud:{debugConfig.can_baud}\r\n");

            AppendLog($"NET->\t LOCAL_IP1:{System.Text.Encoding.ASCII.GetString(debugConfig.local_ip1).TrimEnd('\0')}");
            AppendLog($"\t LOCAL_IP2:{System.Text.Encoding.ASCII.GetString(debugConfig.local_ip2).TrimEnd('\0')}\r\n");

            AppendLog($"UPPER->\tTCP_LOCAL_PORT:{debugConfig.tcp_local_port.ToString()},TCP_LOCAL_PORT1:{debugConfig.tcp_local_port1}\r\n");
            AppendLog($"\tUDP_SERVER_IP:{System.Text.Encoding.ASCII.GetString(debugConfig.udp_server_ip).TrimEnd('\0')},UDP_SERVER_PORT:{debugConfig.udp_server_port}\r\n");
            AppendLog($"\tDEFAULT_PATH_FILE_NAME:{System.Text.Encoding.ASCII.GetString(debugConfig.default_path_file_name).TrimEnd('\0')}\r\n");

            AppendLog($"INS->\tInsType:{System.Text.Encoding.ASCII.GetString(debugConfig.udp_server_ip).TrimEnd('\0')}");
            return true;
        }

        private void log_clear_button_Click(object sender, RoutedEventArgs e)
        {
            log_textbox.Clear();
        }

        private void log_save_button_Click(object sender, RoutedEventArgs e)
        {
            // 弹出保存文件对话框
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存日志文件",
                Filter = "日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, log_textbox.Text, Encoding.UTF8);
                    MessageBox.Show("日志保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"日志保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void origin_cfg_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {

                config_rb_type_siasun.IsChecked = true;

                config_can_device_textbox.Text = "can1";
                config_can_baud_textbox.Text = "500";
                config_upper_ip_textbox.Text = "192.168.1.100";
                config_upper_port_textbox.Text = "2122";
                // VUT惯导默认参数
                config_rb_instype_rt.IsChecked = true;

                config_ins_ip_textbox.Text = "192.168.1.110";
                config_ins_port_textbox.Text = "3001";

                //SPT惯导默认参数
                config_rb_instype_by1.IsChecked = true;
                config_ins_ip_textbox1.Text = "192.168.1.121";
                config_ins_port_textbox1.Text = "3002";

                //VT惯导默认参数
                config_rb_instype_by2.IsChecked = true;

                config_ins_ip_textbox2.Text = "192.168.1.126";
                config_ins_port_textbox2.Text = "3002";

                //SMD默认参数
                config_smd_savedays_textbox.Text = "30";
                config_smd_savepath_textbox.Text = "/home/root/custapp/smd";

                config_log_savedays_textbox.Text = "30";
                config_log_savepath_textbox.Text = "/home/root/custapp/log";

                config_data_can_device_textbox.Text = "can0";
                config_data_can_baud_textbox.Text = "500";

                config_daq_can_device_textbox.Text = "can0";
                config_daq_can_baud_textbox.Text = "500";

                config_headtraker_textbox.Text = "0";
                config_dbc_version_textbox.Text = "2";

                config_save_textbox.Text = "0";


                //}));
            });
        }

        private void Restart_Button_Click(object sender, RoutedEventArgs e)
        {
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01; // 假设设备类型为1
            debugData.FunctionCode = 0x0085; // 假设功能码为1

            // 将DebugConfig结构体序列化为字节数组

            byte[] debugConfigBytes = "restart".Select(c => (byte)c).ToArray();
            // 确保不超过FuntionParamter的长度
            int copyLength = Math.Min(debugConfigBytes.Length, debugData.FuntionParamter.Length);
            Array.Copy(debugConfigBytes, 0, debugData.FuntionParamter, 0, copyLength);
            debugData.Length = (uint)copyLength; // 设置实际参数长度

            // 通过TCP发送debugData
            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送 {dataToSend.Length} 字节数据\r\n");
                    read_cfg.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    AppendLog($"发送失败: {ex.Message}\r\n");
                }
            }
            else
            {
                AppendLog("TCP连接未建立，无法发送数据\r\n");
                close_connection();
                Connect_Button.IsEnabled = true;
                Connect_Button.Content = "连接";
            }
        }

        private void Update_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Refresh_Track_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Refresh_Track(string pathfile)
        {


            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. SFTP下载文件到assets目录
                    string localFilePath = DownloadFileFromSftp(pathfile);
                    // 2. 解析文档中的Track轨迹
                    List<TrackPoint> trackPoints = ParseTrackFromFile(localFilePath);
                    CalculateImgScaleAndOffset();

                    // 步骤3：计算轨迹起点（Canvas中的实际坐标）和轨迹缩放比例
                    CalculateTrackStartAndScale();

                    // 3. 在Canvas上绘制轨迹
                    DrawTrackOnCanvas(trackPoints);

                    MessageBox.Show("轨迹显示成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            
        }


        private string DownloadFileFromSftp(string pathfile)
        {
            // SFTP配置（根据实际情况修改用户名和密码）
            string sftpHost = server_ip.Text;
            int sftpPort = 22; // 默认SFTP端口
            string sftpUsername = "wch"; // 通常SFTP用户名是root，需确认
            string sftpPassword = "1234"; // 替换为实际SFTP密码
            string remoteFilePath = "/home/wch/code/lz_driver_robot/config/"; // 远程文件路径

            // 本地路径：项目输出目录下的assets文件夹
            string localDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            // 若assets文件夹不存在则创建
            if (!Directory.Exists(localDir))
            {
                AppendLog("Mkdir\r\n");
                Directory.CreateDirectory(localDir);
            }
            string localFilePath = System.IO.Path.Combine(localDir, pathfile);
            remoteFilePath = remoteFilePath + pathfile;

            AppendLog($"LocalFilePath {localFilePath}\r\n");
            AppendLog($"RemoteFilePath {remoteFilePath}\r\n");
            SftpClient sftpClient = null;
            try
            {
                // 1. 初始化SFTP客户端
                sftpClient = new SftpClient(sftpHost, sftpPort, sftpUsername, sftpPassword);
                sftpClient.Connect(); // 建立连接

                // 2. 验证远程文件是否存在
                if (!sftpClient.Exists(remoteFilePath))
                {
                    MessageBox.Show($"远程文件不存在！路径：{remoteFilePath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // 3. 下载远程文件到本地临时路径（用FileStream确保文件流安全释放）
                using (var localFileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
                {
                    sftpClient.DownloadFile(remoteFilePath, localFileStream);
                }
                MessageBox.Show($"文件已从服务器下载到本地临时路径：\n{localFilePath}", "下载成功", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                // 捕获常见异常（连接失败、权限不足、网络超时等）
                string errorMsg = ex switch
                {
                    SshConnectionException => "SFTP连接失败！请检查服务器IP、端口、用户名密码是否正确，或服务器是否开启SSH服务。",
                    SftpPermissionDeniedException => "权限不足！无法读取远程文件，请确认用户有 /home/root/ 路径的访问权限。",
                    IOException => "文件读写失败！请检查本地临时路径是否可写，或远程文件是否被占用。",
                    _ => $"未知错误：{ex.Message}"
                };
                MessageBox.Show(errorMsg, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 5. 清理资源：关闭SFTP连接 + （可选）删除本地临时文件
                if (sftpClient != null && sftpClient.IsConnected)
                {
                    sftpClient.Disconnect();
                }
                // 可选：读取完成后删除临时文件（避免占用空间）
                //if (File.Exists(localTempPath))
                //{
                //    File.Delete(localTempPath);
                //    // MessageBox.Show("本地临时文件已清理", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                //}
            }

            return localFilePath;

        }

        private List<TrackPoint> ParseTrackFromFile(string xyzFilePath)
        {
            List<TrackPoint> trackPoints = new List<TrackPoint>();

            if (!File.Exists(xyzFilePath))
            {
                throw new FileNotFoundException("XYZ文件不存在");
            }

            var lines = File.ReadAllLines(xyzFilePath);
            bool isInOriginSection = false; // 是否在[Origin]节内
            bool isInTrackSection = false; // 是否在[Track]节内
            int pointsCount = 0;
            int currentPointIndex = -1;

            foreach (string line in lines)
            {
                string cleanLine = line.Trim();

                // 1. 处理节的切换逻辑
                if (cleanLine == "[Origin]")
                {
                    isInOriginSection = true;
                    isInTrackSection = false;
                    continue;
                }
                else if (cleanLine == "[Track]")
                {
                    isInTrackSection = true;
                    isInOriginSection = false;
                    continue;
                }
                else if (cleanLine.StartsWith("[") && (isInOriginSection || isInTrackSection))
                {
                    // 遇到新的节，退出当前节
                    isInOriginSection = false;
                    isInTrackSection = false;
                    continue;
                }

                // 2. 解析[Origin]节内容
                if (isInOriginSection && !string.IsNullOrEmpty(cleanLine) && cleanLine.Contains("="))
                {
                    string[] keyValue = cleanLine.Split('=');
                    string key = keyValue[0].Trim();
                    double value = double.Parse(keyValue[1].Trim());

                    // 根据键名赋值到OriginInfo对应属性
                    switch (key)
                    {
                        case "Lat": BLH_Origin.Lat = value; break;
                        case "Lon": BLH_Origin.Lon = value; break;
                        case "Height": BLH_Origin.Altitude = value; break;
                        case "Azimuth": BLH_Origin.Heading = value; break;
                    }
                }

                // 3. 解析[Track]节内容（保留原有轨迹点解析逻辑）
                if (isInTrackSection && !string.IsNullOrEmpty(cleanLine))
                {
                    // 解析轨迹点总数
                    if (cleanLine.StartsWith("Points ="))
                    {
                        pointsCount = int.Parse(cleanLine.Split('=')[1].Trim());
                        continue;
                    }

                    // 解析每个轨迹点的属性（X/Y/Time等）
                    if (cleanLine.Contains("="))
                    {
                        string[] keyValue = cleanLine.Split('=');
                        string key = keyValue[0].Trim();
                        double value = double.Parse(keyValue[1].Trim());

                        if (key.Length >= 2 && int.TryParse(key.Substring(1), out int pointIndex))
                        {
                            // 初始化轨迹点（若索引超出当前列表长度）
                            if (trackPoints.Count <= pointIndex)
                            {
                                trackPoints.Add(new TrackPoint());
                            }

                            // 根据键名首字符，赋值到轨迹点对应属性
                            switch (key[0])
                            {
                                case 'X': trackPoints[pointIndex].X = value; break;
                                case 'Y': trackPoints[pointIndex].Y = value; break;
                                case 'T':
                                    if (key.StartsWith("Time")) trackPoints[pointIndex].Time = value;
                                    else if (key.StartsWith("Theta")) trackPoints[pointIndex].Theta = value;
                                    break;
                                case 'V': trackPoints[pointIndex].Vel = value; break;
                                case 'A': trackPoints[pointIndex].Acc = value; break;
                                case 'C': trackPoints[pointIndex].Curvature = value; break;
                            }
                        }
                    }
                }
            }

            // 解析完成后，验证数据
            if (trackPoints.Count == 0)
            {
                throw new Exception("解析到的轨迹点数量为0，请检查文档格式");
            }
            bool hasValidPoints = false;
            foreach (var point in trackPoints)
            {
                if (point.X != 0 || point.Y != 0)
                {
                    hasValidPoints = true;
                    break;
                }
            }
            if (!hasValidPoints)
            {
                throw new Exception("所有轨迹点的X/Y均为0，无法绘制可见轨迹");
            }
            AppendLog($"Origin_Lat{origin.Lat},Origin_Lon{origin.Lon},Origin_Heading{origin.Azimuth}\r\n");
            return trackPoints;
        }

        private void DrawTrackOnCanvas(List<TrackPoint> trackPoints)
        {
            TrackCanvas.Children.Clear();

            // 1. 绘制轨迹线段（红色，2px宽）
            for (int i = 0; i < trackPoints.Count - 1; i++)
            {
                var p1 = trackPoints[i];
                var p2 = trackPoints[i + 1];

                // 米级轨迹点 → Canvas像素坐标（相对于起点偏移）
                double x1 = CanvasStartPoint.X + p1.X*TrackScale_X;
                double y1 = CanvasStartPoint.Y + p1.Y*TrackScale_Y;
                double x2 = CanvasStartPoint.X + p2.X*TrackScale_X;
                double y2 = CanvasStartPoint.Y + p2.Y*TrackScale_Y;

                var line = new Line
                {
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2,
                    StrokeLineJoin = PenLineJoin.Round // 线段连接点圆滑
                };
                TrackCanvas.Children.Add(line);
            }
        }


        private static void ParseProperty(string line, string propertyName, List<TrackPoint> points, Action<TrackPoint, double> setter)
        {
            if (line.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    // 提取索引号
                    var indexPart = parts[0].Substring(propertyName.Length);
                    if (int.TryParse(indexPart, out int index) && index >= 0 && index < points.Count)
                    {
                        if (double.TryParse(parts[1].Trim(), out double value))
                        {
                            setter(points[index], value);
                        }
                    }
                }
            }
        }
        

        private double[] ConvertLatLonToUtm(double lat, double lon)
        {
            AppendLog($"ConvertLatLonToUtm Lat:{lat},Lon:{lon}\r\n");
            double[] wgs84Point = { lon, lat, 0 }; // 经、纬、高
            double[] utmPoint = _wgs84ToUtm.MathTransform.Transform(wgs84Point);
            return new[] { utmPoint[0], utmPoint[1] }; // 返回UTM的X、Y
        }

        /// <summary>
        /// 计算A1的平面化坐标并绘制到Canvas
        /// </summary>
        /// private CoordinateTransformationFactory _ctf;
        private void UpdateA1OnCanvas()
        {
            // 步骤1：移除Canvas中所有标记为"A1"的旧元素（原有点和航向线）
            List<UIElement> elementsToRemove = new List<UIElement>();
            foreach (UIElement element in TrackCanvas.Children)
            {
                if (element is FrameworkElement fe && fe.Tag != null && fe.Tag.ToString() == "A1")
                {
                    elementsToRemove.Add(element);
                }
            }
            foreach (UIElement element in elementsToRemove)
            {
                TrackCanvas.Children.Remove(element);
            }

            // 步骤2：更新车辆的经纬度、航向角（从数据/GPS获取）
            BLH_VUT.Lat = robotData_Part2.VUTMP_Latitude;
            BLH_VUT.Lon = robotData_Part2.VUTMP_Longitude;
            BLH_VUT.Altitude = 0;
            BLH_VUT.Heading = robotData_Part2.VUTMP_Azimuth; // 航向角（假设为**弧度**，若为角度需转弧度）

            // 步骤3：将经纬度转换为Canvas平面坐标（与原有逻辑一致）
            GIS.Complanation(BLH_Origin, BLH_VUT, out VUT_XYZ);
            // AppendLog($"X:{VUT_XYZ.X}Y:{VUT_XYZ.Y}");
            double dx = VUT_XYZ.X;
            double dy = VUT_XYZ.Y;

            double scale = 10.0; // 1米 → 10像素（可按需调整）
            //double canvasCenterX = TrackCanvas.ActualWidth / 2;
            //double canvasCenterY = TrackCanvas.ActualHeight / 2;
            double canvasX = CanvasStartPoint.X + dx * scale;
            double canvasY = CanvasStartPoint.Y - dy * scale; // Canvas Y轴向下，取反

            // 步骤4：创建车辆图片元素
            Image carImage = new Image();
            // 加载PNG图片（路径需根据项目实际情况调整，确保图片“生成操作”为“Resource”）
            carImage.Source = new BitmapImage(new Uri("pack://application:,,,/assets/VUT.png"));
            carImage.Width = 30;  // 车辆图片宽度（按需调整，保持比例）
            carImage.Height = 20; // 车辆图片高度（按需调整，保持比例）

            // 步骤5：计算车辆旋转角度（将弧度航向角转为角度，用于RotateTransform）
            double rotationAngle = (BLH_VUT.Heading - BLH_Origin.Heading) * (180 / Math.PI);
            // 若BLH_VUT.Heading本身是“角度”，则无需转弧度，直接用：
            // double rotationAngle = BLH_VUT.Heading - BLH_Origin.Heading;

            // 步骤6：设置“围绕图片中心旋转”的变换
            RotateTransform rotateTransform = new RotateTransform(
                rotationAngle,       // 旋转角度
                carImage.Width / 2,  // 旋转中心X（图片中心）
                carImage.Height / 2  // 旋转中心Y（图片中心）
            );
            carImage.RenderTransform = rotateTransform;

            // 步骤7：设置图片在Canvas中的位置（让图片中心与计算出的canvasX/Y对齐）
            Canvas.SetLeft(carImage, canvasX - carImage.Width / 2);
            Canvas.SetTop(carImage, canvasY - carImage.Height / 2);

            // 步骤8：标记图片，方便后续移除旧元素
            carImage.Tag = "A1";

            // 步骤9：将车辆图片添加到Canvas
            TrackCanvas.Children.Add(carImage);
        }

        private void UpdateB1OnCanvas()
        {
            // 模拟A1的航向角（实际需从GPS或数据中获取）
            //double A1Heading = 40.0; // 示例：A1的航向角（单位：度）
            List<UIElement> elementsToRemove = new List<UIElement>();
            foreach (UIElement element in TrackCanvas.Children)
            {
                // 筛选Tag为"A1Point"的元素
                if (element is FrameworkElement fe && fe.Tag  != null && fe.Tag.ToString() == "A1")
                {
                    elementsToRemove.Add(element);
                }
            }
            // 批量移除（避免遍历中修改集合报错）
            foreach (UIElement element in elementsToRemove)
            {
                TrackCanvas.Children.Remove(element);
            }

            BLH_VUT.Lat = robotData_Part2.VUTMP_Latitude;
            BLH_VUT.Lon = robotData_Part2.VUTMP_Longitude;
            BLH_VUT.Altitude = 0;
            BLH_VUT.Heading = robotData_Part2.VUTMP_Azimuth;


            GIS.Complanation(BLH_Origin, BLH_VUT, out VUT_XYZ);
            AppendLog($"X:{VUT_XYZ.X}Y:{VUT_XYZ.Y}");
            double dx = VUT_XYZ.X;
            double dy = VUT_XYZ.Y;


            double scale = 10.0; // 1米 → 10像素（可根据需求调整）
            double canvasCenterX = TrackCanvas.ActualWidth / 2;
            double canvasCenterY = TrackCanvas.ActualHeight / 2;
            double canvasX = canvasCenterX + dx * scale;
            double canvasY = canvasCenterY - dy * scale; // Canvas Y轴向下，需取反

            // 绘制A1点
            DrawPoint(canvasX, canvasY, Brushes.Red, "A1");

            // 可选：绘制A1的航向指示（用短线段表示航向）
            double headingLineLen = 20; // 航向指示线段长度（像素）
            double headingLineX = canvasX + headingLineLen * Math.Cos(BLH_VUT.Heading - BLH_Origin.Heading);
            double headingLineY = canvasCenterY - headingLineLen * Math.Sin(BLH_VUT.Heading - BLH_Origin.Heading);
            Line headingLine = new Line
            {
                X1 = canvasX,
                Y1 = canvasY,
                X2 = headingLineX,
                Y2 = headingLineY,
                Stroke = Brushes.Orange,
                StrokeThickness = 2,
                Tag = "A1"
            };
            TrackCanvas.Children.Add(headingLine);
        }

        /// <summary>
        /// 在Canvas上绘制带标签的点
        /// </summary>
        private void DrawPoint(double x, double y, Brush brush, string label)
        {
            // 绘制点（椭圆）
            Ellipse ellipse = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = brush,
                Margin = new Thickness(x - 4, y - 4, 0, 0), // 椭圆中心对齐坐标
                Tag = label
            };
            TrackCanvas.Children.Add(ellipse);

        }


        /// 从键值对字典中获取指定键的值，并转换为目标类型
        /// </summary>
        private static T GetValueFromKv<T>(Dictionary<string, string> kvPairs, string key, Func<string, T> converter)
        {
            if (!kvPairs.TryGetValue(key, out string valueStr))
                throw new Exception($"[Track]区块中缺失键：{key}");

            try
            {
                return converter(valueStr);
            }
            catch
            {
                throw new Exception($"[Track]区块中键{key}的值{valueStr}无法转换为{typeof(T).Name}类型");
            }
        }

        private void CalculateImgScaleAndOffset()
        {
            // 获取Canvas实际尺寸（需在Window加载后获取，否则为0）
            double canvasWidth = TrackCanvas.ActualWidth;
            double canvasHeight = TrackCanvas.ActualHeight;

            // 1. 计算Uniform缩放比例（取宽/高方向缩放的最小值，避免图片拉伸）
            ImgScale_X = canvasWidth / OriginalImgWidth;
            ImgScale_Y = canvasHeight / OriginalImgHeight;
            

            // 2. 计算图片在Canvas中的偏移（实现居中显示）
            double imgDisplayWidth = OriginalImgWidth * ImgScale_X;
            double imgDisplayHeight = OriginalImgHeight * ImgScale_Y;
            double offsetX = (canvasWidth - imgDisplayWidth) / 2; // 水平居中偏移
            double offsetY = (canvasHeight - imgDisplayHeight) / 2; // 垂直居中偏移
            ImgOffset = new Point(offsetX, offsetY);
        }


        // 5. 计算轨迹起点与缩放比例（匹配3.5m车道宽）
        private void CalculateTrackStartAndScale()
        {
            // 1. 计算轨迹起点在Canvas中的实际坐标
            // 逻辑：原始图片中的起点坐标 → 缩放后坐标 → 叠加图片偏移
            double scaledStartX = OriginalStartPointX * ImgScale_X;
            double scaledStartY = Original4thLaneY * ImgScale_Y;
            CanvasStartPoint = new Point(
                scaledStartX + ImgOffset.X,
                scaledStartY + ImgOffset.Y
            );

            // 2. 计算轨迹缩放比例（米→像素）
            // 逻辑：原始图片中1车道宽（像素）对应实际3.5m → 1米对应多少像素
            TrackScale_X = OriginalLanePixelWidth * ImgScale_X / ActualLaneWidth;
            TrackScale_Y = OriginalLanePixelWidth * ImgScale_Y / ActualLaneWidth;
            AppendLog($"ImgScale:{ImgScale_X},TrackScale:{TrackScale_X}");
        }
        public void Refresh_Track_Button_Click_1(object sender, RoutedEventArgs e)
{
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01; // 假设设备类型为1
            debugData.FunctionCode = 0x0075; // 假设功能码为1
            string paramStr = "PATH_READ";

            // 将字符串转换为字节数组并赋值给FuntionParameter
            byte[] paramBytes = Encoding.UTF8.GetBytes(paramStr);
            debugData.Length = (uint)paramBytes.Length;
            // 确保不超过数组大小限制
            // int copyLength = Math.Min(paramBytes.Length, debugData.FuntionParamter.Length);
            Array.Copy(paramBytes, debugData.FuntionParamter, debugData.Length);
            // debugData.Length = (uint)copyLength; // 设置实际参数长度

            // 通过TCP发送debugData
            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送轨迹请求 {dataToSend.Length} 字节数据\r\n");
                }
                catch (Exception ex)
                {
                    AppendLog($"获取轨迹请求发送失败: {ex.Message}\r\n");
                }
            }
            else
            {
                AppendLog("TCP连接未建立，无法发送数据\r\n");
            }
        }


    }
}



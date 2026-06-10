using FluentFTP;
using HandyControl.Tools;
using Org.BouncyCastle.Tls;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;

using Microsoft.Data.Sqlite;
using GMap.NET.Projections;

namespace LZ
{
    public class MBTilesMapProvider : GMapProvider
    {
        private readonly string _dbPath;
        private readonly Guid _id = Guid.NewGuid();


        // 构造函数传入 mbtiles 文件的完整路径
        public MBTilesMapProvider(string dbPath)
        {
            _dbPath = dbPath;
        }

        public override Guid Id => _id;
        public override string Name => "MBTilesOfflineMap";
        public override PureProjection Projection => MercatorProjection.Instance;
        public override GMapProvider[] Overlays => new GMapProvider[] { this };

        // 核心方法：拦截 GMap 的瓦片请求，改为去 SQLite 数据库里查图片
        //public override PureImage GetTileImage(GPoint pos, int zoom)
        //{
        //    try
        //    {
        //        if (!File.Exists(_dbPath)) return null;

        //        // MBTiles 使用的是 TMS 坐标系，Y轴与标准瓦片图上下颠倒，需要翻转
        //        long tmsY = (1 << zoom) - 1 - pos.Y;

        //        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        //        {
        //            conn.Open();
        //            using (var cmd = conn.CreateCommand())
        //            {
        //                // 在 tiles 表中查询对应层级和坐标的图片数据
        //                cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level = @z AND tile_column = @x AND tile_row = @y";
        //                cmd.Parameters.AddWithValue("@z", zoom);
        //                cmd.Parameters.AddWithValue("@x", pos.X);
        //                cmd.Parameters.AddWithValue("@y", tmsY);

        //                var result = cmd.ExecuteScalar();
        //                if (result != null && result != DBNull.Value && result is byte[] data)
        //                {
        //                    // 使用 GMap 内置的字节转图片方法返回
        //                    return GetTileImageFromArray(data);
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        System.Diagnostics.Debug.WriteLine($"读取 MBTiles 失败: {ex.Message}");
        //    }
        //    return null;
        //}
        public override PureImage GetTileImage(GPoint pos, int zoom)
        {
            try
            {
                // 1. 检查文件到底在不在
                if (!File.Exists(_dbPath))
                {
                    // 如果文件不在，弹窗警告（只会弹一次以防卡死）
                    System.Windows.MessageBox.Show($"严重错误：找不到离线地图文件！\n预期路径：{_dbPath}\n请检查“复制到输出目录”属性是否设置！", "文件缺失", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return null;
                }

                // 标准 MBTiles 翻转 Y 轴
                long tmsY = (1 << zoom) - 1 - pos.Y;

                using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level = @z AND tile_column = @x AND tile_row = @y";
                        cmd.Parameters.AddWithValue("@z", zoom);
                        cmd.Parameters.AddWithValue("@x", pos.X);
                        cmd.Parameters.AddWithValue("@y", tmsY);

                        var result = cmd.ExecuteScalar();

                        // 2. 如果查到了数据，成功返回
                        if (result != null && result != DBNull.Value && result is byte[] data)
                        {
                            return GetTileImageFromArray(data);
                        }
                        else
                        {
                            // 3. 如果没查到数据，尝试用不翻转的 Y 轴（XYZ模式）再查一次！
                            cmd.Parameters["@y"].Value = pos.Y;
                            var fallbackResult = cmd.ExecuteScalar();
                            if (fallbackResult != null && fallbackResult != DBNull.Value && fallbackResult is byte[] fallbackData)
                            {
                                return GetTileImageFromArray(fallbackData);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"读取 SQLite 数据库异常：{ex.Message}", "数据库错误");
            }
            return null;
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private GMapMarker carMarker;

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
        SGeofence sGeofence;
        OriginInfo origin = new OriginInfo();
        private CoordinateTransformationFactory _ctf;
        private ICoordinateTransformation _wgs84ToUtm;

        COGStatus cOGStatus = new COGStatus();

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

        public bool isLeftWarningBlinking = false;
        public bool isRightWarningBlinking = false;
        float currentVehicleY = 0;

        private FtpClient ftpClient;
        public bool ftp_connected;


        // 限制画布最大保留点数，防止长时间运行内存暴涨导致卡顿
        private const int MaxTrackPointCount = 5000;

        // 后台线程安全的坐标缓存点集
        private readonly List<System.Windows.Point> _vutPointsCache = new List<System.Windows.Point>();
        private readonly List<System.Windows.Point> _sptPointsCache = new List<System.Windows.Point>();
        private readonly List<System.Windows.Point> _vtPointsCache = new List<System.Windows.Point>();

        // 拖拽平移相关变量
        private System.Windows.Point _mouseStartPoint;
        private bool _isPlotDragging = false;

        // 是否已经成功锁定了第一次数据作为轨迹图原点
        private bool _isPlotOriginInitialized = false;

        // 轨迹图独立的局部原点
        private BlhPoint _plotOriginBLH = new BlhPoint();


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
            sGeofence = new SGeofence
            {
                nPointCount = 4,
                vecPoints = new SPoint[4]
            };
            ftpClient = new FtpClient();

            InitializeDefaultGeofenceUI();

            // 构造函数中初始化 transform（在 InitializeComponent(); 和 ftpClient = new FtpClient(); 之后或合适位置）
            // 保留原有代码行，不要删除其它初始化
            _trackCanvasScale = new ScaleTransform(1.0, 1.0);
            _trackCanvasTranslate = new TranslateTransform(0, 0);
            var trackGroup = new TransformGroup();
            trackGroup.Children.Add(_trackCanvasScale);
            trackGroup.Children.Add(_trackCanvasTranslate);
            TrackCanvas.RenderTransform = trackGroup;
            TrackCanvas.RenderTransformOrigin = new Point(0, 0);

            _driverScale = new ScaleTransform(1.0, 1.0);
            _driverTranslate = new TranslateTransform(0, 0);
            var driverGroup = new TransformGroup();
            driverGroup.Children.Add(_driverScale);
            driverGroup.Children.Add(_driverTranslate);
            Driver.RenderTransform = driverGroup;
            Driver.RenderTransformOrigin = new Point(0, 0);

            // 监听鼠标滚轮（在窗口预览阶段捕获，便于在 canvas 或 vehicle 上都能触发）
            //this.PreviewMouseWheel += MainWindow_PreviewMouseWheel;

            timer = new DispatcherTimer(); // 设置定时器间隔为1000毫秒（1秒）
            timer.Interval = TimeSpan.FromSeconds(0.05);
            timer.Tick += UpdateTimer_Tick; // 指定事件触发时调用的方法
            _ctf = new CoordinateTransformationFactory();
            var wgs84 = GeographicCoordinateSystem.WGS84;
            var utm = ProjectedCoordinateSystem.WGS84_UTM(50, true); // Example: UTM zone 50N
            _wgs84ToUtm = _ctf.CreateFromCoordinateSystems(wgs84, utm);
            Restart_Button.IsEnabled = false;
            Update_Button.IsEnabled = false;

            // 绑定轨迹图专属的鼠标缩放、拖拽交互处理
            this.TrajectoryPlotContainer.MouseWheel += PlotCanvas_MouseWheel;
            this.TrajectoryPlotContainer.MouseLeftButtonDown += PlotCanvas_MouseLeftButtonDown;
            this.TrajectoryPlotContainer.MouseLeftButtonUp += PlotCanvas_MouseLeftButtonUp;
            this.TrajectoryPlotContainer.MouseMove += PlotCanvas_MouseMove;

            // 画布尺寸改变时，重置原点到画布中心
            this.PlotCanvas.SizeChanged += (s, e) => ResetPlotCanvasCenter();


        }

        // 定时器事件处理方法
        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateDriverDisplay();
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
            UpdateDriverDisplay();
            // 刷新高级轨迹图界面
            RenderTrajectoryPlot();
            // 可选：让TextBox自动滚动到最后一行
            //AppendLog($"robotData_part1:{robotData_Part1.SBV}\r\n");
            //textBox3.AppendText($"robotData_part1:{robotData_Part1.Soft_Vertion}\r\n");
            //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Latitude}\r\n");
            //textBox3.AppendText($"robotData_part2:{robotData_Part2.VUTMP_Longitude}\r\n");
            //textBox1.SelectionStart = textBox1.TextLength;
            //textBox1.ScrollToCaret();
        }
        private async void Connect_Button_Click(object sender, RoutedEventArgs e)
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


                    ftp_connected = await ftpClient.ConnectAsync(server_ip.Text,21,"root","");

                    if (ftp_connected)
                    {
                        AppendLog("FTP连接成功！\r\n");
                        Update_Button.IsEnabled = true;
                    }
                    else
                    {
                        AppendLog("FTP连接失败！\r\n");
                    }

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

                    Restart_Button.IsEnabled = true;
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
                    AppendLog($"连接失败: {ex.Message}，请检查域控程序是否启动\r\n");
                    close_connection();
                    Connect_Button.IsEnabled = true;

                }

            }
        }

        // 接收TCP服务器数据
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
                        Dispatcher.Invoke(() =>
                        {
                            Restart_Button.IsEnabled = false;
                            Update_Button.IsEnabled = false;
                        });
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
                                case 0x93:
                                    sDebugControlParam = ByteArrayToDebugControlParam(msg);
                                    AppendLog("控制参数接收成功!\r\n");
                                    break;
                                case 0x96:
                                    sGeofence = ByteArrayToGeofence(msg);
                                    AppendLog($"电子围栏数据接收成功，点数: {sGeofence.nPointCount}，载荷长度: {msg.Length} 字节\r\n");
                                    AppendLog($"电子围栏点坐标: {string.Join(", ", sGeofence.vecPoints.Select(p => $"({p.dLongitude:F8}, {p.dLatitude:F8})"))}\r\n");
                                    UpdateGeofenceUI();
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
                                            HandyControl.Controls.MessageBox.Show("错误：多个惯导的IP地址和端口不能同时相同！请修改", "配置错误");
                                        }
                                        else
                                        {
                                            //MessageBox.Show("配置文件读取成功!");
                                            HandyControl.Controls.MessageBox.Show("配置文件读取成功!");
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
                                        //MessageBox.Show("配置写入成功!", "配置写入");
                                        // 使用 Dispatcher 并在内部调用 HandyControl 的 MessageBox
                                        Dispatcher.Invoke(() =>
                                        {
                                            // 传入 this 使弹窗在主界面中心显示，而不是屏幕中心
                                            MessageBoxResult result = HandyControl.Controls.MessageBox.Show(
                                                this,
                                                "配置写入成功！是否立即重启？\n\n(选择“是”进行重启，选择“否”稍后处理)",
                                                "配置写入",
                                                MessageBoxButton.YesNo,
                                                MessageBoxImage.Information);

                                            if (result == MessageBoxResult.Yes)
                                            {
                                                Restart_Button_Click(null, null);
                                            }
                                        });

                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("配置写入失败!", "配置写入"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    //debugConfig = ByteArrayToDebugConfig(data);
                                    break;
                                // 控制参数接收成功
                                case 0x83:
                                    sSendBack = BytesToStruct<SendBack>(msg, 0);
                                    if (sSendBack.ack == 1)
                                    {
                                        
                                        HandyControl.Controls.MessageBox.Show("控制参数读取成功!");

                                        // 更新控制参数界面
                                        UpdateControlUI();
                                        //Dispatcher.Invoke(() => CustomMessageBox.Show("配置写入成功!"));
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("控制参数读取失败!", "配置读取"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    //debugConfig = ByteArrayToDebugConfig(data);
                                    break;
                                case 0x86:
                                    sSendBack = BytesToStruct<SendBack>(msg, 0);
                                    if (sSendBack.ack == 1)
                                    {
                                        HandyControl.Controls.MessageBox.Show("电子围栏读取成功!");
                                        UpdateGeofenceUI();
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("电子围栏读取失败!", "UWB配置"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    break;
                                case 0x87:
                                    sSendBack = BytesToStruct<SendBack>(msg, 0);
                                    if (sSendBack.ack == 1)
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            HandyControl.Controls.MessageBox.Show(
                                                this,
                                                "电子围栏写入成功！",
                                                "UWB配置",
                                                MessageBoxButton.OK,
                                                MessageBoxImage.Information);
                                        });
                                        UpdateMapGeofence();
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
                                    else
                                    {
                                        Dispatcher.Invoke(() => MessageBox.Show("电子围栏写入失败!", "UWB配置"));
                                        AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                    }
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

                                // 检查基准原点是否已初始化
                                // 1. 动态初始化轨迹图专属原点（使用接收到的第一条有效 VUT 经纬度）
                                if (!_isPlotOriginInitialized)
                                {
                                    double vutLat = robotData_Part2.VUTMP_Latitude;
                                    double vutLon = robotData_Part2.VUTMP_Longitude;

                                    if (vutLat != 0 && vutLon != 0)
                                    {
                                        _plotOriginBLH.Lat = vutLat;
                                        _plotOriginBLH.Lon = vutLon;
                                        _plotOriginBLH.Altitude = 0;
                                        _plotOriginBLH.Heading = robotData_Part2.VUTMP_Azimuth; // 如果需要旋转，以当前航向为基准
                                        _isPlotOriginInitialized = true;

                                        // 第一次收到数据时，自动触发一次回中
                                        Dispatcher.Invoke(() => Btn_ResetPlot_Click(null, null));
                                    }
                                }

                                // 2. 只有原点初始化后，才开始解算相对位置，防止坐标爆表
                                if (_isPlotOriginInitialized)
                                {
                                    // VUT 相对坐标解算
                                    BlhPoint pVut = new BlhPoint { Lat = robotData_Part2.VUTMP_Latitude, Lon = robotData_Part2.VUTMP_Longitude };
                                    if (pVut.Lat != 0 && pVut.Lon != 0)
                                    {
                                        GIS.Complanation(_plotOriginBLH, pVut, out XyzPoint xyz);
                                        lock (_vutPointsCache)
                                        {
                                            _vutPointsCache.Add(new System.Windows.Point(xyz.X, xyz.Y));
                                            if (_vutPointsCache.Count > MaxTrackPointCount) _vutPointsCache.RemoveAt(0);
                                        }
                                    }

                                    // SPT 相对坐标解算
                                    BlhPoint pSpt = new BlhPoint { Lat = robotData_Part3.SPTMP_Latitude, Lon = robotData_Part3.SPTMP_Longitude };
                                    if (pSpt.Lat != 0 && pSpt.Lon != 0)
                                    {
                                        GIS.Complanation(_plotOriginBLH, pSpt, out XyzPoint xyz);
                                        lock (_sptPointsCache)
                                        {
                                            _sptPointsCache.Add(new System.Windows.Point(xyz.X, xyz.Y));
                                            if (_sptPointsCache.Count > MaxTrackPointCount) _sptPointsCache.RemoveAt(0);
                                        }
                                    }

                                    // VT 相对坐标解算
                                    BlhPoint pVt = new BlhPoint { Lat = robotData_Part4.SubMP_Latitude, Lon = robotData_Part4.SubMP_Longitude };
                                    if (pVt.Lat != 0 && pVt.Lon != 0)
                                    {
                                        GIS.Complanation(_plotOriginBLH, pVt, out XyzPoint xyz);
                                        lock (_vtPointsCache)
                                        {
                                            _vtPointsCache.Add(new System.Windows.Point(xyz.X, xyz.Y));
                                            if (_vtPointsCache.Count > MaxTrackPointCount) _vtPointsCache.RemoveAt(0);
                                        }
                                    }
                                }


                                updatemsg();
                                break;

                            case 0x93:
                                Array.Copy(data, 8, msg, 0, length);
                                AppendLog(Encoding.UTF8.GetString(msg).TrimEnd('\0'));
                                break;
                            case 0x94:
                                Array.Copy(data, 8, msg, 0, length);
                                cOGStatus = BytesToStruct<COGStatus>(msg, 0);
                                //AppendLog($"COG:{cOGStatus.sample_count}");
                                updatecog();
                                break;
                            case 0x95:
                                Array.Copy(data, 8, msg, 0, length);
                                // UDP: msg 已经从 data 中拷贝
                                if (TryParseLaneRelative(msg, out var laneRelUdp))
                                {
                                    HandleLaneRelativeData(laneRelUdp);
                                }
                                else
                                {
                                    AppendLog("接收到未知的 0x95 UDP 包（未能解析为车道数据）");
                                }
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
                

                //string robot_type_str = System.Text.Encoding.ASCII.GetString(debugConfig.robot_type).TrimEnd('\0');
                string robot_type_str = GetStringFromByteArray(debugConfig.robot_type);
                if (robot_type_str == "aid" || robot_type_str == "lizhong")
                {
                    AppendLog($"机器人类型: {robot_type_str}\r\n");
                    config_rb_type_lz.IsChecked = true;
                }
                else
                {
                    config_rb_type_siasun.IsChecked = true;
                }
                //config_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.can_device_name).TrimEnd('\0');
                config_can_device_textbox.Text = GetStringFromByteArray(debugConfig.can_device_name);
                config_can_baud_textbox.Text = debugConfig.can_baud.ToString();
                //config_upper_ip_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.udp_server_ip).TrimEnd('\0');
                config_upper_ip_textbox.Text = GetStringFromByteArray(debugConfig.udp_server_ip);
                config_upper_port_textbox.Text = debugConfig.udp_server_port.ToString();
                //string ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.ins_type).TrimEnd('\0');
                //string agreement = System.Text.Encoding.ASCII.GetString(debugConfig.agreement).TrimEnd('\0'); // 取出协议类型
                string ins_type = GetStringFromByteArray(debugConfig.ins_type);
                string agreement = GetStringFromByteArray(debugConfig.agreement);
                AppendLog($"InsType:{ins_type}\r\n");
                if (ins_type == "bynav")
                {
                    config_rb_instype_by.IsChecked = true;
                    if (agreement == "udp") config_rb_by_unicast.IsChecked = true;
                    else config_rb_by_broadcast.IsChecked = true;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo.IsChecked = true;
                }
                //config_ins_ip_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.vut_ins_ip).TrimEnd('\0');
                config_ins_ip_textbox.Text = GetStringFromByteArray(debugConfig.vut_ins_ip);
                config_ins_port_textbox.Text = debugConfig.vut_ins_port.ToString();
                //string spt_ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.spt_ins_type).TrimEnd('\0');
                //string spt_agreement = System.Text.Encoding.ASCII.GetString(debugConfig.spt_agreement).TrimEnd('\0'); // 取出协议类型
                string spt_ins_type = GetStringFromByteArray(debugConfig.spt_ins_type);
                string spt_agreement = GetStringFromByteArray(debugConfig.spt_agreement);
                if (spt_ins_type == "bynav")
                {
                    config_rb_instype_by1.IsChecked = true;
                    if (spt_agreement == "udp") config_rb_by_unicast1.IsChecked = true;
                    else config_rb_by_broadcast1.IsChecked = true;
;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt1.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo1.IsChecked = true;
                }
                //config_ins_ip_textbox1.Text = System.Text.Encoding.ASCII.GetString(debugConfig.spt_ins_ip).TrimEnd('\0');
                config_ins_ip_textbox1.Text = GetStringFromByteArray(debugConfig.spt_ins_ip);
                config_ins_port_textbox1.Text = debugConfig.spt_ins_port.ToString();

                //string vt_ins_type = System.Text.Encoding.ASCII.GetString(debugConfig.vt_ins_type).TrimEnd('\0');
                //string vt_agreement = System.Text.Encoding.ASCII.GetString(debugConfig.vt_agreement).TrimEnd('\0'); // 取出协议类型
                string vt_ins_type = GetStringFromByteArray(debugConfig.vt_ins_type);
                string vt_agreement = GetStringFromByteArray(debugConfig.vt_agreement);
                if (vt_ins_type == "bynav")
                {
                    config_rb_instype_by2.IsChecked = true;
                    if (vt_agreement == "udp") config_rb_by_unicast2.IsChecked = true;
                    else config_rb_by_broadcast2.IsChecked = true;
                }
                else if (ins_type == "rt")
                {
                    config_rb_instype_rt2.IsChecked = true;
                }
                else
                {
                    config_rb_instype_shibo2.IsChecked = true;
                }
                //config_ins_ip_textbox2.Text = System.Text.Encoding.ASCII.GetString(debugConfig.vt_ins_ip).TrimEnd('\0');
                config_ins_ip_textbox2.Text = GetStringFromByteArray(debugConfig.vt_ins_ip);
                config_ins_port_textbox2.Text = debugConfig.vt_ins_port.ToString();

                config_smd_savedays_textbox.Text = debugConfig.smd_save_days.ToString();
                //config_smd_savepath_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.smd_file_save_path).TrimEnd('\0');
                config_smd_savepath_textbox.Text = GetStringFromByteArray(debugConfig.smd_file_save_path);

                config_log_savedays_textbox.Text = debugConfig.log_save_days.ToString();
                //config_log_savepath_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.log_file_save_path).TrimEnd('\0');
                config_log_savepath_textbox.Text = GetStringFromByteArray(debugConfig.log_file_save_path);

                //config_data_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.data_can_device_name).TrimEnd('\0');
                config_data_can_device_textbox.Text = GetStringFromByteArray(debugConfig.data_can_device_name);
                config_data_can_baud_textbox.Text = debugConfig.data_can_baud.ToString();

                //config_daq_can_device_textbox.Text = System.Text.Encoding.ASCII.GetString(debugConfig.daq_can_device_name).TrimEnd('\0');
                config_daq_can_device_textbox.Text = GetStringFromByteArray(debugConfig.daq_can_device_name);
                config_daq_can_baud_textbox.Text = debugConfig.daq_can_baud.ToString();

                config_headtraker_textbox.Text = debugConfig.headtrakertype.ToString();
                //config_dbc_version_textbox.Text = debugConfig.dbc_version.ToString();
                if (debugConfig.dbc_version == 2)
                {
                    config_rb_robot.IsChecked = true;
                }
                else
                {
                    config_rb_ufo.IsChecked = true;
                }

                config_save_textbox.Text = debugConfig.save_mode.ToString();

                
                //}));
            });

            

        }

        private void UpdateControlUI()
        {

            Dispatcher.Invoke(() =>
            {


                //string robot_type_str = System.Text.Encoding.ASCII.GetString(debugConfig.robot_type).TrimEnd('\0');
                cfg_frobase.Text = sDebugControlParam.FrontBase.ToString("F2");
                cfg_curve_kp1.Text = sDebugControlParam.CurveKp1.ToString("F2");
                cfg_curve_kp2.Text = sDebugControlParam.CurveKp2.ToString("F2");
                cfg_curve_kp3.Text = sDebugControlParam.CurveKp3.ToString("F2");
                cfg_ccrh_stanley.Text = sDebugControlParam.ccrh_stanley.ToString("F2");
                cfg_elk_flag.Text = sDebugControlParam.ElkFlag.ToString();

                cfg_preview_point.Text = sDebugControlParam.Preview1.ToString("F2");
                cfg_lf_10.Text = sDebugControlParam.lf10_stanley.ToString("F2");
                cfg_lf_20.Text = sDebugControlParam.lf20_stanley.ToString("F2");
                cfg_lf_30.Text = sDebugControlParam.lf30_stanley.ToString("F2");
                cfg_steer_limit.Text = sDebugControlParam.steer_angle_limit.ToString("F2");

                cfg_heading_compen.Text = sDebugControlParam.Heading.ToString("F2");
                cfg_rf_10.Text = sDebugControlParam.rf10_stanley.ToString("F2");
                cfg_rf_20.Text = sDebugControlParam.rf20_stanley.ToString("F2");
                cfg_speed_limit.Text = sDebugControlParam.steer_speed_limit.ToString("F2");


                cfg_low_kp.Text = sDebugControlParam.Low_P.ToString("F2");
                cfg_low_ki.Text = sDebugControlParam.Low_I.ToString("F2");
                cfg_low_kd.Text = sDebugControlParam.Low_D.ToString("F2");
                cfg_acceleration_limit.Text = sDebugControlParam.Acc_Max.ToString("F2");
                
                cfg_mid_kp.Text = sDebugControlParam.Mid_P.ToString("F2");
                cfg_mid_ki.Text = sDebugControlParam.Mid_I.ToString("F2");
                cfg_mid_kd.Text = sDebugControlParam.Mid_D.ToString("F2");
                cfg_decceleration_limit.Text = sDebugControlParam.Acc_Min.ToString("F2");

                cfg_high_kp.Text = sDebugControlParam.Hig_P.ToString("F2");
                cfg_high_ki.Text = sDebugControlParam.Hig_I.ToString("F2");
                cfg_high_kd.Text = sDebugControlParam.Hig_D.ToString("F2");


                cfg_ten.Text = sDebugControlParam.ten.ToString("F2");
                cfg_twenty.Text = sDebugControlParam.twenty.ToString("F2");
                cfg_thirty.Text = sDebugControlParam.thirty.ToString("F2");
                cfg_forty.Text = sDebugControlParam.forty.ToString("F2");
                cfg_fifty.Text = sDebugControlParam.fifty.ToString("F2");
                cfg_sixty.Text = sDebugControlParam.sixty.ToString("F2");
                cfg_senventy.Text = sDebugControlParam.seventy.ToString("F2");
                cfg_eighty.Text = sDebugControlParam.eighty.ToString("F2");
                cfg_ninety.Text = sDebugControlParam.ninety.ToString("F2");
                cfg_hundred.Text = sDebugControlParam.hundred.ToString("F2");
                cfg_hundred_ten.Text = sDebugControlParam.hundred_ten.ToString("F2");
                cfg_hundred_twenty.Text = sDebugControlParam.hundred_twenty.ToString("F2");

                cfg_Xactual.Text = sDebugControlParam.XActual.ToString("F2");
                cfg_SRtorque.Text = sDebugControlParam.SRTortue.ToString("F2");

                //}));
            });



        }

        private void UpdateGeofenceUI()
        {
            Dispatcher.Invoke(() =>
            {
                if (sGeofence.vecPoints == null || sGeofence.vecPoints.Length < 4)
                    sGeofence.vecPoints = new SPoint[4];

                uwb_fence_p1_lon.Text = sGeofence.vecPoints[0].dLongitude.ToString("F8");
                uwb_fence_p1_lat.Text = sGeofence.vecPoints[0].dLatitude.ToString("F8");
                uwb_fence_p2_lon.Text = sGeofence.vecPoints[1].dLongitude.ToString("F8");
                uwb_fence_p2_lat.Text = sGeofence.vecPoints[1].dLatitude.ToString("F8");
                uwb_fence_p3_lon.Text = sGeofence.vecPoints[2].dLongitude.ToString("F8");
                uwb_fence_p3_lat.Text = sGeofence.vecPoints[2].dLatitude.ToString("F8");
                uwb_fence_p4_lon.Text = sGeofence.vecPoints[3].dLongitude.ToString("F8");
                uwb_fence_p4_lat.Text = sGeofence.vecPoints[3].dLatitude.ToString("F8");

                UpdateMapGeofence();
            });
        }

        private bool ReadGeofenceFromUI()
        {
            var lonTextBoxes = new[] { uwb_fence_p1_lon, uwb_fence_p2_lon, uwb_fence_p3_lon, uwb_fence_p4_lon };
            var latTextBoxes = new[] { uwb_fence_p1_lat, uwb_fence_p2_lat, uwb_fence_p3_lat, uwb_fence_p4_lat };

            if (sGeofence.vecPoints == null || sGeofence.vecPoints.Length < 4)
                sGeofence.vecPoints = new SPoint[4];

            for (int i = 0; i < 4; i++)
            {
                if (!double.TryParse(lonTextBoxes[i].Text.Trim(), out double longitude))
                {
                    MessageBox.Show($"顶点 {i + 1} 经度格式无效，请输入有效数字。", "UWB配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!double.TryParse(latTextBoxes[i].Text.Trim(), out double latitude))
                {
                    MessageBox.Show($"顶点 {i + 1} 纬度格式无效，请输入有效数字。", "UWB配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                sGeofence.vecPoints[i].dLongitude = longitude;
                sGeofence.vecPoints[i].dLatitude = latitude;
            }

            sGeofence.nPointCount = 4;
            return true;
        }

        private void Read_Geofence_Button_Click(object sender, RoutedEventArgs e)
        {
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01;
            debugData.FunctionCode = 0x86;
            string paramStr = "CFG_Geofence_READ";

            byte[] paramBytes = Encoding.UTF8.GetBytes(paramStr);
            debugData.Length = (uint)paramBytes.Length;
            Array.Copy(paramBytes, debugData.FuntionParamter, debugData.Length);

            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送电子围栏读取请求 {dataToSend.Length} 字节\r\n");
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

        private void Write_Geofence_Button_Click(object sender, RoutedEventArgs e)
        {
            Read_Geofence_Button.IsEnabled = false;
            if (!ReadGeofenceFromUI())
            {
                Read_Geofence_Button.IsEnabled = true;
                return;
            }

            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01;
            debugData.FunctionCode = 0x87;

            byte[] geofenceBytes = GeofenceToByteArray(sGeofence);
            int copyLength = Math.Min(geofenceBytes.Length, debugData.FuntionParamter.Length);
            Array.Copy(geofenceBytes, 0, debugData.FuntionParamter, 0, copyLength);
            debugData.Length = (uint)copyLength;

            if (_client != null && _client.Connected && _stream != null)
            {
                try
                {
                    byte[] dataToSend = StructStreamReader.StructToByteArray(debugData);
                    _stream.Write(dataToSend, 0, dataToSend.Length);
                    AppendLog($"已发送电子围栏写入请求 {dataToSend.Length} 字节\r\n");
                }
                catch (Exception ex)
                {
                    AppendLog($"发送失败: {ex.Message}\r\n");
                }
                finally
                {
                    Read_Geofence_Button.IsEnabled = true;
                }
            }
            else
            {
                AppendLog("TCP连接未建立，无法发送数据\r\n");
                Read_Geofence_Button.IsEnabled = true;
            }
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
                data_vut_forward_velocity.Text = robotData_Part2.VUTMP_Forward_velocity.ToString("F2");
                data_vut_lateral_velocity.Text = robotData_Part2.VUTMP_Lateral_velocity.ToString("F2");
                data_vut_forward_acc.Text = robotData_Part2.VUTMP_Forward_acceleration.ToString("F2");
                data_vut_lateral_acc.Text = robotData_Part2.VUTMP_Lateral_acceleration.ToString("F2");
                data_vut_insX.Text = robotData_Part2.VUTMP_INS_X.ToString("F2");
                data_vut_insY.Text = robotData_Part2.VUTMP_INS_Y.ToString("F2");
                data_vut_insStatus.Text = robotData_Part2.VUTMP_INS_Status.ToString("F2");
                data_vut_posType.Text = robotData_Part2.VUTMP_RTK_Status.ToString("F2");
                data_vut_yaw.Text = robotData_Part2.VUTMP_Yaw_angle.ToString("F2");
                data_vut_utc.Text = robotData_Part1.Utc_time.ToString("F4");
                //data_vut_actualx.Text = robotData_Part2.VUTMP_Actual_X.ToString("F2");
                //data_vut_actualy.Text = robotData_Part2.VUTMP_Actual_Y.ToString("F2");

                data_spt_latitude1.Text = robotData_Part3.SPTMP_Latitude.ToString("F8");
                data_spt_longitude1.Text = robotData_Part3.SPTMP_Longitude.ToString("F8");
                data_spt_azimuth1.Text = robotData_Part3.SPTMP_Azimuth.ToString("F2");
                data_spt_velocity1.Text = robotData_Part3.SPTMP_Vertical_velocity.ToString("F2");
                data_spt_forward_velocity.Text = robotData_Part3.SPTMP_Forward_velocity.ToString("F2");
                data_spt_lateral_velocity.Text = robotData_Part3.SPTMP_Lateral_velocity.ToString("F2");
                data_spt_forward_acc.Text = robotData_Part3.SPTMP_Forward_acceleration.ToString("F2");
                data_spt_lateral_acc.Text = robotData_Part3.SPTMP_Lateral_acceleration.ToString("F2");
                data_spt_insX.Text = robotData_Part3.SPTMP_INS_X.ToString("F2");
                data_spt_insY.Text = robotData_Part3.SPTMP_INS_Y.ToString("F2");

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

                // 更新离线地图上的车辆位置
                if (carMarker != null && robotData_Part2.VUTMP_Latitude != 0 && robotData_Part2.VUTMP_Longitude != 0)
                {
                    carMarker.Position = new PointLatLng(robotData_Part2.VUTMP_Latitude, robotData_Part2.VUTMP_Longitude);

                    // 可选：如果希望地图一直跟随车辆中心移动，可以取消下面这行的注释
                    // OfflineMap.Position = carMarker.Position; 
                }
            });
        }

        private void updatecog()
        {
            Dispatcher.Invoke(() =>
            {
                if(cOGStatus.is_ins_ready == 1)
                {
                    AppendLog($"COG采样完成，样本数量: {cOGStatus.sample_count} 估计值: {cOGStatus.est_cog}\r\n");
                    cog_estimated_textbox.Text = cOGStatus.est_cog.ToString("F2");
                    COG_ProcessBar.Value = 100;
                }
                else
                {
                    COG_ProcessBar.Value = cOGStatus.sample_count / 30;
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

        private DebugControlParam ByteArrayToDebugControlParam(byte[] data)
        {
            int size = Marshal.SizeOf(typeof(DebugControlParam));
            // AppendLog($"DebugConfigLength: {size}\r\n");
            if (data.Length < size)
                throw new ArgumentException("数据长度不足，无法转换为DebugControlParam结构体");

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(data, 0, ptr, size);
                return (DebugControlParam)Marshal.PtrToStructure(ptr, typeof(DebugControlParam));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private SGeofence ByteArrayToGeofence(byte[] data)
        {
            const int pointCountSize = sizeof(int);
            const int pointSize = sizeof(double) * 2;
            const int expectedSize = pointCountSize + pointSize * 4;

            if (data.Length < pointCountSize)
                throw new ArgumentException("数据长度不足，无法转换为SGeofence结构体");

            // 期望线格式: int nPointCount + 4 * (double dLongitude + double dLatitude) = 68 字节
            // C++ 不可 memcpy 含 std::vector 的 SGeofence，否则会发送 vector 内部指针而非坐标点
            if (data.Length < expectedSize)
            {
                AppendLog($"警告: 电子围栏载荷仅 {data.Length} 字节，期望 {expectedSize} 字节。"
                    + "请确认 C++ 端按 nPointCount + 4 个 SPoint 序列化，而非 memcpy 整个 SGeofence。\r\n");
            }

            var geofence = new SGeofence
            {
                nPointCount = Math.Min(BitConverter.ToInt32(data, 0), 4),
                vecPoints = new SPoint[4]
            };

            int pointsToRead = Math.Min(4, (data.Length - pointCountSize) / pointSize);
            for (int i = 0; i < pointsToRead; i++)
            {
                int offset = pointCountSize + i * pointSize;
                geofence.vecPoints[i].dLongitude = BitConverter.ToDouble(data, offset);
                geofence.vecPoints[i].dLatitude = BitConverter.ToDouble(data, offset + sizeof(double));
            }

            if (data.Length >= expectedSize)
                geofence.nPointCount = 4;

            return geofence;
        }

        private byte[] GeofenceToByteArray(SGeofence geofence)
        {
            const int pointCountSize = sizeof(int);
            const int pointSize = sizeof(double) * 2;
            byte[] bytes = new byte[pointCountSize + pointSize * 4];

            BitConverter.GetBytes(geofence.nPointCount).CopyTo(bytes, 0);
            for (int i = 0; i < 4; i++)
            {
                int offset = pointCountSize + i * pointSize;
                BitConverter.GetBytes(geofence.vecPoints[i].dLongitude).CopyTo(bytes, offset);
                BitConverter.GetBytes(geofence.vecPoints[i].dLatitude).CopyTo(bytes, offset + sizeof(double));
            }

            return bytes;
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
        // 将界面数据发送给下位机
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
                debugConfig.robot_type = ConvertStringToFixedByteArray("lizhong", 8);
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
            debugConfig.tcp_local_port1 = 8202;

            debugConfig.udp_server_ip = ConvertStringToFixedByteArray(config_upper_ip_textbox.Text, 16);
            debugConfig.udp_server_port = int.Parse(config_upper_port_textbox.Text);

            debugConfig.default_path_file_name = ConvertStringToFixedByteArray("/home/root/custapp/configure/CPFA.XYZ", 64);

            /// VUT惯导配置
            if (config_rb_instype_by.IsChecked == true)
            {

                debugConfig.ins_type = ConvertStringToFixedByteArray("bynav", 8);
                debugConfig.agreement = config_rb_by_broadcast.IsChecked == true ? ConvertStringToFixedByteArray("tcp", 8) : ConvertStringToFixedByteArray("udp", 8);
            }
            else if (config_rb_instype_rt.IsChecked == true)
            {
                debugConfig.ins_type = ConvertStringToFixedByteArray("rt", 8);
                debugConfig.agreement = ConvertStringToFixedByteArray("udp", 8);
            }
            else
            {
                debugConfig.ins_type = ConvertStringToFixedByteArray("shibo", 8);
                debugConfig.agreement = ConvertStringToFixedByteArray("udp", 8);
            }

            debugConfig.vut_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox.Text, 16);
            debugConfig.vut_ins_port = int.Parse(config_ins_port_textbox.Text);
            //debugConfig.agreement = ConvertStringToFixedByteArray("udp", 8);
            debugConfig.message_id = 0;
            debugConfig.region = 8;
            debugConfig.sys_time_enable = 0;

            /// SPT 惯导配置
            if (config_rb_instype_by1.IsChecked == true)
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("bynav", 8);
                debugConfig.spt_agreement = config_rb_by_broadcast1.IsChecked == true ? ConvertStringToFixedByteArray("tcp", 8) : ConvertStringToFixedByteArray("udp", 8);
            }
            else if (config_rb_instype_rt1.IsChecked == true)
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("rt", 8);
                debugConfig.spt_agreement = ConvertStringToFixedByteArray("udp", 8);
            }
            else
            {
                debugConfig.spt_ins_type = ConvertStringToFixedByteArray("shibo", 8);
                debugConfig.spt_agreement = ConvertStringToFixedByteArray("udp", 8);
            }
            debugConfig.spt_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox1.Text, 16);
            debugConfig.spt_ins_port = int.Parse(config_ins_port_textbox1.Text);
            //debugConfig.spt_agreement = ConvertStringToFixedByteArray("udp", 8);
            debugConfig.spt_message_id = 0;
            debugConfig.spt_region = 8;
            debugConfig.spt_sys_time_enable = 0;

            /// VT惯导配置
            if (config_rb_instype_by2.IsChecked == true)
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("bynav", 8);
                debugConfig.vt_agreement = config_rb_by_broadcast2.IsChecked == true ? ConvertStringToFixedByteArray("tcp", 8) : ConvertStringToFixedByteArray("udp", 8);
            }
            else if (config_rb_instype_rt2.IsChecked == true)
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("rt", 8);
                debugConfig.vt_agreement = ConvertStringToFixedByteArray("udp", 8);
            }
            else
            {
                debugConfig.vt_ins_type = ConvertStringToFixedByteArray("shibo", 8);
                debugConfig.vt_agreement = ConvertStringToFixedByteArray("udp", 8);
            }
            debugConfig.vt_ins_ip = ConvertStringToFixedByteArray(config_ins_ip_textbox2.Text, 16);
            debugConfig.vt_ins_port = int.Parse(config_ins_port_textbox2.Text);
            //debugConfig.vt_agreement = ConvertStringToFixedByteArray("udp", 8);
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
            if(config_rb_robot.IsChecked == true)
            {
                debugConfig.dbc_version = 2;
            }
            else
            {
                debugConfig.dbc_version = 1;
            }

            debugConfig.save_mode = uint.Parse(config_save_textbox.Text);

            AppendLog($"bottom->\tRobot_Type:{GetStringFromByteArray(debugConfig.robot_type)}");
            AppendLog($"\tCan_Device:{GetStringFromByteArray(debugConfig.can_device_name)}");
            AppendLog($"\tCan_Baud:{debugConfig.can_baud}\r\n");

            AppendLog($"NET->\t LOCAL_IP1:{GetStringFromByteArray(debugConfig.local_ip1)}");
            AppendLog($"\t LOCAL_IP2:{GetStringFromByteArray(debugConfig.local_ip2)}\r\n");

            AppendLog($"UPPER->\tTCP_LOCAL_PORT:{debugConfig.tcp_local_port.ToString()},TCP_LOCAL_PORT1:{debugConfig.tcp_local_port1}\r\n");
            AppendLog($"\tUDP_SERVER_IP:{GetStringFromByteArray(debugConfig.udp_server_ip)},UDP_SERVER_PORT:{debugConfig.udp_server_port}\r\n");
            AppendLog($"\tDEFAULT_PATH_FILE_NAME:{GetStringFromByteArray(debugConfig.default_path_file_name)}\r\n");

            AppendLog($"INS->\tInsType:{GetStringFromByteArray(debugConfig.udp_server_ip)}");
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
                config_rb_robot.IsChecked = true;

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

        //private async void Update_Button_Click(object sender, RoutedEventArgs e)
        //{
        //    // 1. 弹出文件选择框
        //    var openFileDialog = new Microsoft.Win32.OpenFileDialog();
        //    openFileDialog.Filter = "所有文件 (*.*)|*.*";
        //    openFileDialog.Title = "选择要上传的文件";

        //    //if (openFileDialog.ShowDialog() == true)
        //    //{
        //    //    string selectedFilePath = openFileDialog.FileName;
        //    //    string fileName = System.IO.Path.GetFileName(selectedFilePath);

        //    //    AppendLog($"开始上传文件: {fileName}\r\n");

        //    //    try
        //    //    {
        //    //        string remoteDir = "/home/root/";
        //    //        string backupDir = "/home/root/backup/";
        //    //        string originalFile = "driverobot_arm";
        //    //        string backupFile = $"driverobot_arm_backup_{DateTime.Now:yyyyMMdd_HHmmss}";

        //    //        try
        //    //        {
        //    //            bool backupDirExists = await ftpClient.FileExistsAsync(backupDir);
        //    //            if (!backupDirExists)
        //    //            {
        //    //                AppendLog("创建backup文件夹...\r\n");
        //    //                await ftpClient.CreateDirectoryAsync(backupDir);
        //    //                AppendLog("backup文件夹创建成功\r\n");
        //    //            }
        //    //        }
        //    //        catch (Exception ex)
        //    //        {
        //    //            AppendLog($"创建backup文件夹失败: {ex.Message}\r\n");
        //    //            // 继续执行，可能文件夹已存在
        //    //        }

        //    //        // 4. 备份原有的driverobot_arm文件到backup文件夹
        //    //        bool fileExists = await ftpClient.FileExistsAsync(remoteDir + originalFile);

        //    //        if (fileExists)
        //    //        {
        //    //            AppendLog("发现原有driverobot_arm文件，开始备份到backup文件夹...\r\n");

        //    //            try
        //    //            {
        //    //                // 先下载原文件到临时位置
        //    //                MoveFileAsync();
        //    //            }
        //    //            catch (Exception ex)
        //    //            {
        //    //                AppendLog($"备份失败: {ex.Message}\r\n");
        //    //                MessageBox.Show($"备份文件失败: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        //    //            }
        //    //        }
        //    //        else
        //    //        {
        //    //            AppendLog("未发现原有driverobot_arm文件，跳过备份步骤\r\n");
        //    //        }

        //    //        // 5. 上传新文件
        //    //        AppendLog("开始上传新文件...\r\n");
        //    //        string remoteFilePath = remoteDir + originalFile;

        //    //        await ftpClient.UploadFileAsync(selectedFilePath, remoteFilePath);

        //    //        AppendLog($"文件上传成功: {fileName} -> {remoteFilePath}\r\n");

        //    //        // 6. 为上传的文件设置可执行权限
        //    //        AppendLog("设置文件可执行权限...\r\n");
        //    //        bool permissionSet = await ftpClient.SetFilePermissionsAsync(remoteFilePath, "111");

        //    //        if (permissionSet)
        //    //        {
        //    //            AppendLog("文件权限设置成功 (755)\r\n");
        //    //        }
        //    //        else
        //    //        {
        //    //            AppendLog("警告：文件权限设置失败\r\n");
        //    //            MessageBox.Show("文件权限设置失败，可能需要手动设置权限", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        //    //        }

        //    //        // 7. 验证上传结果
        //    //        bool uploadVerified = await ftpClient.FileExistsAsync(remoteFilePath);
        //    //        if (uploadVerified)
        //    //        {
        //    //            AppendLog("文件上传验证成功\r\n");
        //    //            MessageBox.Show("文件上传成功！权限已设置为可执行。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        //    //        }
        //    //        else
        //    //        {
        //    //            AppendLog("警告：文件上传验证失败\r\n");
        //    //            MessageBox.Show("文件上传验证失败，请检查服务器状态", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        //    //        }
        //    //    }
        //    //    catch (Exception ex)
        //    //    {
        //    //        AppendLog($"文件上传失败: {ex.Message}\r\n");
        //    //        MessageBox.Show($"文件上传失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

        //    //    }
        //    //}
        //}

        private async void Update_Button_Click(object sender, RoutedEventArgs e)
        {
            // 1. 弹出文件选择框
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "所有文件 (*.*)|*.*";
            openFileDialog.Title = "选择要更新的程序文件";

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFilePath = openFileDialog.FileName;
                string fileName = System.IO.Path.GetFileName(selectedFilePath);

                AppendLog($"开始更新程序，选择本地文件: {fileName}\r\n");

                try
                {
                    Update_Button.IsEnabled = false; // 更新期间禁用按钮防止重复点击

                    string remoteDir = "/home/root/";
                    string backupDir = "/home/root/bak/";
                    string originalFile = "driverobot_arm"; // 默认的远程程序名称
                    string backupFileName = $"{originalFile}_{DateTime.Now:yyyyMMdd_HHmmss}"; // 加上当前日期时间的备份名
                    string remoteFilePath = remoteDir + originalFile;

                    // 2. 检查并创建 bak 文件夹
                    //bool backupDirExists = await ftpClient.FileExistsAsync(backupDir);
                    bool backupDirExists = await ftpClient.DirectoryExistsAsync(backupDir);
                    if (!backupDirExists)
                    {
                        AppendLog("未找到 bak 文件夹，正在创建...\r\n");
                        await ftpClient.CreateDirectoryAsync(backupDir);
                    }

                    // 3. 检查远程是否存在原程序，如果存在则将其移动到 bak 文件夹备份
                    bool fileExists = await ftpClient.FileExistsAsync(remoteFilePath);
                    if (fileExists)
                    {
                        string backupPath = backupDir + backupFileName;
                        AppendLog($"发现原程序，正在备份为: {backupPath} ...\r\n");
                        await ftpClient.MoveFileAsync(remoteFilePath, backupPath);
                    }
                    else
                    {
                        AppendLog("未发现原有程序文件，跳过备份步骤\r\n");
                    }

                    // 4. 通过 FTP 传输选中的文件到远程目录
                    AppendLog($"开始上传新程序到: {remoteFilePath} ...\r\n");
                    await ftpClient.UploadFileAsync(selectedFilePath, remoteFilePath);
                    AppendLog($"文件上传成功！\r\n");

                    // 5. 为上传的文件设置可执行权限（比如777）
                    AppendLog("正在设置文件可执行权限...\r\n");
                    bool permissionSet = await ftpClient.SetFilePermissionsAsync(remoteFilePath, "+x");

                    if (permissionSet)
                    {
                        AppendLog("权限设置成功\r\n");
                        // 弹窗提示更新成功，并在主窗口居中
                        //HandyControl.Controls.MessageBox.Show(this, "程序更新成功！权限已设置为可执行。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        MessageBoxResult result = HandyControl.Controls.MessageBox.Show(
                                                this,
                                                "程序更新成功！是否立即重启？\n\n(选择“是”进行重启，选择“否”稍后处理)",
                                                "程序更新",
                                                MessageBoxButton.YesNo,
                                                MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            Restart_Button_Click(null, null);
                        }
                    }
                    else
                    {
                        AppendLog("警告：程序上传成功，但权限设置失败\r\n");
                        HandyControl.Controls.MessageBox.Show(this, "文件上传成功，但权限设置失败，可能需要手动设置。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"程序更新失败: {ex.Message}\r\n");
                    HandyControl.Controls.MessageBox.Show(this, $"更新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // 结束后恢复按钮状态
                    Update_Button.IsEnabled = true;
                }
            }
        }

        // 读取固定长度 C 字符数组（ASCII），以第一个 '\0' 截断
        private static string ReadFixedAsciiString(byte[] buf, int offset, int length)
        {
            if (buf == null || offset < 0 || offset + length > buf.Length) return string.Empty;
            int end = offset;
            int max = offset + length;
            while (end < max && buf[end] != 0) end++;
            return Encoding.ASCII.GetString(buf, offset, end - offset);
        }


        // 尝试解析 0x95 的二进制消息，返回 true 则 out data 有效
        private static bool TryParseLaneRelative(byte[] msg, out LaneRelativeDataC data)
        {
            data = null;
            if (msg == null || msg.Length < 4) return false;

            int offset = 0;
            // 小端整数（C 服务器在 x86/x64 上通常为 little-endian）
            if (offset + 4 > msg.Length) return false;
            int laneNum = BitConverter.ToInt32(msg, offset);
            offset += 4;

            if (laneNum <= 0 || laneNum > 10) return false; // 容错上限与服务器定义一致

            var result = new LaneRelativeDataC { LaneNum = laneNum };

            for (int i = 0; i < laneNum; i++)
            {
                // 每个 Vehicle2LaneInfo 最少需要 20 + 128 + 4 字节（ID、LaneName、relative_num）
                if (offset + 20 + 128 + 4 > msg.Length) return false;

                string id = ReadFixedAsciiString(msg, offset, 20);
                offset += 20;

                string laneName = ReadFixedAsciiString(msg, offset, 128);
                offset += 128;

                int relativeNum = BitConverter.ToInt32(msg, offset);
                offset += 4;

                if (relativeNum < 0 || relativeNum > 10) return false; // 安全检查

                // 每个 LaneMetrics1 为 4 个 float（16 字节）
                int metricsBytes = relativeNum * 16;
                if (offset + metricsBytes > msg.Length) return false;

                var info = new Vehicle2LaneInfoC
                {
                    ID = id,
                    LaneName = laneName,
                    RelativeNum = relativeNum
                };

                for (int j = 0; j<relativeNum; j++)
                {
                    // 依次读取 4 个 float（little-endian）
                    float distance = BitConverter.ToSingle(msg, offset); offset += 4;
                    float lateralSpeed = BitConverter.ToSingle(msg, offset); offset += 4;
                    float lateralAcc = BitConverter.ToSingle(msg, offset); offset += 4;
                    float ttc = BitConverter.ToSingle(msg, offset); offset += 4;

                    info.Metrics.Add(new LaneMetricsItem
                    {
                        Distance = distance,
                        LateralSpeed = lateralSpeed,
                        LateralAcc = lateralAcc,
                        TTC = ttc
                    });
                }

                result.Infos.Add(info);
            }

            data = result;
            return true;
        }

        // 处理并记录解析后的车道数据（可扩展为更新 UI）
        private void HandleLaneRelativeData(LaneRelativeDataC laneData)
        {
            if (laneData == null) return;
            Dispatcher.Invoke(() =>
            {
                AppendLog($"接收到 0x95 车道数据：laneNum={laneData.LaneNum}");
                for (int i = 0; i < laneData.Infos.Count; i++)
                {
                    var inf = laneData.Infos[i];
                    AppendLog($"  Lane[{i}] ID=\"{inf.ID}\", Name=\"{inf.LaneName}\", points={inf.RelativeNum}");
                    for (int j = 0; j < inf.Metrics.Count; j++)
                    {
                        var m = inf.Metrics[j];
                        AppendLog($"    Pt[{j}] dist={m.Distance:F3}m latSpeed={m.LateralSpeed:F3}m/s latAcc={m.LateralAcc:F3}m/s2 TTC={m.TTC:F3}s");
                    }
                }
            });
        }


        private void Refresh_Track_Button_Click(object sender, RoutedEventArgs e)
        {

        }

        private async void Refresh_Track(string pathfile)
        {


            Dispatcher.Invoke(async () =>
            {
                try
                {
                    // 1. SFTP下载文件到assets目录
                    string localDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");

                    if (!Directory.Exists(localDir))
                    {
                        AppendLog("Mkdir\r\n");
                        Directory.CreateDirectory(localDir);
                    }
                    pathfile += ".XYZ";
                    string localFilePath = System.IO.Path.Combine(localDir, pathfile);
                    string remoteFilePath = "/home/root/custapp/configure/" + pathfile;

                    await ftpClient.DownloadFileAsync(remoteFilePath, localFilePath);

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


        private async Task<string> DownloadFileFromSftp(string pathfile)
        {
            // SFTP配置（根据实际情况修改用户名和密码）
            string sftpHost = server_ip.Text;
            int sftpPort = 22; // 默认SFTP端口
            string sftpUsername = "book"; // 通常SFTP用户名是root，需确认
            string sftpPassword = "123456"; // 替换为实际SFTP密码
            string remoteFilePath = "/home/book/"; // 远程文件路径
            //string remoteFilePath = "/home/wch/wch/lizhong/lz_driver_robot/config/"; // 远程文件路径

            // 本地路径：项目输出目录下的assets文件夹
            string localDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            // 若assets文件夹不存在则创建
            if (!Directory.Exists(localDir))
            {
                AppendLog("Mkdir\r\n");
                Directory.CreateDirectory(localDir);
            }
            pathfile += ".XYZ";
            string localFilePath = System.IO.Path.Combine(localDir, pathfile);
            remoteFilePath = remoteFilePath + pathfile;

            AppendLog($"LocalFilePath {localFilePath}\r\n");
            AppendLog($"RemoteFilePath {remoteFilePath}\r\n");
            SftpClient sftpClient = null;

            try
            {
                
                await ftpClient.DownloadFileAsync(remoteFilePath, localFilePath);

                MessageBox.Show("文件下载成功!", "成功",MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载错误: {ex.Message}", "错误",MessageBoxButton.OK, MessageBoxImage.Error);
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
                double y1 = CanvasStartPoint.Y - p1.Y*TrackScale_Y;
                double x2 = CanvasStartPoint.X + p2.X*TrackScale_X;
                double y2 = CanvasStartPoint.Y - p2.Y*TrackScale_Y;

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
            double canvasX = CanvasStartPoint.X + dx * TrackScale_X;
            double canvasY = CanvasStartPoint.Y - dy * TrackScale_Y; // Canvas Y轴向下，取反

            // 步骤4：创建车辆图片元素
            Image carImage = new Image();
            // 加载PNG图片（路径需根据项目实际情况调整，确保图片“生成操作”为“Resource”）
            carImage.Source = new BitmapImage(new Uri("pack://application:,,,/assets/VUT.png"));
            carImage.Width = 30;  // 车辆图片宽度（按需调整，保持比例）
            carImage.Height = 20; // 车辆图片高度（按需调整，保持比例）

            // 步骤5：计算车辆旋转角度（将弧度航向角转为角度，用于RotateTransform）
            //double rotationAngle = (BLH_VUT.Heading - BLH_Origin.Heading) * (180 / Math.PI);
            double rotationAngle = (BLH_VUT.Heading - BLH_Origin.Heading);
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

        private double _zoom = 1.0;
        private readonly double _minZoom = 0.2;
        private readonly double _maxZoom = 20.0;
        private ScaleTransform _trackCanvasScale;
        private TranslateTransform _trackCanvasTranslate;
        private ScaleTransform _driverScale;
        private TranslateTransform _driverTranslate;

        // 新增方法：处理滚轮缩放（添加到类中）
        //private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        //{
        //    // 只在鼠标在 TrackCanvas 或 Driver 上时响应缩放（避免干扰其它控件）
        //    Point posOnTrack = e.GetPosition(TrackCanvas);
        //    bool overTrack = posOnTrack.X >= 0 && posOnTrack.Y >= 0 && posOnTrack.X <= TrackCanvas.ActualWidth && posOnTrack.Y <= TrackCanvas.ActualHeight;
        //    Point posOnDriver = e.GetPosition(Driver);
        //    bool overDriver = posOnDriver.X >= 0 && posOnDriver.Y >= 0 && posOnDriver.X <= Driver.ActualWidth && posOnDriver.Y <= Driver.ActualHeight;

        //    if (!overTrack && !overDriver) return;

        //    double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        //    double newZoom = Math.Clamp(_zoom * zoomFactor, _minZoom, _maxZoom);
        //    double scaleChange = newZoom / _zoom;
        //    if (Math.Abs(scaleChange - 1.0) < 1e-6) return;

        //    // 对 TrackCanvas 使用相对于 TrackCanvas 的鼠标位置，保持鼠标指针下的内容不动
        //    if (overTrack)
        //    {
        //        // 当前 translate + 缩放后保持鼠标点不动的公式： translate' = translate - mousePos * (scaleChange - 1)
        //        _trackCanvasTranslate.X = _trackCanvasTranslate.X - (posOnTrack.X * (scaleChange - 1));
        //        _trackCanvasTranslate.Y = _trackCanvasTranslate.Y - (posOnTrack.Y * (scaleChange - 1));
        //        _trackCanvasScale.ScaleX = newZoom;
        //        _trackCanvasScale.ScaleY = newZoom;
        //    }

        //    // 对 Driver 使用相对于 Driver 的鼠标位置（同样逻辑）
        //    if (overDriver)
        //    {
        //        _driverTranslate.X = _driverTranslate.X - (posOnDriver.X * (scaleChange - 1));
        //        _driverTranslate.Y = _driverTranslate.Y - (posOnDriver.Y * (scaleChange - 1));
        //        _driverScale.ScaleX = newZoom;
        //        _driverScale.ScaleY = newZoom;
        //    }

        //    // 同步两者的缩放值（确保在只对其中一个控件滚轮时，另一个也按相同比例缩放）
        //    // 如果你希望严格要求只有同时鼠标在两个控件上才同步，可移除下面两行
        //    _trackCanvasScale.ScaleX = newZoom;
        //    _trackCanvasScale.ScaleY = newZoom;
        //    _driverScale.ScaleX = newZoom;
        //    _driverScale.ScaleY = newZoom;

        //    _zoom = newZoom;
        //    e.Handled = true;
        //}

        private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point posOnTrack = e.GetPosition(TrackCanvas);
            bool overTrack = posOnTrack.X >= 0 && posOnTrack.Y >= 0 && posOnTrack.X <= TrackCanvas.ActualWidth && posOnTrack.Y <= TrackCanvas.ActualHeight;
            Point posOnDriver = e.GetPosition(Driver);
            bool overDriver = posOnDriver.X >= 0 && posOnDriver.Y >= 0 && posOnDriver.X <= Driver.ActualWidth && posOnDriver.Y <= Driver.ActualHeight;

            if (!overTrack && !overDriver) return;

            // 【核心优化1：提升单次缩放倍率并支持高精度滚动】
            // 标准鼠标滚轮拨动一格，e.Delta 的值通常是 120。
            // 使用 1.25 作为基础倍率，比之前的 1.1 响应快很多！
            double step = 1.25;
            double zoomFactor = Math.Pow(step, e.Delta / 120.0);

            double newZoom = Math.Clamp(_zoom * zoomFactor, _minZoom, _maxZoom);

            // 【核心优化2：忽略极微小的计算抖动，防止无效重绘】
            double scaleChange = newZoom / _zoom;
            if (Math.Abs(scaleChange - 1.0) < 0.001) return;

            // 对 TrackCanvas 保持鼠标指针下的内容不动
            if (overTrack)
            {
                _trackCanvasTranslate.X -= posOnTrack.X * (scaleChange - 1);
                _trackCanvasTranslate.Y -= posOnTrack.Y * (scaleChange - 1);
            }

            // 对 Driver 保持鼠标指针下的内容不动
            if (overDriver)
            {
                _driverTranslate.X -= posOnDriver.X * (scaleChange - 1);
                _driverTranslate.Y -= posOnDriver.Y * (scaleChange - 1);
            }

            // 同步两者的缩放值
            _trackCanvasScale.ScaleX = newZoom;
            _trackCanvasScale.ScaleY = newZoom;
            _driverScale.ScaleX = newZoom;
            _driverScale.ScaleY = newZoom;

            _zoom = newZoom;

            // 标记事件已处理，防止整个窗口跟着滚动
            e.Handled = true;
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

        private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var tabItem = sender as TabItem;
            if (tabItem != null)
            {
                DrawCenterLine();
                UpdateWarningAreas();

                

            }
        }

        private void UpdateDriverDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                Track_TTC.Content = robotData_Part2.VUTPF_TTC_interest.ToString("F2");
                Track_Vel.Content = robotData_Part2.VUTMP_Vertical_velocity.ToString("F2");
                Track_X.Content = robotData_Part2.VUTMP_Actual_X.ToString("F2");
                Track_Y.Content = robotData_Part2.VUTPF_Lateral_Err.ToString("F2");
            });

            // 获取车辆Y坐标（模拟数据，实际应从数据结构获取）
            currentVehicleY = robotData_Part2.VUTPF_Lateral_Err;

            // 更新车辆位置
            UpdateVehiclePosition();

            // 更新警告闪烁
            UpdateWarningBlink();
        }
        private void UpdateVehiclePosition()
        {
            List<UIElement> elementsToRemove = new List<UIElement>();
            foreach (UIElement element in Driver.Children)
            {
                if (element is FrameworkElement fe && fe.Tag != null && fe.Tag.ToString() == "A2")
                {
                    elementsToRemove.Add(element);
                }
            }
            foreach (UIElement element in elementsToRemove)
            {
                Driver.Children.Remove(element);
            }

            double canvasWidth = Driver.ActualWidth;
            double canvasHeight = Driver.ActualHeight;
            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;

            // 将Y坐标转换为像素位置（Canvas中心为原点）
            // Y>0：车辆偏左，Y<0：车辆偏右
            double vehicleYPixels = centerX + (currentVehicleY * 5);

            
            // 若BLH_VUT.Heading本身是“角度”，则无需转弧度，直接用：
            // double rotationAngle = BLH_VUT.Heading - BLH_Origin.Heading;
            // 步骤4：创建车辆图片元素
            Image VehicleImage = new Image();
            VehicleImage.Width = 100;
            VehicleImage.Height = 60;
            // 加载PNG图片（路径需根据项目实际情况调整，确保图片“生成操作”为“Resource”）
            VehicleImage.Source = new BitmapImage(new Uri("pack://application:,,,/assets/VUT.png"));

            double rotationAngle = robotData_Part2.VUTPF_Heading_Err-90;
            // 步骤6：设置“围绕图片中心旋转”的变换
            RotateTransform rotateTransform = new RotateTransform(
                rotationAngle,       // 旋转角度
                VehicleImage.Width / 2,  // 旋转中心X（图片中心）
                VehicleImage.Height / 2  // 旋转中心Y（图片中心）
            );
            VehicleImage.RenderTransform = rotateTransform;
            
            // 设置车辆图片位置（垂直居中）
            Canvas.SetLeft(VehicleImage, vehicleYPixels - VehicleImage.Width / 2);
            Canvas.SetTop(VehicleImage, centerY - VehicleImage.Height / 2);
            VehicleImage.Tag = "A2";

            Driver.Children.Add(VehicleImage);
        }
        // / 绘制Canvas中心的虚线
        private void DrawCenterLine()
        {
            double canvasWidth = Driver.ActualWidth;
            double canvasHeight = Driver.ActualHeight;

            // 中心虚线（Canvas中心为原点）
            double centerX = canvasWidth / 2;
            CenterDashedLine.X1 = centerX;
            CenterDashedLine.Y1 = 0;
            CenterDashedLine.X2 = centerX;
            CenterDashedLine.Y2 = canvasHeight;
        }
        /// 更新警告区域的大小和位置
        private void UpdateWarningAreas()
        {
            double canvasWidth = Driver.ActualWidth;
            double canvasHeight = Driver.ActualHeight;
            double centerX = canvasWidth / 2;

            // 左侧警告区域（从Canvas左边界到中心线）
            LeftWarningArea.Width = centerX;
            LeftWarningArea.Height = canvasHeight;
            Canvas.SetLeft(LeftWarningArea, 0);
            Canvas.SetTop(LeftWarningArea, 0);

            // 右侧警告区域（从中心线到Canvas右边界）
            RightWarningArea.Width = centerX;
            RightWarningArea.Height = canvasHeight;
            Canvas.SetLeft(RightWarningArea, centerX);
            Canvas.SetTop(RightWarningArea, 0);
        }

        private void UpdateWarningBlink()
        {
            // 根据Y值控制警告区域闪烁
            if (robotData_Part2.VUTPF_Lateral_Err < -0.2) // 车辆偏左（Y>0）
            {
                isLeftWarningBlinking = !isLeftWarningBlinking;
                isRightWarningBlinking = false;

                //LeftWarningArea.Fill = isLeftWarningBlinking ? Brushes.Red : Brushes.Transparent;
                LeftWarningArea.Fill = Brushes.Red;
                RightWarningArea.Fill = Brushes.Transparent;
                //AppendLog("LEFT WARNING\r\n");
            }
            else if (robotData_Part2.VUTPF_Lateral_Err > 0.2) // 车辆偏右（Y<0）
            {
                isRightWarningBlinking = !isRightWarningBlinking;
                isLeftWarningBlinking = false;

                RightWarningArea.Fill = isRightWarningBlinking ? Brushes.Red : Brushes.Transparent;
                RightWarningArea.Fill = Brushes.Red;
                LeftWarningArea.Fill = Brushes.Transparent;
                //AppendLog("RIGHT WARNING\r\n");
            }
            else // 车辆在中间（Y=0）
            {
                isLeftWarningBlinking = false;
                isRightWarningBlinking = false;

                LeftWarningArea.Fill = Brushes.Transparent;
                RightWarningArea.Fill = Brushes.Transparent;
            }
        }


        // 专门用于解析 C/C++ 定长字节数组的字符串
        private string GetStringFromByteArray(byte[] bytes)
        {
            if (bytes == null) return string.Empty;

            // 找到第一个 \0 (0x00) 的索引位置
            int nullIndex = Array.IndexOf(bytes, (byte)0);

            // 如果找到了 \0，就只取 \0 前面的长度；如果没找到，就取整个数组长度
            int length = nullIndex >= 0 ? nullIndex : bytes.Length;

            // 按照实际有效长度转换为字符串
            return System.Text.Encoding.ASCII.GetString(bytes, 0, length);
        }

        private void Calculate_COG_Button_Click(object sender, RoutedEventArgs e)
        {
            debugData.Header1 = 0xAA;
            debugData.Header2 = 0x55;
            debugData.DeviceType = 0x01; // 假设设备类型为1
            debugData.FunctionCode = 0x0084; // 假设功能码为1
            string paramStr = "Calculate_COG";

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



        // 在 MainWindow 类中添加以下方法
        private GMapPolygon _electronicFence;
        private List<PointLatLng> _fencePoints;
        // 用于记录车辆上一次是否在围栏外，防止疯狂打印日志卡死 UI
        private bool _wasOutsideFence = false;
        private void AddElectronicFence()
        {
            UpdateMapGeofence();
        }

        private void InitializeDefaultGeofenceUI()
        {
            sGeofence.nPointCount = 4;
            if (sGeofence.vecPoints == null || sGeofence.vecPoints.Length < 4)
                sGeofence.vecPoints = new SPoint[4];

            sGeofence.vecPoints[0] = new SPoint { dLongitude = 112.511610870102, dLatitude = 28.3542445550822 };
            sGeofence.vecPoints[1] = new SPoint { dLongitude = 112.511510038889, dLatitude = 28.3543953380492 };
            sGeofence.vecPoints[2] = new SPoint { dLongitude = 112.51096714, dLatitude = 28.35402939 };
            sGeofence.vecPoints[3] = new SPoint { dLongitude = 112.51107334, dLatitude = 28.35390217 };

            uwb_fence_p1_lon.Text = sGeofence.vecPoints[0].dLongitude.ToString("F8");
            uwb_fence_p1_lat.Text = sGeofence.vecPoints[0].dLatitude.ToString("F8");
            uwb_fence_p2_lon.Text = sGeofence.vecPoints[1].dLongitude.ToString("F8");
            uwb_fence_p2_lat.Text = sGeofence.vecPoints[1].dLatitude.ToString("F8");
            uwb_fence_p3_lon.Text = sGeofence.vecPoints[2].dLongitude.ToString("F8");
            uwb_fence_p3_lat.Text = sGeofence.vecPoints[2].dLatitude.ToString("F8");
            uwb_fence_p4_lon.Text = sGeofence.vecPoints[3].dLongitude.ToString("F8");
            uwb_fence_p4_lat.Text = sGeofence.vecPoints[3].dLatitude.ToString("F8");
        }

        private void UpdateMapGeofence()
        {
            if (OfflineMap == null || sGeofence.vecPoints == null || sGeofence.vecPoints.Length < 4)
                return;

            if (_electronicFence != null)
                OfflineMap.Markers.Remove(_electronicFence);

            _fencePoints = new List<PointLatLng>
            {
                new PointLatLng(sGeofence.vecPoints[0].dLatitude, sGeofence.vecPoints[0].dLongitude),
                new PointLatLng(sGeofence.vecPoints[1].dLatitude, sGeofence.vecPoints[1].dLongitude),
                new PointLatLng(sGeofence.vecPoints[2].dLatitude, sGeofence.vecPoints[2].dLongitude),
                new PointLatLng(sGeofence.vecPoints[3].dLatitude, sGeofence.vecPoints[3].dLongitude),
            };

            _electronicFence = new GMapPolygon(_fencePoints);

            System.Windows.Shapes.Path polygonPath = new System.Windows.Shapes.Path
            {
                Stroke = Brushes.OrangeRed,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 69, 0)),
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            _electronicFence.Shape = polygonPath;
            OfflineMap.Markers.Add(_electronicFence);
        }

        // 添加 Loaded 事件处理方法
        private void OfflineMap_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 【关键】因为我们自己写了 Provider 来读本地文件，
                // GMap 会把我们写的 Provider 当作一个"服务器"。
                // 所以这里必须设置为 ServerOnly 或 ServerAndCache，千万不要设为 CacheOnly，否则它不会触发查询。
                //GMaps.Instance.Mode = AccessMode.ServerOnly;
                GMaps.Instance.Mode = AccessMode.ServerAndCache;

                // 2. 指定你的 mbtiles 文件路径（假设你把它放在了程序运行目录的 assets 文件夹下）
                string mbtilesPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "satellite_tiles_1.mbtiles");

                // 3. 将地图提供者设置为我们刚刚写的自定义提供者
                OfflineMap.MapProvider = new MBTilesMapProvider(mbtilesPath);

                // 4. 设置缩放级别与中心点 (确保你设置的中心点在你的 mbtiles 离线包覆盖范围内)
                OfflineMap.MinZoom = 10;
                OfflineMap.MaxZoom = 18;
                OfflineMap.Zoom = 15;
                OfflineMap.Position = new PointLatLng(28.35335215, 112.5094273); // 替换为你场地的实际经纬度
                OfflineMap.ShowCenter = false;
                AddElectronicFence();

                // 初始化车辆标记...
                if (carMarker == null)
                {
                    carMarker = new GMapMarker(OfflineMap.Position);
                    carMarker.Shape = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Fill = Brushes.Red,
                        Stroke = Brushes.White,
                        StrokeThickness = 1.5,
                        ToolTip = "VUT 车辆"
                    };
                    OfflineMap.Markers.Add(carMarker);
                }

            }
            catch (Exception ex)
            {
                AppendLog($"加载 MBTiles 地图异常: {ex.Message}");
            }
        }

        private void RenderTrajectoryPlot()
        {
            // 如果“轨迹图”Tab当前没有被激活选中，为了节约CPU资源可以不渲染
            // （此处作为选做项，可以直接强制重绘）

            // 1. 批量同步并更新 VUT 折线
            lock (_vutPointsCache)
            {
                if (_vutPointsCache.Count > 0)
                {
                    var collection = new PointCollection();
                    foreach (var pt in _vutPointsCache)
                    {
                        // Y取反：因为笛卡尔坐标系Y向上，Canvas的Y轴向下
                        collection.Add(new System.Windows.Point(pt.X, -pt.Y));
                    }
                    Polyline_VUT.Points = collection;

                    // 移动最新靶点
                    var last = _vutPointsCache[^1];
                    Marker_VUT.Visibility = Visibility.Visible;
                    Canvas.SetLeft(Marker_VUT, last.X - 0.5);
                    Canvas.SetTop(Marker_VUT, -last.Y - 0.5);
                }
            }

            // 2. 批量同步并更新 SPT 折线
            lock (_sptPointsCache)
            {
                if (_sptPointsCache.Count > 0)
                {
                    var collection = new PointCollection();
                    foreach (var pt in _sptPointsCache)
                    {
                        collection.Add(new System.Windows.Point(pt.X, -pt.Y));
                    }
                    Polyline_SPT.Points = collection;

                    var last = _sptPointsCache[^1];
                    Marker_SPT.Visibility = Visibility.Visible;
                    Canvas.SetLeft(Marker_SPT, last.X - 0.5);
                    Canvas.SetTop(Marker_SPT, -last.Y - 0.5);
                }
            }

            // 3. 批量同步并更新 VT 折线
            lock (_vtPointsCache)
            {
                if (_vtPointsCache.Count > 0)
                {
                    var collection = new PointCollection();
                    foreach (var pt in _vtPointsCache)
                    {
                        collection.Add(new System.Windows.Point(pt.X, -pt.Y));
                    }
                    Polyline_VT.Points = collection;

                    var last = _vtPointsCache[^1];
                    Marker_VT.Visibility = Visibility.Visible;
                    Canvas.SetLeft(Marker_VT, last.X - 0.5);
                    Canvas.SetTop(Marker_VT, -last.Y - 0.5);
                }
            }
        }

        private void Btn_ResetPlot_Click(object sender, RoutedEventArgs e)
        {
            if (PlotCanvas.ActualWidth == 0 || PlotCanvas.ActualHeight == 0) return;

            // 1. 将缩放倍率重置为中等适中大小 (如 8 像素/米)
            PlotScale.ScaleX = 8.0;
            PlotScale.ScaleY = 8.0;

            // 2. 将平移矩阵的 (0,0) 位置重新对齐到 Canvas 画布的正中央
            PlotTranslate.X = PlotCanvas.ActualWidth / 2;
            PlotTranslate.Y = PlotCanvas.ActualHeight / 2;

            // 3. 在中心建立一个显眼的十字标靶或中心参考圆
            DrawCenterTarget();

            // 4. 更新文字提示
            Txt_PlotScaleInfo.Text = "网格主间距: 5m | 缩放倍率: 8.0x (已复位居中)";
        }

        private void DrawCenterTarget()
        {
            // 移除旧的中心十字架/原点标志
            var oldTargets = PlotCanvas.Children.OfType<FrameworkElement>().Where(x => x.Tag?.ToString() == "CenterTarget").ToList();
            foreach (var item in oldTargets) PlotCanvas.Children.Remove(item);

            // 绘制一个中心参考圆圈 (半径 2 米)
            System.Windows.Shapes.Ellipse centerCircle = new System.Windows.Shapes.Ellipse
            {
                Width = 4,
                Height = 4, // 对应直径 4 米
                Stroke = Brushes.DimGray,
                StrokeThickness = 0.08,
                StrokeDashArray = new DoubleCollection { 2, 2 },
                Tag = "CenterTarget"
            };
            Canvas.SetLeft(centerCircle, -2);
            Canvas.SetTop(centerCircle, -2);

            // 绘制一个实心小原点 (0,0)
            System.Windows.Shapes.Ellipse centerDot = new System.Windows.Shapes.Ellipse
            {
                Width = 0.5,
                Height = 0.5,
                Fill = Brushes.Black,
                Tag = "CenterTarget"
            };
            Canvas.SetLeft(centerDot, -0.25);
            Canvas.SetTop(centerDot, -0.25);

            PlotCanvas.Children.Add(centerCircle);
            PlotCanvas.Children.Add(centerDot);
        }
        // 初始化/改变尺寸时重置中心点
        private void ResetPlotCanvasCenter()
        {
            double cx = PlotCanvas.ActualWidth / 2;
            double cy = PlotCanvas.ActualHeight / 2;

            // 将 Canvas 的基础原点定位到中心
            PlotTranslate.X = cx;
            PlotTranslate.Y = cy;

            // 动态生成和铺设背景网格虚线
            DrawBackgroundGrid();
        }

        // 动态画背景网格虚线（类似参考图）
        private void DrawBackgroundGrid()
        {
            // 清理老旧的网格线（保留 Polyline 和 Marker）
            var toRemove = PlotCanvas.Children.OfType<System.Windows.Shapes.Line>().ToList();
            foreach (var item in toRemove) PlotCanvas.Children.Remove(item);

            // 建立前后各200米的虚拟坐标网格，间距为5米一格
            int range = 200;
            int interval = 5;

            // 绘制横/纵轴网格细线
            for (int i = -range; i <= range; i += interval)
            {
                // 纵向平行线
                var vLine = new System.Windows.Shapes.Line
                {
                    X1 = i,
                    Y1 = -range,
                    X2 = i,
                    Y2 = range,
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    StrokeThickness = 0.05,
                    StrokeDashArray = new DoubleCollection { 1, 2 }
                };
                // 横向平行线
                var hLine = new System.Windows.Shapes.Line
                {
                    X1 = -range,
                    Y1 = i,
                    X2 = range,
                    Y2 = i,
                    Stroke = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    StrokeThickness = 0.05,
                    StrokeDashArray = new DoubleCollection { 1, 2 }
                };

                // 突出粗显中心基准轴交叉线
                if (i == 0)
                {
                    vLine.Stroke = Brushes.LightGray; vLine.StrokeThickness = 0.1; vLine.StrokeDashArray = null;
                    hLine.Stroke = Brushes.LightGray; hLine.StrokeThickness = 0.1; hLine.StrokeDashArray = null;
                }

                // 插入到底层，防止遮挡定位线条
                PlotCanvas.Children.Insert(0, vLine);
                PlotCanvas.Children.Insert(0, hLine);
            }
        }



        private void PlotCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            System.Windows.Point mousePos = e.GetPosition(PlotCanvas);
            double zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;

            double oldScaleX = PlotScale.ScaleX;
            double oldScaleY = PlotScale.ScaleY;

            // 设置安全缩放区间（防止无限放大或缩小）
            double newScaleX = Math.Clamp(oldScaleX * zoomFactor, 1.0, 150.0);
            double newScaleY = Math.Clamp(oldScaleY * zoomFactor, 1.0, 150.0);

            PlotScale.ScaleX = newScaleX;
            PlotScale.ScaleY = newScaleY;

            // 调整平移量，实现以鼠标指针为中心进行完美缩放
            PlotTranslate.X -= (mousePos.X * (newScaleX - oldScaleX));
            PlotTranslate.Y -= (mousePos.Y * (newScaleY - oldScaleY));

            Txt_PlotScaleInfo.Text = $"网格主间距: 5m | 缩放倍率: {newScaleX:F1}x";
            e.Handled = true;
        }

        private void PlotCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = sender as FrameworkElement;
            if (container == null) return;

            _isPlotDragging = true;
            _mouseStartPoint = e.GetPosition(container);
            container.CaptureMouse();
        }

        private void PlotCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var container = sender as FrameworkElement;
            if (container != null)
            {
                _isPlotDragging = false;
                container.ReleaseMouseCapture();
            }
        }

        private void PlotCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPlotDragging) return;

            var container = sender as FrameworkElement;
            if (container == null) return;

            System.Windows.Point currentPos = e.GetPosition(container);
            double offsetX = currentPos.X - _mouseStartPoint.X;
            double offsetY = currentPos.Y - _mouseStartPoint.Y;

            // 更新平移变换值
            PlotTranslate.X += offsetX;
            PlotTranslate.Y += offsetY;

            _mouseStartPoint = currentPos;
        }


    }

}



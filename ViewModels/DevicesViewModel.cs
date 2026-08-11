using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

/// <summary>设备列表行视图模型（Admin 设备 Tab 绑定）。</summary>
public sealed class DeviceListItem
{
    /// <summary>设备 Id。</summary>
    public long Id { get; init; }

    /// <summary>设备名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>协议显示名。</summary>
    public string ProtocolName { get; init; } = string.Empty;

    /// <summary>连接端点摘要（IP:端口 / 串口 / HTTP）。</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>轮询间隔（毫秒）；0 表示采集关闭。</summary>
    public int PollInterval { get; init; }

    /// <summary>采集状态文案（「采集关闭」或「轮询 N ms」）。</summary>
    public string CollectionText { get; init; } = string.Empty;

    /// <summary>是否启用该设备。</summary>
    public bool IsActive { get; init; }

    /// <summary>列表排序序号。</summary>
    public int SortOrder { get; init; }

    /// <summary>原始 <see cref="Device"/> 实体，供编辑/删除使用。</summary>
    public Device Source { get; init; } = null!;
}

/// <summary>物模型变量列表行视图模型（Admin 物模型 Tab 绑定）。</summary>
public sealed class VariableListItem
{
    /// <summary>变量 Id。</summary>
    public long Id { get; init; }

    /// <summary>寄存器/点位地址。</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>数据类型显示名。</summary>
    public string DataTypeName { get; init; } = string.Empty;

    /// <summary>别名。</summary>
    public string Alias { get; init; } = string.Empty;

    /// <summary>描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>读写权限显示名。</summary>
    public string ReadWriteName { get; init; } = string.Empty;

    /// <summary>是否在手动操作页展示。</summary>
    public bool ShowOnDefinedPage { get; init; }

    /// <summary>手动操作模式摘要（读取/写入/点动等）。</summary>
    public string DefinedPageModeText { get; init; } = string.Empty;

    /// <summary>列表单行摘要（地址、类型、权限、描述、手动操作等）。</summary>
    public string DisplayLine { get; init; } = string.Empty;

    /// <summary>原始 <see cref="DeviceVariable"/> 实体，供编辑/删除使用。</summary>
    public DeviceVariable Source { get; init; } = null!;
}

/// <summary>设备下拉选择项（物模型 / 调试 Tab 共用）。</summary>
public sealed class DevicePickerItem
{
    /// <summary>设备 Id。</summary>
    public long Id { get; init; }

    /// <summary>下拉显示文本（Id、名称、协议）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>设备协议类型，用于判断 HTTP 等字段可见性。</summary>
    public ProtocolType ProtocolType { get; init; }
}

/// <summary>调试 Tab 物模型变量下拉项。</summary>
public sealed class DebugVariableItem
{
    /// <summary>变量 Id。</summary>
    public long Id { get; init; }

    /// <summary>寄存器/点位地址。</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>别名。</summary>
    public string Alias { get; init; } = string.Empty;

    /// <summary>数据类型。</summary>
    public DataType DataType { get; init; }

    /// <summary>下拉显示文本（别名、地址、描述）。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>原始 <see cref="DeviceVariable"/> 实体。</summary>
    public DeviceVariable Source { get; init; } = null!;
}

/// <summary>
/// 设备管理页（Admin）：设备 / 物模型 / MQTT / 调试读写 / 配置备份。
/// 物模型采集三开关控制轮询、状态展示与 MQTT 周期上报；「手动操作」开关仅影响右侧按钮。
/// 调试读写请走注入的 <see cref="IDeviceDebugApi"/>。
/// </summary>
public partial class DevicesViewModel : ViewModelBase
{
    /// <summary>HTTP 插件配置 JSON 序列化选项（camelCase、忽略 null）。</summary>
    private static readonly JsonSerializerOptions HttpConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>网关设备 CRUD 与物模型服务。</summary>
    private readonly GatewayDeviceService _service;

    /// <summary>设备调试读写 API（经历史记录）。</summary>
    private readonly IDeviceDebugApi _debugApi;

    /// <summary>编辑已有设备时的原始 Id（Id 变更时用于 Update）。</summary>
    private long _editingDeviceOldId;

    /// <summary>当前设备编辑是否为新建模式。</summary>
    private bool _isNewDevice;

    /// <summary>
    /// 构造并初始化下拉选项，随后异步加载设备列表。
    /// </summary>
    /// <param name="service">网关设备服务。</param>
    /// <param name="debugApi">设备调试 API。</param>
    public DevicesViewModel(GatewayDeviceService service, IDeviceDebugApi debugApi)
    {
        _service = service;
        _debugApi = debugApi;
        ProtocolOptions = ProtocolDisplay.ProtocolNames.ToList();
        DataTypeOptions = ProtocolDisplay.DataTypeNames.ToList();
        ReadWriteOptions = ProtocolDisplay.ReadWriteNames.ToList();
        DefinedPageOperationOptions = ["读取", "写入"];
        ImportModeOptions = ["merge", "replaceAll"];
        UpdateFieldVisibility();
        // 构造完成后异步拉取设备列表
        _ = RefreshDevicesAsync();
    }

    // ── Tabs ──────────────────────────────────────────────────────────

    /// <summary>当前选中的 Tab 索引（0=设备，1=物模型，2=MQTT，3=调试，4=备份）。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Tab 切换时按需懒加载对应页数据。
    /// </summary>
    /// <param name="value">新 Tab 索引。</param>
    partial void OnSelectedTabIndexChanged(int value)
    {
        switch (value)
        {
            case 1:
                // 物模型 Tab：确保设备下拉已填充
                _ = EnsureDevicePickerAsync();
                break;
            case 2:
                // MQTT Tab：加载配置
                _ = LoadMqttAsync();
                break;
            case 3:
                // 调试 Tab：刷新设备与物模型上下文
                _ = RefreshDebugDeviceContextAsync();
                break;
        }
    }

    // ── Shared ────────────────────────────────────────────────────────

    /// <summary>页面底部状态/提示消息。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>是否正在执行异步操作（防重入）。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>协议类型下拉选项。</summary>
    public IReadOnlyList<string> ProtocolOptions { get; }

    /// <summary>数据类型下拉选项。</summary>
    public IReadOnlyList<string> DataTypeOptions { get; }

    /// <summary>读写权限下拉选项。</summary>
    public IReadOnlyList<string> ReadWriteOptions { get; }

    /// <summary>自定义页操作下拉选项（读取/写入）。</summary>
    public IReadOnlyList<string> DefinedPageOperationOptions { get; }

    /// <summary>备份导入模式下拉选项（merge / replaceAll）。</summary>
    public IReadOnlyList<string> ImportModeOptions { get; }

    // ── Devices tab ───────────────────────────────────────────────────

    /// <summary>设备列表绑定集合。</summary>
    public ObservableCollection<DeviceListItem> DeviceListItems { get; } = [];

    /// <summary>设备名称搜索关键字。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前选中的设备列表项。</summary>
    [ObservableProperty]
    private DeviceListItem? _selectedDevice;

    /// <summary>是否处于设备编辑面板。</summary>
    [ObservableProperty]
    private bool _isEditingDevice;

    /// <summary>编辑中的设备 Id。</summary>
    [ObservableProperty]
    private long _editId;

    /// <summary>编辑中的设备名称。</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>编辑中的协议类型索引。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTcpFields))]
    [NotifyPropertyChangedFor(nameof(ShowSerialFields))]
    [NotifyPropertyChangedFor(nameof(ShowPlcFields))]
    [NotifyPropertyChangedFor(nameof(ShowHttpFields))]
    private int _editProtocol;

    /// <summary>编辑中的 IP 地址（TCP 协议）。</summary>
    [ObservableProperty]
    private string _editIp = string.Empty;

    /// <summary>编辑中的端口号。</summary>
    [ObservableProperty]
    private int _editPort = 502;

    /// <summary>编辑中的串口名。</summary>
    [ObservableProperty]
    private string _editPortName = "COM1";

    /// <summary>编辑中的波特率。</summary>
    [ObservableProperty]
    private int _editBaudRate = 9600;

    /// <summary>编辑中的数据位。</summary>
    [ObservableProperty]
    private int _editDataBits = 8;

    /// <summary>编辑中的停止位。</summary>
    [ObservableProperty]
    private int _editStopBits = 1;

    /// <summary>编辑中的校验位索引。</summary>
    [ObservableProperty]
    private int _editParity;

    /// <summary>编辑中的 PLC 型号/版本。</summary>
    [ObservableProperty]
    private string _editPlcVersion = string.Empty;

    /// <summary>编辑中的列表排序序号。</summary>
    [ObservableProperty]
    private int _editSortOrder;

    /// <summary>编辑中是否启用采集（轮询）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPollIntervalEditor))]
    private bool _editCollectionEnabled = true;

    /// <summary>编辑中的轮询间隔（毫秒）。</summary>
    [ObservableProperty]
    private int _editPollInterval = 1000;

    /// <summary>是否显示轮询间隔编辑器（采集开启时）。</summary>
    public bool ShowPollIntervalEditor => EditCollectionEnabled;

    /// <summary>编辑中设备是否启用。</summary>
    [ObservableProperty]
    private bool _editIsActive = true;

    /// <summary>HTTP 插件：登录 URL。</summary>
    [ObservableProperty]
    private string _hcLoginUrl = string.Empty;

    /// <summary>HTTP 插件：登录 HTTP 方法。</summary>
    [ObservableProperty]
    private string _hcLoginMethod = "POST";

    /// <summary>HTTP 插件：登录请求体。</summary>
    [ObservableProperty]
    private string _hcLoginBody = string.Empty;

    /// <summary>HTTP 插件：Token JSON 路径。</summary>
    [ObservableProperty]
    private string _hcTokenPath = "data.token";

    /// <summary>HTTP 插件：查询 URL。</summary>
    [ObservableProperty]
    private string _hcQueryUrl = string.Empty;

    /// <summary>HTTP 插件：查询 HTTP 方法。</summary>
    [ObservableProperty]
    private string _hcQueryMethod = "GET";

    /// <summary>HTTP 插件：查询请求体。</summary>
    [ObservableProperty]
    private string _hcQueryBody = string.Empty;

    /// <summary>HTTP 插件：查询请求头 Key 列表（逗号分隔）。</summary>
    [ObservableProperty]
    private string _hcQueryHeaderKeys = string.Empty;

    /// <summary>HTTP 插件：响应体 JSON 根路径。</summary>
    [ObservableProperty]
    private string _hcBodyPath = "data";

    /// <summary>HTTP 插件：嵌套数组 JSON 路径。</summary>
    [ObservableProperty]
    private string _hcNestedPath = string.Empty;

    /// <summary>PLC 型号下拉选项（随协议动态重建）。</summary>
    public ObservableCollection<string> PlcVersionOptions { get; } = [];

    /// <summary>是否显示 TCP 连接字段（IP/端口）。</summary>
    public bool ShowTcpFields => ProtocolDisplay.IsTcp((ProtocolType)EditProtocol);

    /// <summary>是否显示串口连接字段。</summary>
    public bool ShowSerialFields => ProtocolDisplay.IsSerial((ProtocolType)EditProtocol);

    /// <summary>是否显示 PLC 型号字段。</summary>
    public bool ShowPlcFields => ProtocolDisplay.NeedsPlcVersion((ProtocolType)EditProtocol);

    /// <summary>是否显示 HTTP 插件配置字段。</summary>
    public bool ShowHttpFields => ProtocolDisplay.IsHttp((ProtocolType)EditProtocol);

    /// <summary>
    /// 协议变更时刷新字段可见性与 PLC 下拉。
    /// </summary>
    /// <param name="value">新协议索引。</param>
    partial void OnEditProtocolChanged(int value) => UpdateFieldVisibility();

    /// <summary>
    /// 按当前协议更新 TCP/串口/PLC/HTTP 字段可见性，并重建 PLC 型号下拉与默认端口。
    /// </summary>
    private void UpdateFieldVisibility()
    {
        // 通知计算属性刷新（TCP/串口/PLC/HTTP 区块显隐）
        OnPropertyChanged(nameof(ShowTcpFields));
        OnPropertyChanged(nameof(ShowSerialFields));
        OnPropertyChanged(nameof(ShowPlcFields));
        OnPropertyChanged(nameof(ShowHttpFields));

        var protocol = (ProtocolType)EditProtocol;
        // 先保存目标值，避免 ComboBox Items Clear 时双向绑定把 EditPlcVersion 冲成空
        var desiredPlcVersion = EditPlcVersion;

        PlcVersionOptions.Clear();
        if (protocol == ProtocolType.SiemensClient)
        {
            // 西门子：填充型号列表并规范化选中项
            foreach (var v in ProtocolDisplay.SiemensVersions)
                PlcVersionOptions.Add(v);

            var siemens = IoTClientFactory.NormalizeSiemensVersion(desiredPlcVersion);
            EditPlcVersion = PlcVersionOptions.Contains(siemens)
                ? siemens
                : ProtocolDisplay.SiemensVersions[0];
        }
        else if (protocol == ProtocolType.MitsubishiClient)
        {
            // 三菱：保留已有型号或取默认首项
            foreach (var v in ProtocolDisplay.MitsubishiVersions)
                PlcVersionOptions.Add(v);

            EditPlcVersion = !string.IsNullOrWhiteSpace(desiredPlcVersion)
                             && PlcVersionOptions.Contains(desiredPlcVersion)
                ? desiredPlcVersion
                : ProtocolDisplay.MitsubishiVersions[0];
        }
        else
        {
            EditPlcVersion = string.Empty;
        }

        // 按协议修正常用默认端口
        EditPort = protocol switch
        {
            ProtocolType.ModbusTcp when EditPort is 0 or 102 => 502,
            ProtocolType.SiemensClient when EditPort is 0 or 502 => 102,
            ProtocolType.MitsubishiClient when EditPort is 0 or 502 or 102 => 6000,
            ProtocolType.OmronFinsClient when EditPort is 0 or 502 or 102 => 9600,
            _ => EditPort,
        };
    }

    /// <summary>
    /// 刷新设备列表（支持按名称搜索），并同步重建设备下拉。
    /// </summary>
    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var name = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
            var devices = await _service.ListDevicesAsync(name);
            DeviceListItems.Clear();
            foreach (var d in devices.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                DeviceListItems.Add(ToListItem(d));

            RebuildDevicePicker(devices);
            StatusMessage = $"已加载 {DeviceListItems.Count} 台设备";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载设备失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 进入新建设备编辑面板并重置表单默认值。
    /// </summary>
    [RelayCommand]
    private void NewDevice()
    {
        _isNewDevice = true;
        _editingDeviceOldId = 0;
        EditId = NextSuggestedDeviceId();
        EditName = string.Empty;
        EditProtocol = 0;
        EditIp = "127.0.0.1";
        EditPort = 502;
        EditPortName = "COM1";
        EditBaudRate = 9600;
        EditDataBits = 8;
        EditStopBits = 1;
        EditParity = 0;
        EditPlcVersion = string.Empty;
        EditSortOrder = 0;
        EditCollectionEnabled = true;
        EditPollInterval = 1000;
        EditIsActive = true;
        ClearHttpFields();
        UpdateFieldVisibility();
        IsEditingDevice = true;
        StatusMessage = "新建设备";
    }

    /// <summary>
    /// 将当前选中设备载入编辑面板。
    /// </summary>
    [RelayCommand]
    private void EditSelectedDevice()
    {
        if (SelectedDevice is null)
        {
            StatusMessage = "请先选择一台设备";
            return;
        }

        var d = SelectedDevice.Source;
        _isNewDevice = false;
        _editingDeviceOldId = d.Id;
        EditId = d.Id;
        EditName = d.Name;
        EditIp = d.Ip;
        EditPort = d.Port;
        EditPortName = d.PortName;
        EditBaudRate = d.BaudRate;
        EditDataBits = d.DataBits;
        EditStopBits = d.StopBits;
        EditParity = d.Parity;
        EditSortOrder = d.SortOrder;
        EditCollectionEnabled = d.PollInterval > 0;
        EditPollInterval = d.PollInterval > 0 ? d.PollInterval : 1000;
        EditIsActive = d.IsActive;
        LoadHttpFields(d.PluginConfigJson);

        var plcVersion = d.PlcVersion ?? string.Empty;
        if (d.ProtocolType == ProtocolType.SiemensClient)
            plcVersion = IoTClientFactory.NormalizeSiemensVersion(plcVersion);

        // 顺序：先 PLC 型号 → 再协议（会重建下拉）→ 最后强制回写选中项
        EditPlcVersion = plcVersion;
        EditProtocol = (int)d.ProtocolType;
        UpdateFieldVisibility();
        ApplyPlcVersionSelection(plcVersion);

        IsEditingDevice = true;
        StatusMessage = $"编辑设备 #{d.Id}";
    }

    /// <summary>
    /// 在 PLC 型号下拉重建后，强制 ComboBox 选中与实体一致的项。
    /// </summary>
    /// <param name="plcVersion">目标 PLC 型号；可为 null。</param>
    private void ApplyPlcVersionSelection(string? plcVersion)
    {
        if (PlcVersionOptions.Count == 0)
        {
            EditPlcVersion = string.Empty;
            return;
        }

        var desired = plcVersion ?? string.Empty;
        if ((ProtocolType)EditProtocol == ProtocolType.SiemensClient)
            desired = IoTClientFactory.NormalizeSiemensVersion(desired);

        var matched = PlcVersionOptions.FirstOrDefault(x =>
            string.Equals(x, desired, StringComparison.OrdinalIgnoreCase));

        // 先清空再赋值，触发 ComboBox SelectedItem 重新匹配
        EditPlcVersion = string.Empty;
        EditPlcVersion = matched ?? PlcVersionOptions[0];
    }

    /// <summary>
    /// 校验并保存设备（新建或更新），成功后刷新列表。
    /// </summary>
    [RelayCommand]
    private async Task SaveDeviceAsync()
    {
        if (IsBusy)
            return;

        if (string.IsNullOrWhiteSpace(EditName))
        {
            StatusMessage = "设备名称不能为空";
            return;
        }

        if (EditId <= 0)
        {
            StatusMessage = "设备 Id 必须为正整数";
            return;
        }

        try
        {
            IsBusy = true;
            var device = BuildDeviceFromEditor();
            if (_isNewDevice)
            {
                await _service.CreateDeviceAsync(device);
                StatusMessage = $"已创建设备 #{device.Id}";
            }
            else
            {
                await _service.UpdateDeviceAsync(_editingDeviceOldId, device);
                StatusMessage = $"已更新设备 #{device.Id}";
            }

            IsEditingDevice = false;
            await RefreshDevicesInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "保存设备失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 删除当前选中设备并刷新列表。
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedDeviceAsync()
    {
        if (SelectedDevice is null)
        {
            StatusMessage = "请先选择一台设备";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var id = SelectedDevice.Id;
            await _service.DeleteDeviceAsync(id);
            // 删除后关闭编辑态并清空选中
            IsEditingDevice = false;
            SelectedDevice = null;
            StatusMessage = $"已删除设备 #{id}";
            await RefreshDevicesInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "删除设备失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 取消设备编辑并关闭编辑面板。
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingDevice = false;
        StatusMessage = "已取消编辑";
    }

    // ── Variables tab ─────────────────────────────────────────────────

    /// <summary>物模型 Tab 设备下拉集合。</summary>
    public ObservableCollection<DevicePickerItem> DevicePicker { get; } = [];

    /// <summary>当前设备的物模型变量列表。</summary>
    public ObservableCollection<VariableListItem> Variables { get; } = [];

    /// <summary>物模型 Tab 当前选中的设备 Id。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHttpVariableFields))]
    private long? _selectedVariableDeviceId;

    /// <summary>当前选中的物模型变量列表项。</summary>
    [ObservableProperty]
    private VariableListItem? _selectedVariable;

    /// <summary>是否处于物模型变量编辑面板。</summary>
    [ObservableProperty]
    private bool _isEditingVariable;

    /// <summary>编辑中的变量 Id（新建时为 0）。</summary>
    [ObservableProperty]
    private long _varId;

    /// <summary>编辑中的变量地址。</summary>
    [ObservableProperty]
    private string _varAddress = string.Empty;

    /// <summary>编辑中的数据类型索引。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoolVariableDataType))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageOperationSelector))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageWriteValue))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageBoolHint))]
    private int _varDataType;

    /// <summary>编辑中的变量别名。</summary>
    [ObservableProperty]
    private string _varAlias = string.Empty;

    /// <summary>编辑中的变量描述。</summary>
    [ObservableProperty]
    private string _varDescription = string.Empty;

    /// <summary>编辑中的读写权限索引。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditIncludeInPoll))]
    [NotifyPropertyChangedFor(nameof(CanEditDerivedCollectionFlags))]
    private int _varReadWrite = (int)ReadWriteAccess.ReadWrite;

    /// <summary>编辑中是否参与后台轮询。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDerivedCollectionFlags))]
    private bool _varIncludeInPoll = true;

    /// <summary>编辑中是否在手动操作页状态区展示。</summary>
    [ObservableProperty]
    private bool _varShowInStatus = true;

    /// <summary>编辑中是否纳入 MQTT 周期遥测。</summary>
    [ObservableProperty]
    private bool _varIncludeInTelemetry = true;

    /// <summary>WriteOnly 时不可参与轮询。</summary>
    public bool CanEditIncludeInPoll => VarReadWrite != (int)ReadWriteAccess.WriteOnly;

    /// <summary>未参与轮询时不可单独勾选展示/上报。</summary>
    public bool CanEditDerivedCollectionFlags => VarIncludeInPoll;

    /// <summary>编辑中的 HTTP Key JSON 路径。</summary>
    [ObservableProperty]
    private string _varHttpKeyPath = string.Empty;

    /// <summary>编辑中的 HTTP Value JSON 路径。</summary>
    [ObservableProperty]
    private string _varHttpValuePath = string.Empty;

    /// <summary>编辑中是否在自定义页展示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageOptions))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageOperationSelector))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageWriteValue))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageBoolHint))]
    private bool _varShowOnDefinedPage;

    /// <summary>编辑中的自定义页显示名称。</summary>
    [ObservableProperty]
    private string _varDefinedPageDisplayName = string.Empty;

    /// <summary>编辑中的自定义页操作（读取/写入）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageWriteValue))]
    private int _varDefinedPageOperation;

    /// <summary>编辑中的自定义页默认写入值。</summary>
    [ObservableProperty]
    private string _varDefinedPageWriteValue = string.Empty;

    /// <summary>当前变量编辑是否为新建模式。</summary>
    private bool _isNewVariable;

    /// <summary>是否显示自定义页相关编辑字段。</summary>
    public bool ShowDefinedPageOptions => VarShowOnDefinedPage;

    /// <summary>当前变量是否为布尔/线圈/离散类型。</summary>
    public bool IsBoolVariableDataType
        => (DataType)VarDataType is DataType.Bool or DataType.Coil or DataType.Discrete;

    /// <summary>是否显示自定义页操作选择器（非布尔且已勾选进入自定义页）。</summary>
    public bool ShowDefinedPageOperationSelector
        => VarShowOnDefinedPage && !IsBoolVariableDataType;

    /// <summary>是否显示自定义页默认写入值（写入模式且非布尔）。</summary>
    public bool ShowDefinedPageWriteValue
        => VarShowOnDefinedPage
           && !IsBoolVariableDataType
           && VarDefinedPageOperation == (int)DefinedPageOperation.Write;

    /// <summary>是否显示布尔变量自定义页点动提示。</summary>
    public bool ShowDefinedPageBoolHint
        => VarShowOnDefinedPage && IsBoolVariableDataType;

    /// <summary>是否显示 HTTP 物模型字段（当前选中设备为 HTTP 协议时）。</summary>
    public bool ShowHttpVariableFields
    {
        get
        {
            if (SelectedVariableDeviceId is null)
                return false;
            var item = DevicePicker.FirstOrDefault(x => x.Id == SelectedVariableDeviceId.Value);
            return item is not null && ProtocolDisplay.IsHttp(item.ProtocolType);
        }
    }

    /// <summary>
    /// 物模型 Tab 选中设备变更：刷新 HTTP 字段可见性并加载变量列表。
    /// </summary>
    /// <param name="value">新选中的设备 Id；null 表示未选。</param>
    partial void OnSelectedVariableDeviceIdChanged(long? value)
    {
        OnPropertyChanged(nameof(ShowHttpVariableFields));
        IsEditingVariable = false;
        _ = RefreshVariablesAsync();
    }

    /// <summary>
    /// 加载当前选中设备的物模型变量列表。
    /// </summary>
    [RelayCommand]
    private async Task RefreshVariablesAsync()
    {
        if (SelectedVariableDeviceId is null or <= 0)
        {
            Variables.Clear();
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var list = await _service.ListVariablesAsync(SelectedVariableDeviceId.Value);
            Variables.Clear();
            foreach (var v in list)
                Variables.Add(ToVariableItem(v));
            StatusMessage = $"已加载 {Variables.Count} 个物模型变量";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载物模型失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 进入新建物模型变量编辑面板。
    /// </summary>
    [RelayCommand]
    private void NewVariable()
    {
        if (SelectedVariableDeviceId is null or <= 0)
        {
            StatusMessage = "请先选择设备";
            return;
        }

        _isNewVariable = true;
        VarId = 0;
        VarAddress = string.Empty;
        VarDataType = 0;
        VarAlias = string.Empty;
        VarDescription = string.Empty;
        VarReadWrite = (int)ReadWriteAccess.ReadWrite;
        VarIncludeInPoll = true;
        VarShowInStatus = true;
        VarIncludeInTelemetry = true;
        VarHttpKeyPath = string.Empty;
        VarHttpValuePath = string.Empty;
        VarShowOnDefinedPage = false;
        VarDefinedPageDisplayName = string.Empty;
        VarDefinedPageOperation = (int)DefinedPageOperation.Read;
        VarDefinedPageWriteValue = string.Empty;
        IsEditingVariable = true;
        StatusMessage = "新建物模型变量";
    }

    /// <summary>
    /// 将当前选中变量载入编辑面板。
    /// </summary>
    [RelayCommand]
    private void EditVariable()
    {
        if (SelectedVariable is null)
        {
            StatusMessage = "请先选择变量";
            return;
        }

        var v = SelectedVariable.Source;
        _isNewVariable = false;
        VarId = v.Id;
        VarAddress = v.Address;
        VarDataType = (int)v.DataType;
        VarAlias = v.Alias;
        VarDescription = v.Description;
        VarReadWrite = (int)v.ReadWrite;
        VarIncludeInPoll = v.IncludeInPoll;
        VarShowInStatus = v.ShowInStatus;
        VarIncludeInTelemetry = v.IncludeInTelemetry;
        VarHttpKeyPath = v.HttpKeyJsonPath;
        VarHttpValuePath = v.HttpValueJsonPath;
        VarShowOnDefinedPage = v.ShowOnDefinedPage;
        VarDefinedPageDisplayName = v.DefinedPageDisplayName;
        VarDefinedPageOperation = (int)v.DefinedPageOperation;
        VarDefinedPageWriteValue = v.DefinedPageWriteValue;
        IsEditingVariable = true;
        StatusMessage = $"编辑变量 #{v.Id}";
    }

    /// <summary>
    /// 校验并保存物模型变量（新建或更新），成功后刷新列表。
    /// </summary>
    [RelayCommand]
    private async Task SaveVariableAsync()
    {
        if (SelectedVariableDeviceId is null or <= 0)
        {
            StatusMessage = "请先选择设备";
            return;
        }

        if (string.IsNullOrWhiteSpace(VarAddress) && !ShowHttpVariableFields)
        {
            StatusMessage = "变量地址不能为空";
            return;
        }

        if (VarShowOnDefinedPage && string.IsNullOrWhiteSpace(VarDefinedPageDisplayName))
        {
            StatusMessage = "开启手动操作时请填写「操作名称」";
            return;
        }

        if (VarIncludeInPoll
            && !ShowHttpVariableFields
            && string.IsNullOrWhiteSpace(VarAlias))
        {
            StatusMessage = "参与轮询时请填写「别名」";
            return;
        }

        var isBool = (DataType)VarDataType is DataType.Bool or DataType.Coil or DataType.Discrete;
        if (VarShowOnDefinedPage
            && !isBool
            && VarDefinedPageOperation == (int)DefinedPageOperation.Write
            && string.IsNullOrWhiteSpace(VarDefinedPageWriteValue))
        {
            StatusMessage = "手动操作写入模式请填写「默认写入值」";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var variable = new DeviceVariable
            {
                Id = VarId,
                DeviceId = SelectedVariableDeviceId.Value,
                Address = VarAddress?.Trim() ?? string.Empty,
                DataType = (DataType)VarDataType,
                Alias = VarAlias?.Trim() ?? string.Empty,
                Description = VarDescription?.Trim() ?? string.Empty,
                ReadWrite = (ReadWriteAccess)VarReadWrite,
                IncludeInPoll = VarIncludeInPoll,
                ShowInStatus = VarShowInStatus,
                IncludeInTelemetry = VarIncludeInTelemetry,
                HttpKeyJsonPath = VarHttpKeyPath?.Trim() ?? string.Empty,
                HttpValueJsonPath = VarHttpValuePath?.Trim() ?? string.Empty,
                ShowOnDefinedPage = VarShowOnDefinedPage,
                // 未勾选自定义页时清空相关字段
                DefinedPageDisplayName = VarShowOnDefinedPage
                    ? (VarDefinedPageDisplayName?.Trim() ?? string.Empty)
                    : string.Empty,
                DefinedPageOperation = VarShowOnDefinedPage && !isBool
                    ? (DefinedPageOperation)VarDefinedPageOperation
                    : DefinedPageOperation.Read,
                DefinedPageWriteValue = VarShowOnDefinedPage
                                        && !isBool
                                        && VarDefinedPageOperation == (int)DefinedPageOperation.Write
                    ? (VarDefinedPageWriteValue?.Trim() ?? string.Empty)
                    : string.Empty,
            };

            if (_isNewVariable)
            {
                await _service.CreateVariableAsync(variable);
                StatusMessage = $"已创建变量 #{variable.Id}";
            }
            else
            {
                await _service.UpdateVariableAsync(variable);
                StatusMessage = $"已更新变量 #{variable.Id}";
            }

            IsEditingVariable = false;
            await RefreshVariablesInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "保存变量失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 删除当前选中物模型变量并刷新列表。
    /// </summary>
    [RelayCommand]
    private async Task DeleteVariableAsync()
    {
        if (SelectedVariable is null)
        {
            StatusMessage = "请先选择变量";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            var id = SelectedVariable.Id;
            await _service.DeleteVariableAsync(id);
            IsEditingVariable = false;
            SelectedVariable = null;
            StatusMessage = $"已删除变量 #{id}";
            await RefreshVariablesInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "删除变量失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 取消物模型变量编辑并关闭编辑面板。
    /// </summary>
    [RelayCommand]
    private void CancelVariableEdit()
    {
        IsEditingVariable = false;
        StatusMessage = "已取消变量编辑";
    }

    // ── MQTT tab ──────────────────────────────────────────────────────

    /// <summary>MQTT 是否启用（连接与上报）。</summary>
    [ObservableProperty]
    private bool _mqttIsEnabled = true;

    /// <summary>是否启用本地轮询采集。</summary>
    [ObservableProperty]
    private bool _mqttEnablePolling = true;

    /// <summary>MQTT Broker IP。</summary>
    [ObservableProperty]
    private string _mqttBrokerIp = string.Empty;

    /// <summary>MQTT 端口。</summary>
    [ObservableProperty]
    private int _mqttPort = 1883;

    /// <summary>MQTT Client Id。</summary>
    [ObservableProperty]
    private string _mqttClientId = string.Empty;

    /// <summary>MQTT 用户名。</summary>
    [ObservableProperty]
    private string _mqttUsername = string.Empty;

    /// <summary>MQTT 密码。</summary>
    [ObservableProperty]
    private string _mqttPassword = string.Empty;

    /// <summary>MQTT 发布主题（遥测上报）。</summary>
    [ObservableProperty]
    private string _mqttPubTopic = string.Empty;

    /// <summary>MQTT 订阅主题（指令下发）。</summary>
    [ObservableProperty]
    private string _mqttSubTopic = string.Empty;

    /// <summary>MQTT 在线状态主题。</summary>
    [ObservableProperty]
    private string _mqttOnlineStatusTopic = string.Empty;

    /// <summary>在线状态上报间隔（毫秒）。</summary>
    [ObservableProperty]
    private int _mqttOnlineStatusReportInterval = 30000;

    /// <summary>遥测周期上报间隔（毫秒）；0 表示不限制/由轮询驱动。</summary>
    [ObservableProperty]
    private int _mqttTelemetryPublishInterval;

    /// <summary>
    /// 从服务加载 MQTT 配置到编辑表单。
    /// </summary>
    [RelayCommand]
    private async Task LoadMqttAsync()
    {
        try
        {
            var mqtt = await _service.GetMqttAsync();
            if (mqtt is null)
            {
                // 尚未配置：填充默认值
                MqttIsEnabled = true;
                MqttEnablePolling = true;
                MqttBrokerIp = string.Empty;
                MqttPort = 1883;
                MqttClientId = string.Empty;
                MqttUsername = string.Empty;
                MqttPassword = string.Empty;
                MqttPubTopic = string.Empty;
                MqttSubTopic = string.Empty;
                MqttOnlineStatusTopic = string.Empty;
                MqttOnlineStatusReportInterval = 30000;
                MqttTelemetryPublishInterval = 0;
                StatusMessage = "尚未配置 MQTT，可填写后保存";
                return;
            }

            // 已有配置：映射到表单
            MqttIsEnabled = mqtt.IsEnabled;
            MqttEnablePolling = mqtt.EnablePolling;
            MqttBrokerIp = mqtt.BrokerIp;
            MqttPort = mqtt.Port;
            MqttClientId = mqtt.ClientId;
            MqttUsername = mqtt.Username;
            MqttPassword = mqtt.Password;
            MqttPubTopic = mqtt.PubTopic;
            MqttSubTopic = mqtt.SubTopic;
            MqttOnlineStatusTopic = mqtt.OnlineStatusTopic;
            MqttOnlineStatusReportInterval = mqtt.OnlineStatusReportInterval;
            MqttTelemetryPublishInterval = mqtt.TelemetryPublishInterval;
            StatusMessage = mqtt.IsEnabled
                ? mqtt.EnablePolling
                    ? "已加载 MQTT（已连接上报 + 轮询）"
                    : "已加载 MQTT（仅指令，不上报周期数据）"
                : mqtt.EnablePolling
                    ? "已加载 MQTT（轮询开，MQTT 关）"
                    : "已加载 MQTT（轮询与 MQTT 均已关闭）";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载 MQTT 失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 将当前表单 MQTT 配置保存到服务。
    /// </summary>
    [RelayCommand]
    private async Task SaveMqttAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await _service.SaveMqttAsync(new MqttConfig
            {
                IsEnabled = MqttIsEnabled,
                EnablePolling = MqttEnablePolling,
                BrokerIp = MqttBrokerIp?.Trim() ?? string.Empty,
                Port = MqttPort,
                ClientId = MqttClientId?.Trim() ?? string.Empty,
                Username = MqttUsername?.Trim() ?? string.Empty,
                Password = MqttPassword ?? string.Empty,
                PubTopic = MqttPubTopic?.Trim() ?? string.Empty,
                SubTopic = MqttSubTopic?.Trim() ?? string.Empty,
                OnlineStatusTopic = MqttOnlineStatusTopic?.Trim() ?? string.Empty,
                OnlineStatusReportInterval = MqttOnlineStatusReportInterval,
                // 负值归 0，避免无效间隔
                TelemetryPublishInterval = MqttTelemetryPublishInterval < 0 ? 0 : MqttTelemetryPublishInterval,
            });
            StatusMessage = MqttEnablePolling
                ? MqttIsEnabled
                    ? "已保存：轮询开，MQTT 开（周期上报受遥测间隔限制）"
                    : "已保存：轮询开，MQTT 关（仅本地快照）"
                : MqttIsEnabled
                    ? "已保存：轮询关，MQTT 开（可收指令写入，不上报周期数据）"
                    : "已保存：轮询与 MQTT 均已关闭";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存 MQTT 失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Debug tab ─────────────────────────────────────────────────────

    /// <summary>调试 Tab 当前设备的物模型变量下拉集合。</summary>
    public ObservableCollection<DebugVariableItem> DebugVariables { get; } = [];

    /// <summary>调试 Tab 当前选中的设备 Id。</summary>
    [ObservableProperty]
    private long? _debugDeviceId;

    /// <summary>调试 Tab 当前选中的物模型变量。</summary>
    [ObservableProperty]
    private DebugVariableItem? _selectedDebugVariable;

    /// <summary>调试读写的目标地址（可手改或由选中变量填充）。</summary>
    [ObservableProperty]
    private string _debugAddress = string.Empty;

    /// <summary>调试读写的数据类型索引。</summary>
    [ObservableProperty]
    private int _debugDataType;

    /// <summary>调试写入的目标值。</summary>
    [ObservableProperty]
    private string _debugWriteValue = string.Empty;

    /// <summary>调试读/写/批量读的结果输出文本。</summary>
    [ObservableProperty]
    private string _debugOutput = string.Empty;

    /// <summary>
    /// 调试设备变更时重新加载该设备的物模型列表。
    /// </summary>
    /// <param name="value">新选中的设备 Id。</param>
    partial void OnDebugDeviceIdChanged(long? value)
    {
        _ = LoadDebugVariablesAsync(value);
    }

    /// <summary>
    /// 选中调试变量时同步地址与数据类型到编辑区。
    /// </summary>
    /// <param name="value">新选中的调试变量；null 时清空地址。</param>
    partial void OnSelectedDebugVariableChanged(DebugVariableItem? value)
    {
        if (value is null)
        {
            DebugAddress = string.Empty;
            return;
        }

        DebugAddress = value.Address;
        DebugDataType = (int)value.DataType;
    }

    /// <summary>
    /// 加载指定设备的物模型变量供调试下拉使用。
    /// </summary>
    /// <param name="deviceId">设备 Id；null 或无效时清空列表。</param>
    private async Task LoadDebugVariablesAsync(long? deviceId)
    {
        SelectedDebugVariable = null;
        DebugVariables.Clear();
        DebugAddress = string.Empty;
        DebugDataType = 0;

        if (deviceId is null or <= 0)
            return;

        try
        {
            var vars = await _service.ListVariablesAsync(deviceId.Value);
            foreach (var v in vars.OrderBy(x => x.Alias).ThenBy(x => x.Address).ThenBy(x => x.Id))
            {
                var label = string.IsNullOrWhiteSpace(v.Alias) ? "(未命名)" : v.Alias;
                // HTTP 变量无 Address 时用 Key/Value 路径拼接
                var addressPart = string.IsNullOrWhiteSpace(v.Address)
                    ? $"{v.HttpKeyJsonPath}/{v.HttpValueJsonPath}".Trim('/')
                    : v.Address;
                if (string.IsNullOrWhiteSpace(addressPart))
                    addressPart = "-";

                var desc = string.IsNullOrWhiteSpace(v.Description) ? string.Empty : $" - {v.Description}";
                DebugVariables.Add(new DebugVariableItem
                {
                    Id = v.Id,
                    Address = v.Address,
                    Alias = v.Alias,
                    DataType = v.DataType,
                    DisplayName = $"{label} ({addressPart}){desc}",
                    Source = v,
                });
            }

            if (DebugVariables.Count == 0)
                StatusMessage = "该设备暂无物模型，请先在「物模型」中配置";
            else
                // 默认选中第一项便于快速调试
                SelectedDebugVariable = DebugVariables[0];
        }
        catch (Exception ex)
        {
            StatusMessage = "加载调试物模型失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 对当前调试地址执行单次读取（经 <see cref="IDeviceDebugApi"/>）。
    /// </summary>
    [RelayCommand]
    private async Task DebugReadAsync()
    {
        if (DebugDeviceId is null or <= 0)
        {
            StatusMessage = "请选择调试设备";
            return;
        }

        if (SelectedDebugVariable is null || string.IsNullOrWhiteSpace(DebugAddress))
        {
            StatusMessage = "请选择物模型属性";
            return;
        }

        try
        {
            IsBusy = true;
            // 经 DebugApi 读取，带来源标记便于历史记录
            var result = await _debugApi.ReadAsync(
                DebugDeviceId.Value,
                DebugAddress.Trim(),
                (DataType)DebugDataType,
                new DeviceDebugContext { Source = "DevicesDebug" });

            DebugOutput = result.Success
                ? $"读取成功\n物模型: {SelectedDebugVariable.Alias}\n地址: {DebugAddress}\n类型: {ProtocolDisplay.DataTypeNames[DebugDataType]}\n值: {FormatValue(result.Value)}"
                : $"读取失败\n{result.Error}";
            StatusMessage = result.Success ? "调试读取完成" : "调试读取失败";
        }
        catch (Exception ex)
        {
            DebugOutput = "读取异常：" + ex.Message;
            StatusMessage = "调试读取异常";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 对当前调试地址执行单次写入（经 <see cref="IDeviceDebugApi"/>）。
    /// </summary>
    [RelayCommand]
    private async Task DebugWriteAsync()
    {
        if (DebugDeviceId is null or <= 0)
        {
            StatusMessage = "请选择调试设备";
            return;
        }

        if (SelectedDebugVariable is null || string.IsNullOrWhiteSpace(DebugAddress))
        {
            StatusMessage = "请选择物模型属性";
            return;
        }

        try
        {
            IsBusy = true;
            // 经 DebugApi 写入，带来源标记便于历史记录
            var result = await _debugApi.WriteAsync(
                DebugDeviceId.Value,
                DebugAddress.Trim(),
                (DataType)DebugDataType,
                DebugWriteValue,
                new DeviceDebugContext { Source = "DevicesDebug" });

            DebugOutput = result.Success
                ? $"写入成功\n物模型: {SelectedDebugVariable.Alias}\n地址: {DebugAddress}\n类型: {ProtocolDisplay.DataTypeNames[DebugDataType]}\n值: {DebugWriteValue}"
                : $"写入失败\n{result.Error}";
            StatusMessage = result.Success ? "调试写入完成" : "调试写入失败";
        }
        catch (Exception ex)
        {
            DebugOutput = "写入异常：" + ex.Message;
            StatusMessage = "调试写入异常";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 对当前调试设备执行批量读取全部物模型（经 <see cref="IDeviceDebugApi"/>）。
    /// </summary>
    [RelayCommand]
    private async Task DebugReadAllAsync()
    {
        if (DebugDeviceId is null or <= 0)
        {
            StatusMessage = "请选择调试设备";
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _debugApi.ReadAllAsync(
                DebugDeviceId.Value,
                new DeviceDebugContext { Source = "DevicesDebug" });
            if (!result.Success)
            {
                DebugOutput = "批量读取失败\n" + (result.Error ?? string.Empty);
                StatusMessage = "调试批量读取失败";
                return;
            }

            // 逐项拼接成功/失败明细
            var sb = new StringBuilder();
            sb.AppendLine($"批量读取完成，共 {result.Items.Count} 项");
            sb.AppendLine(new string('-', 40));
            foreach (var item in result.Items)
            {
                if (item.Success)
                    sb.AppendLine($"[{item.Alias}] {item.Address} ({item.DataType}) = {FormatValue(item.Value)}");
                else
                    sb.AppendLine($"[{item.Alias}] {item.Address} 失败: {item.Error}");
            }

            DebugOutput = sb.ToString();
            StatusMessage = "调试批量读取完成";
        }
        catch (Exception ex)
        {
            DebugOutput = "批量读取异常：" + ex.Message;
            StatusMessage = "调试批量读取异常";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Backup tab ────────────────────────────────────────────────────

    /// <summary>导出的备份 JSON 文本（只读展示）。</summary>
    [ObservableProperty]
    private string _exportJson = string.Empty;

    /// <summary>待导入的备份 JSON 文本（用户粘贴）。</summary>
    [ObservableProperty]
    private string _importJson = string.Empty;

    /// <summary>导出/导入时是否包含 MQTT 配置。</summary>
    [ObservableProperty]
    private bool _includeMqtt = true;

    /// <summary>导入模式（merge / replaceAll）。</summary>
    [ObservableProperty]
    private string _importMode = "merge";

    /// <summary>
    /// 导出设备与物模型备份 JSON。
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ExportJson = await _service.ExportBackupAsync(IncludeMqtt);
            StatusMessage = "备份导出成功";
        }
        catch (Exception ex)
        {
            StatusMessage = "导出失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 导入备份 JSON 并按选定模式合并或全量替换。
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportJson))
        {
            StatusMessage = "请粘贴要导入的备份 JSON";
            return;
        }

        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            await _service.ImportBackupAsync(ImportJson, ImportMode, IncludeMqtt);
            StatusMessage = $"备份导入成功（模式: {ImportMode}）";
            // 导入后刷新设备列表与下拉
            await RefreshDevicesInternalAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = "导入失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// 静默刷新设备列表与设备下拉（保存/删除/导入后调用，不置 IsBusy）。
    /// </summary>
    private async Task RefreshDevicesInternalAsync()
    {
        var name = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var devices = await _service.ListDevicesAsync(name);
        DeviceListItems.Clear();
        foreach (var d in devices.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            DeviceListItems.Add(ToListItem(d));
        RebuildDevicePicker(devices);
    }

    /// <summary>
    /// 静默刷新当前设备的物模型列表（保存/删除变量后调用）。
    /// </summary>
    private async Task RefreshVariablesInternalAsync()
    {
        if (SelectedVariableDeviceId is null or <= 0)
            return;

        var list = await _service.ListVariablesAsync(SelectedVariableDeviceId.Value);
        Variables.Clear();
        foreach (var v in list)
            Variables.Add(ToVariableItem(v));
    }

    /// <summary>
    /// 物模型 Tab 首次进入时懒加载设备下拉（已有数据则跳过）。
    /// </summary>
    private async Task EnsureDevicePickerAsync()
    {
        if (DevicePicker.Count > 0)
            return;

        try
        {
            var devices = await _service.ListDevicesAsync();
            RebuildDevicePicker(devices);
        }
        catch (Exception ex)
        {
            StatusMessage = "加载设备列表失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 调试 Tab 进入时刷新设备下拉，并在已选设备时重载物模型。
    /// </summary>
    private async Task RefreshDebugDeviceContextAsync()
    {
        try
        {
            var devices = await _service.ListDevicesAsync();
            RebuildDevicePicker(devices);
            if (DebugDeviceId is long id && id > 0)
                await LoadDebugVariablesAsync(id);
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新调试设备失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 用最新设备列表重建下拉，并尽量保留物模型/调试 Tab 的选中项。
    /// </summary>
    /// <param name="devices">设备实体枚举。</param>
    private void RebuildDevicePicker(IEnumerable<Device> devices)
    {
        var selectedVar = SelectedVariableDeviceId;
        var selectedDebug = DebugDeviceId;
        DevicePicker.Clear();
        foreach (var d in devices.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            DevicePicker.Add(new DevicePickerItem
            {
                Id = d.Id,
                DisplayName = $"#{d.Id} {d.Name} ({ProtocolDisplay.GetProtocolName(d.ProtocolType)})",
                ProtocolType = d.ProtocolType,
            });
        }

        // 选中项仍存在则恢复，避免刷新后丢失上下文
        if (selectedVar is not null && DevicePicker.Any(x => x.Id == selectedVar))
            SelectedVariableDeviceId = selectedVar;
        if (selectedDebug is not null && DevicePicker.Any(x => x.Id == selectedDebug))
            DebugDeviceId = selectedDebug;

        OnPropertyChanged(nameof(ShowHttpVariableFields));
    }

    /// <summary>
    /// 根据现有设备 Id 推算下一个建议 Id（max + 1）。
    /// </summary>
    /// <returns>建议的新设备 Id。</returns>
    private long NextSuggestedDeviceId()
    {
        if (DeviceListItems.Count == 0)
            return 1;
        return DeviceListItems.Max(x => x.Id) + 1;
    }

    /// <summary>
    /// 从设备编辑表单字段组装 <see cref="Device"/> 实体。
    /// </summary>
    /// <returns>待持久化的设备实体。</returns>
    private Device BuildDeviceFromEditor()
    {
        return new Device
        {
            Id = EditId,
            Name = EditName.Trim(),
            Ip = EditIp?.Trim() ?? string.Empty,
            Port = EditPort,
            ProtocolType = (ProtocolType)EditProtocol,
            PortName = EditPortName?.Trim() ?? string.Empty,
            BaudRate = EditBaudRate,
            DataBits = EditDataBits,
            StopBits = EditStopBits,
            Parity = EditParity,
            PlcVersion = EditPlcVersion?.Trim() ?? string.Empty,
            PluginConfigJson = BuildHttpConfigJson(),
            SortOrder = EditSortOrder,
            // 关闭采集时 PollInterval 写 0；开启时最低 100ms，不足则回退 1000
            PollInterval = EditCollectionEnabled
                ? (EditPollInterval < 100 ? 1000 : EditPollInterval)
                : 0,
            IsActive = EditIsActive,
        };
    }

    /// <summary>
    /// 从 HTTP 编辑字段组装插件配置 JSON；非 HTTP 协议返回空串。
    /// </summary>
    /// <returns>序列化后的 <see cref="HttpClientConfigDto"/> JSON。</returns>
    private string BuildHttpConfigJson()
    {
        if (!ShowHttpFields)
            return string.Empty;

        var headers = new List<HttpHeaderItem>();
        if (!string.IsNullOrWhiteSpace(HcQueryHeaderKeys))
        {
            foreach (var key in HcQueryHeaderKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(key))
                    headers.Add(new HttpHeaderItem { Key = key });
            }
        }

        var config = new HttpClientConfigDto
        {
            LoginUrl = HcLoginUrl?.Trim() ?? string.Empty,
            LoginMethod = string.IsNullOrWhiteSpace(HcLoginMethod) ? "POST" : HcLoginMethod.Trim(),
            LoginBody = HcLoginBody ?? string.Empty,
            TokenJsonPath = string.IsNullOrWhiteSpace(HcTokenPath) ? "data.token" : HcTokenPath.Trim(),
            QueryUrl = HcQueryUrl?.Trim() ?? string.Empty,
            QueryMethod = string.IsNullOrWhiteSpace(HcQueryMethod) ? "GET" : HcQueryMethod.Trim(),
            QueryBody = HcQueryBody ?? string.Empty,
            QueryHeaders = headers,
            ResponseBodyJsonPath = string.IsNullOrWhiteSpace(HcBodyPath) ? "data" : HcBodyPath.Trim(),
            NestedItemsJsonPath = HcNestedPath?.Trim() ?? string.Empty,
        };

        return JsonSerializer.Serialize(config, HttpConfigJsonOptions);
    }

    /// <summary>
    /// 从设备 <see cref="Device.PluginConfigJson"/> 反序列化并填充 HTTP 编辑字段。
    /// </summary>
    /// <param name="json">插件配置 JSON；空或无效时保持默认空字段。</param>
    private void LoadHttpFields(string? json)
    {
        ClearHttpFields();
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var config = JsonSerializer.Deserialize<HttpClientConfigDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (config is null)
                return;

            HcLoginUrl = config.LoginUrl ?? string.Empty;
            HcLoginMethod = string.IsNullOrWhiteSpace(config.LoginMethod) ? "POST" : config.LoginMethod;
            HcLoginBody = config.LoginBody ?? string.Empty;
            HcTokenPath = string.IsNullOrWhiteSpace(config.TokenJsonPath) ? "data.token" : config.TokenJsonPath;
            HcQueryUrl = config.QueryUrl ?? string.Empty;
            HcQueryMethod = string.IsNullOrWhiteSpace(config.QueryMethod) ? "GET" : config.QueryMethod;
            HcQueryBody = config.QueryBody ?? string.Empty;
            HcQueryHeaderKeys = config.QueryHeaders is { Count: > 0 }
                ? string.Join(",", config.QueryHeaders.Select(h => h.Key).Where(k => !string.IsNullOrWhiteSpace(k)))
                : string.Empty;
            HcBodyPath = string.IsNullOrWhiteSpace(config.ResponseBodyJsonPath) ? "data" : config.ResponseBodyJsonPath;
            HcNestedPath = config.NestedItemsJsonPath ?? string.Empty;
        }
        catch
        {
            // JSON 无效时保留已清空的默认值
        }
    }

    /// <summary>
    /// 将 HTTP 编辑字段重置为默认空值。
    /// </summary>
    private void ClearHttpFields()
    {
        HcLoginUrl = string.Empty;
        HcLoginMethod = "POST";
        HcLoginBody = string.Empty;
        HcTokenPath = "data.token";
        HcQueryUrl = string.Empty;
        HcQueryMethod = "GET";
        HcQueryBody = string.Empty;
        HcQueryHeaderKeys = string.Empty;
        HcBodyPath = "data";
        HcNestedPath = string.Empty;
    }

    /// <summary>
    /// 将 <see cref="Device"/> 映射为列表行视图模型。
    /// </summary>
    /// <param name="d">设备实体。</param>
    /// <returns>列表绑定项。</returns>
    private static DeviceListItem ToListItem(Device d)
    {
        var endpoint = ProtocolDisplay.IsSerial(d.ProtocolType)
            ? $"{d.PortName} @ {d.BaudRate}"
            : ProtocolDisplay.IsHttp(d.ProtocolType)
                ? "HTTP"
                : $"{d.Ip}:{d.Port}";

        return new DeviceListItem
        {
            Id = d.Id,
            Name = d.Name,
            ProtocolName = ProtocolDisplay.GetProtocolName(d.ProtocolType),
            Endpoint = endpoint,
            PollInterval = d.PollInterval,
            CollectionText = d.PollInterval <= 0 ? "采集关闭" : $"轮询 {d.PollInterval} ms",
            IsActive = d.IsActive,
            SortOrder = d.SortOrder,
            Source = d,
        };
    }

    /// <summary>
    /// 将 <see cref="DeviceVariable"/> 映射为物模型列表行视图模型。
    /// </summary>
    /// <param name="v">物模型变量实体。</param>
    /// <returns>列表绑定项。</returns>
    private static VariableListItem ToVariableItem(DeviceVariable v)
    {
        var rw = (int)v.ReadWrite;
        var rwName = rw >= 0 && rw < ProtocolDisplay.ReadWriteNames.Length
            ? ProtocolDisplay.ReadWriteNames[rw]
            : v.ReadWrite.ToString();

        var definedPageModeText = !v.ShowOnDefinedPage
            ? string.Empty
            : FormatDefinedPageModeText(v);

        return new VariableListItem
        {
            Id = v.Id,
            Address = v.Address,
            DataTypeName = v.DataType.ToString(),
            Alias = v.Alias,
            Description = v.Description,
            ReadWriteName = rwName,
            ShowOnDefinedPage = v.ShowOnDefinedPage,
            DefinedPageModeText = definedPageModeText,
            DisplayLine = BuildVariableDisplayLine(v, rwName, definedPageModeText),
            Source = v,
        };
    }

    /// <summary>
    /// 拼接物模型列表单行展示文本。
    /// </summary>
    /// <param name="v">变量实体。</param>
    /// <param name="rwName">读写权限显示名。</param>
    /// <param name="definedPageModeText">自定义页模式摘要。</param>
    /// <returns>用「 · 」连接的单行摘要。</returns>
    private static string BuildVariableDisplayLine(DeviceVariable v, string rwName, string definedPageModeText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(v.Alias))
            parts.Add(v.Alias.Trim());
        if (!string.IsNullOrWhiteSpace(v.Address))
            parts.Add(v.Address.Trim());
        parts.Add(v.DataType.ToString());
        parts.Add(rwName);
        if (!string.IsNullOrWhiteSpace(v.Description))
            parts.Add(v.Description.Trim());
        if (v.ShowOnDefinedPage && !string.IsNullOrWhiteSpace(definedPageModeText))
            parts.Add($"手动·{definedPageModeText}");
        parts.Add(FormatCollectionFlagsSummary(v));
        return string.Join(" · ", parts);
    }

    /// <summary>采集/展示/上报开关摘要。</summary>
    private static string FormatCollectionFlagsSummary(DeviceVariable v)
    {
        if (!v.IncludeInPoll && !v.ShowInStatus && !v.IncludeInTelemetry)
            return "未采集";

        var flags = new List<string>(3);
        if (v.IncludeInPoll) flags.Add("采");
        if (v.ShowInStatus) flags.Add("展");
        if (v.IncludeInTelemetry) flags.Add("报");
        return string.Concat(flags);
    }

    partial void OnVarIncludeInPollChanged(bool value)
    {
        if (!value)
        {
            VarShowInStatus = false;
            VarIncludeInTelemetry = false;
        }

        OnPropertyChanged(nameof(CanEditDerivedCollectionFlags));
    }

    partial void OnVarReadWriteChanged(int value)
    {
        if (value == (int)ReadWriteAccess.WriteOnly)
            VarIncludeInPoll = false;

        OnPropertyChanged(nameof(CanEditIncludeInPoll));
    }

    partial void OnVarShowInStatusChanged(bool value)
    {
        if (value)
            VarIncludeInPoll = true;
    }

    partial void OnVarIncludeInTelemetryChanged(bool value)
    {
        if (value)
            VarIncludeInPoll = true;
    }

    /// <summary>
    /// 格式化自定义页模式摘要（读取/写入/点动）。
    /// </summary>
    /// <param name="v">变量实体。</param>
    /// <returns>自定义页模式文案。</returns>
    private static string FormatDefinedPageModeText(DeviceVariable v)
    {
        var name = string.IsNullOrWhiteSpace(v.DefinedPageDisplayName)
            ? v.Alias
            : v.DefinedPageDisplayName;
        if (v.DataType is DataType.Bool or DataType.Coil or DataType.Discrete)
            return $"{name}·点动";
        return v.DefinedPageOperation == DefinedPageOperation.Write
            ? $"{name}·写入={v.DefinedPageWriteValue}"
            : $"{name}·读取";
    }

    /// <summary>
    /// 将调试读取返回值格式化为可读字符串。
    /// </summary>
    /// <param name="value">原始值；可为 null。</param>
    /// <returns>格式化后的文本。</returns>
    private static string FormatValue(object? value)
        => value switch
        {
            null => "(null)",
            string s => s,
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>HTTP 请求头项（仅 Key，Value 由运行时填充）。</summary>
    private sealed class HttpHeaderItem
    {
        /// <summary>请求头名称。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>请求头值。</summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>HTTP 客户端插件配置 DTO（与 PluginConfigJson 对应）。</summary>
    private sealed class HttpClientConfigDto
    {
        /// <summary>登录 URL。</summary>
        public string LoginUrl { get; set; } = string.Empty;

        /// <summary>登录 HTTP 方法。</summary>
        public string LoginMethod { get; set; } = "POST";

        /// <summary>登录请求体。</summary>
        public string LoginBody { get; set; } = string.Empty;

        /// <summary>Token JSON 路径。</summary>
        public string TokenJsonPath { get; set; } = "data.token";

        /// <summary>数据查询 URL。</summary>
        public string QueryUrl { get; set; } = string.Empty;

        /// <summary>查询 HTTP 方法。</summary>
        public string QueryMethod { get; set; } = "GET";

        /// <summary>查询请求体。</summary>
        public string QueryBody { get; set; } = string.Empty;

        /// <summary>查询附加请求头列表。</summary>
        public List<HttpHeaderItem> QueryHeaders { get; set; } = [];

        /// <summary>响应体 JSON 根路径。</summary>
        public string ResponseBodyJsonPath { get; set; } = "data";

        /// <summary>嵌套数组 JSON 路径。</summary>
        public string NestedItemsJsonPath { get; set; } = string.Empty;
    }
}

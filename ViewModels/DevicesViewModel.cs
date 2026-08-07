using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lana.Gateway.Models;
using Lana.Gateway.Services;

namespace Lana.ViewModels;

public sealed class DeviceListItem
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ProtocolName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public int PollInterval { get; init; }
    public string CollectionText { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public Device Source { get; init; } = null!;
}

public sealed class VariableListItem
{
    public long Id { get; init; }
    public string Address { get; init; } = string.Empty;
    public string DataTypeName { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ReadWriteName { get; init; } = string.Empty;
    public bool ShowOnDefinedPage { get; init; }
    public string DefinedPageModeText { get; init; } = string.Empty;
    public DeviceVariable Source { get; init; } = null!;
}

public sealed class DevicePickerItem
{
    public long Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ProtocolType ProtocolType { get; init; }
}

public sealed class DebugVariableItem
{
    public long Id { get; init; }
    public string Address { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public DataType DataType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public DeviceVariable Source { get; init; } = null!;
}

public partial class DevicesViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions HttpConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly GatewayDeviceService _service;
    private readonly IDeviceDebugApi _debugApi;
    private long _editingDeviceOldId;
    private bool _isNewDevice;

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
        _ = RefreshDevicesAsync();
    }

    // ── Tabs ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        switch (value)
        {
            case 1:
                _ = EnsureDevicePickerAsync();
                break;
            case 2:
                _ = LoadMqttAsync();
                break;
            case 3:
                _ = RefreshDebugDeviceContextAsync();
                break;
        }
    }

    // ── Shared ────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<string> ProtocolOptions { get; }
    public IReadOnlyList<string> DataTypeOptions { get; }
    public IReadOnlyList<string> ReadWriteOptions { get; }
    public IReadOnlyList<string> DefinedPageOperationOptions { get; }
    public IReadOnlyList<string> ImportModeOptions { get; }

    // ── Devices tab ───────────────────────────────────────────────────

    public ObservableCollection<DeviceListItem> DeviceListItems { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DeviceListItem? _selectedDevice;

    [ObservableProperty]
    private bool _isEditingDevice;

    [ObservableProperty]
    private long _editId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTcpFields))]
    [NotifyPropertyChangedFor(nameof(ShowSerialFields))]
    [NotifyPropertyChangedFor(nameof(ShowPlcFields))]
    [NotifyPropertyChangedFor(nameof(ShowHttpFields))]
    private int _editProtocol;

    [ObservableProperty]
    private string _editIp = string.Empty;

    [ObservableProperty]
    private int _editPort = 502;

    [ObservableProperty]
    private string _editPortName = "COM1";

    [ObservableProperty]
    private int _editBaudRate = 9600;

    [ObservableProperty]
    private int _editDataBits = 8;

    [ObservableProperty]
    private int _editStopBits = 1;

    [ObservableProperty]
    private int _editParity;

    [ObservableProperty]
    private string _editPlcVersion = string.Empty;

    [ObservableProperty]
    private int _editSortOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPollIntervalEditor))]
    private bool _editCollectionEnabled = true;

    [ObservableProperty]
    private int _editPollInterval = 1000;

    public bool ShowPollIntervalEditor => EditCollectionEnabled;

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string _hcLoginUrl = string.Empty;

    [ObservableProperty]
    private string _hcLoginMethod = "POST";

    [ObservableProperty]
    private string _hcLoginBody = string.Empty;

    [ObservableProperty]
    private string _hcTokenPath = "data.token";

    [ObservableProperty]
    private string _hcQueryUrl = string.Empty;

    [ObservableProperty]
    private string _hcQueryMethod = "GET";

    [ObservableProperty]
    private string _hcQueryBody = string.Empty;

    [ObservableProperty]
    private string _hcQueryHeaderKeys = string.Empty;

    [ObservableProperty]
    private string _hcBodyPath = "data";

    [ObservableProperty]
    private string _hcNestedPath = string.Empty;

    public ObservableCollection<string> PlcVersionOptions { get; } = [];

    public bool ShowTcpFields => ProtocolDisplay.IsTcp((ProtocolType)EditProtocol);
    public bool ShowSerialFields => ProtocolDisplay.IsSerial((ProtocolType)EditProtocol);
    public bool ShowPlcFields => ProtocolDisplay.NeedsPlcVersion((ProtocolType)EditProtocol);
    public bool ShowHttpFields => ProtocolDisplay.IsHttp((ProtocolType)EditProtocol);

    partial void OnEditProtocolChanged(int value) => UpdateFieldVisibility();

    private void UpdateFieldVisibility()
    {
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
            foreach (var v in ProtocolDisplay.SiemensVersions)
                PlcVersionOptions.Add(v);

            var siemens = IoTClientFactory.NormalizeSiemensVersion(desiredPlcVersion);
            EditPlcVersion = PlcVersionOptions.Contains(siemens)
                ? siemens
                : ProtocolDisplay.SiemensVersions[0];
        }
        else if (protocol == ProtocolType.MitsubishiClient)
        {
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

        EditPort = protocol switch
        {
            ProtocolType.ModbusTcp when EditPort is 0 or 102 => 502,
            ProtocolType.SiemensClient when EditPort is 0 or 502 => 102,
            ProtocolType.MitsubishiClient when EditPort is 0 or 502 or 102 => 6000,
            ProtocolType.OmronFinsClient when EditPort is 0 or 502 or 102 => 9600,
            _ => EditPort,
        };
    }

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

        // 先放入型号，再改协议（协议变更会重建下拉）；最后强制回写一次保证 ComboBox 选中
        EditPlcVersion = plcVersion;
        EditProtocol = (int)d.ProtocolType;
        UpdateFieldVisibility();
        ApplyPlcVersionSelection(plcVersion);

        IsEditingDevice = true;
        StatusMessage = $"编辑设备 #{d.Id}";
    }

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

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditingDevice = false;
        StatusMessage = "已取消编辑";
    }

    // ── Variables tab ─────────────────────────────────────────────────

    public ObservableCollection<DevicePickerItem> DevicePicker { get; } = [];

    public ObservableCollection<VariableListItem> Variables { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHttpVariableFields))]
    private long? _selectedVariableDeviceId;

    [ObservableProperty]
    private VariableListItem? _selectedVariable;

    [ObservableProperty]
    private bool _isEditingVariable;

    [ObservableProperty]
    private long _varId;

    [ObservableProperty]
    private string _varAddress = string.Empty;

    [ObservableProperty]
    private int _varDataType;

    [ObservableProperty]
    private string _varAlias = string.Empty;

    [ObservableProperty]
    private string _varDescription = string.Empty;

    [ObservableProperty]
    private int _varReadWrite = (int)ReadWriteAccess.ReadWrite;

    [ObservableProperty]
    private string _varHttpKeyPath = string.Empty;

    [ObservableProperty]
    private string _varHttpValuePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageOptions))]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageWriteValue))]
    private bool _varShowOnDefinedPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDefinedPageWriteValue))]
    private int _varDefinedPageOperation;

    [ObservableProperty]
    private string _varDefinedPageWriteValue = string.Empty;

    private bool _isNewVariable;

    public bool ShowDefinedPageOptions => VarShowOnDefinedPage;

    public bool ShowDefinedPageWriteValue
        => VarShowOnDefinedPage && VarDefinedPageOperation == (int)DefinedPageOperation.Write;

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

    partial void OnSelectedVariableDeviceIdChanged(long? value)
    {
        OnPropertyChanged(nameof(ShowHttpVariableFields));
        IsEditingVariable = false;
        _ = RefreshVariablesAsync();
    }

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
        VarHttpKeyPath = string.Empty;
        VarHttpValuePath = string.Empty;
        VarShowOnDefinedPage = false;
        VarDefinedPageOperation = (int)DefinedPageOperation.Read;
        VarDefinedPageWriteValue = string.Empty;
        IsEditingVariable = true;
        StatusMessage = "新建物模型变量";
    }

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
        VarHttpKeyPath = v.HttpKeyJsonPath;
        VarHttpValuePath = v.HttpValueJsonPath;
        VarShowOnDefinedPage = v.ShowOnDefinedPage;
        VarDefinedPageOperation = (int)v.DefinedPageOperation;
        VarDefinedPageWriteValue = v.DefinedPageWriteValue;
        IsEditingVariable = true;
        StatusMessage = $"编辑变量 #{v.Id}";
    }

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

        if (VarShowOnDefinedPage
            && VarDefinedPageOperation == (int)DefinedPageOperation.Write
            && string.IsNullOrWhiteSpace(VarDefinedPageWriteValue))
        {
            StatusMessage = "自定义页写入模式请填写「默认写入值」";
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
                HttpKeyJsonPath = VarHttpKeyPath?.Trim() ?? string.Empty,
                HttpValueJsonPath = VarHttpValuePath?.Trim() ?? string.Empty,
                ShowOnDefinedPage = VarShowOnDefinedPage,
                DefinedPageOperation = VarShowOnDefinedPage
                    ? (DefinedPageOperation)VarDefinedPageOperation
                    : DefinedPageOperation.Read,
                DefinedPageWriteValue = VarShowOnDefinedPage
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

    [RelayCommand]
    private void CancelVariableEdit()
    {
        IsEditingVariable = false;
        StatusMessage = "已取消变量编辑";
    }

    // ── MQTT tab ──────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _mqttIsEnabled = true;

    [ObservableProperty]
    private string _mqttBrokerIp = string.Empty;

    [ObservableProperty]
    private int _mqttPort = 1883;

    [ObservableProperty]
    private string _mqttClientId = string.Empty;

    [ObservableProperty]
    private string _mqttUsername = string.Empty;

    [ObservableProperty]
    private string _mqttPassword = string.Empty;

    [ObservableProperty]
    private string _mqttPubTopic = string.Empty;

    [ObservableProperty]
    private string _mqttSubTopic = string.Empty;

    [ObservableProperty]
    private string _mqttOnlineStatusTopic = string.Empty;

    [ObservableProperty]
    private int _mqttOnlineStatusReportInterval = 30000;

    [RelayCommand]
    private async Task LoadMqttAsync()
    {
        try
        {
            var mqtt = await _service.GetMqttAsync();
            if (mqtt is null)
            {
                MqttIsEnabled = true;
                MqttBrokerIp = string.Empty;
                MqttPort = 1883;
                MqttClientId = string.Empty;
                MqttUsername = string.Empty;
                MqttPassword = string.Empty;
                MqttPubTopic = string.Empty;
                MqttSubTopic = string.Empty;
                MqttOnlineStatusTopic = string.Empty;
                MqttOnlineStatusReportInterval = 30000;
                StatusMessage = "尚未配置 MQTT，可填写后保存";
                return;
            }

            MqttIsEnabled = mqtt.IsEnabled;
            MqttBrokerIp = mqtt.BrokerIp;
            MqttPort = mqtt.Port;
            MqttClientId = mqtt.ClientId;
            MqttUsername = mqtt.Username;
            MqttPassword = mqtt.Password;
            MqttPubTopic = mqtt.PubTopic;
            MqttSubTopic = mqtt.SubTopic;
            MqttOnlineStatusTopic = mqtt.OnlineStatusTopic;
            MqttOnlineStatusReportInterval = mqtt.OnlineStatusReportInterval;
            StatusMessage = mqtt.IsEnabled ? "已加载 MQTT 配置（已开启）" : "已加载 MQTT 配置（已关闭）";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载 MQTT 失败：" + ex.Message;
        }
    }

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
                BrokerIp = MqttBrokerIp?.Trim() ?? string.Empty,
                Port = MqttPort,
                ClientId = MqttClientId?.Trim() ?? string.Empty,
                Username = MqttUsername?.Trim() ?? string.Empty,
                Password = MqttPassword ?? string.Empty,
                PubTopic = MqttPubTopic?.Trim() ?? string.Empty,
                SubTopic = MqttSubTopic?.Trim() ?? string.Empty,
                OnlineStatusTopic = MqttOnlineStatusTopic?.Trim() ?? string.Empty,
                OnlineStatusReportInterval = MqttOnlineStatusReportInterval,
            });
            StatusMessage = MqttIsEnabled
                ? "MQTT 配置已保存并开启采集上报"
                : "MQTT 配置已保存；已关闭，客户端不参与采集上报";
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

    public ObservableCollection<DebugVariableItem> DebugVariables { get; } = [];

    [ObservableProperty]
    private long? _debugDeviceId;

    [ObservableProperty]
    private DebugVariableItem? _selectedDebugVariable;

    [ObservableProperty]
    private string _debugAddress = string.Empty;

    [ObservableProperty]
    private int _debugDataType;

    [ObservableProperty]
    private string _debugWriteValue = string.Empty;

    [ObservableProperty]
    private string _debugOutput = string.Empty;

    partial void OnDebugDeviceIdChanged(long? value)
    {
        _ = LoadDebugVariablesAsync(value);
    }

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
                SelectedDebugVariable = DebugVariables[0];
        }
        catch (Exception ex)
        {
            StatusMessage = "加载调试物模型失败：" + ex.Message;
        }
    }

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

    [ObservableProperty]
    private string _exportJson = string.Empty;

    [ObservableProperty]
    private string _importJson = string.Empty;

    [ObservableProperty]
    private bool _includeMqtt = true;

    [ObservableProperty]
    private string _importMode = "merge";

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

    private async Task RefreshDevicesInternalAsync()
    {
        var name = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var devices = await _service.ListDevicesAsync(name);
        DeviceListItems.Clear();
        foreach (var d in devices.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            DeviceListItems.Add(ToListItem(d));
        RebuildDevicePicker(devices);
    }

    private async Task RefreshVariablesInternalAsync()
    {
        if (SelectedVariableDeviceId is null or <= 0)
            return;

        var list = await _service.ListVariablesAsync(SelectedVariableDeviceId.Value);
        Variables.Clear();
        foreach (var v in list)
            Variables.Add(ToVariableItem(v));
    }

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

        if (selectedVar is not null && DevicePicker.Any(x => x.Id == selectedVar))
            SelectedVariableDeviceId = selectedVar;
        if (selectedDebug is not null && DevicePicker.Any(x => x.Id == selectedDebug))
            DebugDeviceId = selectedDebug;

        OnPropertyChanged(nameof(ShowHttpVariableFields));
    }

    private long NextSuggestedDeviceId()
    {
        if (DeviceListItems.Count == 0)
            return 1;
        return DeviceListItems.Max(x => x.Id) + 1;
    }

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
            PollInterval = EditCollectionEnabled
                ? (EditPollInterval < 100 ? 1000 : EditPollInterval)
                : 0,
            IsActive = EditIsActive,
        };
    }

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
            // keep cleared defaults if JSON is invalid
        }
    }

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

    private static VariableListItem ToVariableItem(DeviceVariable v)
    {
        var rw = (int)v.ReadWrite;
        var rwName = rw >= 0 && rw < ProtocolDisplay.ReadWriteNames.Length
            ? ProtocolDisplay.ReadWriteNames[rw]
            : v.ReadWrite.ToString();

        return new VariableListItem
        {
            Id = v.Id,
            Address = v.Address,
            DataTypeName = v.DataType.ToString(),
            Alias = v.Alias,
            Description = v.Description,
            ReadWriteName = rwName,
            ShowOnDefinedPage = v.ShowOnDefinedPage,
            DefinedPageModeText = !v.ShowOnDefinedPage
                ? string.Empty
                : v.DefinedPageOperation == DefinedPageOperation.Write
                    ? $"写入={v.DefinedPageWriteValue}"
                    : "读取",
            Source = v,
        };
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "(null)",
            string s => s,
            _ => value.ToString() ?? string.Empty,
        };

    private sealed class HttpHeaderItem
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class HttpClientConfigDto
    {
        public string LoginUrl { get; set; } = string.Empty;
        public string LoginMethod { get; set; } = "POST";
        public string LoginBody { get; set; } = string.Empty;
        public string TokenJsonPath { get; set; } = "data.token";
        public string QueryUrl { get; set; } = string.Empty;
        public string QueryMethod { get; set; } = "GET";
        public string QueryBody { get; set; } = string.Empty;
        public List<HttpHeaderItem> QueryHeaders { get; set; } = [];
        public string ResponseBodyJsonPath { get; set; } = "data";
        public string NestedItemsJsonPath { get; set; } = string.Empty;
    }
}

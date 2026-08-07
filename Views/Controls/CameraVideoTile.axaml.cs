using Avalonia.Controls;
using Avalonia.Threading;
using Lana.ViewModels;
using LibVLCSharp.Avalonia;

namespace Lana.Views.Controls;

public partial class CameraVideoTile : UserControl
{
    private CameraPreviewSlot? _boundSlot;

    public CameraVideoTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DetachPlayer();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundSlot is not null)
            _boundSlot.Detaching -= OnSlotDetaching;

        _boundSlot = DataContext as CameraPreviewSlot;
        if (_boundSlot is not null)
            _boundSlot.Detaching += OnSlotDetaching;

        Dispatcher.UIThread.Post(AttachPlayer);
    }

    private void OnSlotDetaching()
        => DetachPlayer();

    private void AttachPlayer()
    {
        if (VideoHost is null)
            return;

        if (DataContext is CameraPreviewSlot slot)
            VideoHost.MediaPlayer = slot.Player;
        else
            VideoHost.MediaPlayer = null;
    }

    private void DetachPlayer()
    {
        if (VideoHost is not null)
            VideoHost.MediaPlayer = null;
    }
}

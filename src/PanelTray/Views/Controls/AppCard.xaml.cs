using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PanelTray.Interop;
using PanelTray.ViewModels;
using PanelTray.Views;

namespace PanelTray.Views.Controls;

public partial class AppCard : UserControl
{
    private static readonly TimeSpan FlyoutHoverDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan FlyoutHideDelay = TimeSpan.FromMilliseconds(250);

    private Point _dragStart;
    private bool _mouseDownForDrag;
    private bool _dragMovementDetected;
    private bool _pointerOverFlyout;
    private bool _flyoutPinned;
    private readonly DispatcherTimer _showFlyoutTimer;
    private readonly DispatcherTimer _hideFlyoutTimer;

    public AppCard()
    {
        InitializeComponent();
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        PreviewMouseMove += OnPreviewMouseMove;

        _showFlyoutTimer = new DispatcherTimer { Interval = FlyoutHoverDelay };
        _showFlyoutTimer.Tick += (_, _) =>
        {
            _showFlyoutTimer.Stop();
            ShowTrayFlyout();
        };

        _hideFlyoutTimer = new DispatcherTimer { Interval = FlyoutHideDelay };
        _hideFlyoutTimer.Tick += (_, _) =>
        {
            _hideFlyoutTimer.Stop();
            if (!_flyoutPinned && !_pointerOverFlyout && !IsMouseOver)
            {
                HideTrayFlyout();
            }
        };

        TrayFlyoutPopup.Opened += OnTrayFlyoutPopupOpened;
        TrayFlyoutPopup.Closed += OnTrayFlyoutPopupClosed;

        if (CardBorder.ContextMenu is ContextMenu menu)
        {
            menu.Opened += OnContextMenuOpened;
            menu.Closed += OnContextMenuClosed;
        }
    }

    private PanelViewModel? PanelViewModel
        => Window.GetWindow(this)?.DataContext as PanelViewModel;

    private PanelWindow? PanelWindow => Window.GetWindow(this) as PanelWindow;

    private AppEntryViewModel? App => DataContext as AppEntryViewModel;

    public void SetDragPlaceholder(bool active)
    {
        if (App is not null)
        {
            App.IsDragPlaceholder = active;
        }
    }

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (!CanUseTrayFlyout())
        {
            return;
        }

        _hideFlyoutTimer.Stop();
        _showFlyoutTimer.Start();
    }

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        _showFlyoutTimer.Stop();
        ScheduleHideFlyout();
    }

    private void OnFlyoutMouseEnter(object sender, MouseEventArgs e)
    {
        _pointerOverFlyout = true;
        _hideFlyoutTimer.Stop();
    }

    private void OnFlyoutMouseLeave(object sender, MouseEventArgs e)
    {
        _pointerOverFlyout = false;
        ScheduleHideFlyout();
    }

    private void OnTrayFlyoutPopupOpened(object? sender, EventArgs e)
    {
        PanelWindow?.BeginAuxiliaryUi();
        PopupWindowHelper.TrySetTopmostForPopup(TrayFlyoutPopup);
    }

    private void OnTrayFlyoutPopupClosed(object? sender, EventArgs e)
    {
        _flyoutPinned = false;
        PanelWindow?.EndAuxiliaryUi();
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        PanelWindow?.BeginAuxiliaryUi();

        if (sender is ContextMenu menu)
        {
            Dispatcher.BeginInvoke(
                () => PopupWindowHelper.TrySetTopmostForContextMenu(menu),
                DispatcherPriority.Loaded);
        }
    }

    private void OnContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        PanelWindow?.EndAuxiliaryUi();
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: ContextMenu menu })
        {
            return;
        }

        menu.Background = GetThemeBrush("CardBgBrush");
        menu.Foreground = GetThemeBrush("TextBrush");
        menu.BorderBrush = GetThemeBrush("CardHoverBrush");
    }

    private Brush GetThemeBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;

    private void OnInfoClick(object sender, RoutedEventArgs e)
    {
        if (App is null || App.IsDropSlot || PanelViewModel is null)
        {
            return;
        }

        if (App.UsesTrayFlyout)
        {
            _flyoutPinned = true;
            ShowTrayFlyout(force: true);
            return;
        }

        PanelViewModel.TryShowNativeTrayMenu(App);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragMovementDetected = false;
        _dragStart = e.GetPosition(this);

        if (PanelViewModel?.EditMode != true || App?.IsDropSlot == true)
        {
            _mouseDownForDrag = false;
            return;
        }

        _mouseDownForDrag = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var panel = Window.GetWindow(this) as PanelWindow;
        var suppressOpen = panel?.ShouldSuppressClick == true
            || _dragMovementDetected
            || PanelViewModel?.EditMode == true
            || App?.IsDropSlot == true
            || panel?.IsLiveDragging == true;

        _mouseDownForDrag = false;
        _dragMovementDetected = false;

        if (suppressOpen)
        {
            e.Handled = true;
            return;
        }

        if (CanUseTrayFlyout())
        {
            ShowTrayFlyout();
            return;
        }

        if (App is not null)
        {
            PanelViewModel?.OpenOrActivateCommand.Execute(App);
        }
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || App is null || App.IsDropSlot)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(current.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _dragMovementDetected = true;
        }

        if (!_mouseDownForDrag || PanelViewModel?.EditMode != true)
        {
            return;
        }

        if (!_dragMovementDetected)
        {
            return;
        }

        _mouseDownForDrag = false;
        HideTrayFlyout();

        if (Window.GetWindow(this) is PanelWindow panel)
        {
            panel.BeginLiveDrag(this, App, _dragStart);
        }
    }

    private bool CanUseTrayFlyout()
        => App?.UsesTrayFlyout == true && PanelViewModel?.EditMode != true;

    private void ShowTrayFlyout(bool force = false)
    {
        if (App is null || !App.UsesTrayFlyout || PanelViewModel is null)
        {
            return;
        }

        if (!force && !CanUseTrayFlyout())
        {
            return;
        }

        TrayFlyout.Snapshot = PanelViewModel.GetTailscaleStatus();
        TrayFlyoutPopup.IsOpen = true;
    }

    private void HideTrayFlyout()
    {
        _flyoutPinned = false;
        TrayFlyoutPopup.IsOpen = false;
    }

    private void ScheduleHideFlyout()
    {
        if (_flyoutPinned)
        {
            return;
        }

        _hideFlyoutTimer.Stop();
        _hideFlyoutTimer.Start();
    }

    private void OnTrayFlyoutNativeMenuRequested(object sender, EventArgs e)
    {
        if (App is not null)
        {
            PanelViewModel?.TryShowNativeTrayMenu(App);
        }
    }

    private void OnTrayFlyoutExitRequested(object sender, EventArgs e)
    {
        PanelViewModel?.CloseTailscaleApp();
        HideTrayFlyout();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => Execute(vm => vm.OpenCommand);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Execute(vm => vm.CloseCommand);
    private void OnCloseAllClick(object sender, RoutedEventArgs e) => Execute(vm => vm.CloseAllCommand);
    private void OnRestartClick(object sender, RoutedEventArgs e) => Execute(vm => vm.RestartCommand);
    private void OnOpenLocationClick(object sender, RoutedEventArgs e) => Execute(vm => vm.OpenLocationCommand);
    private void OnDeleteClick(object sender, RoutedEventArgs e) => Execute(vm => vm.DeleteCommand);

    private void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (App is not null)
        {
            PanelViewModel?.RequestEdit(App);
        }
    }

    private void Execute(Func<PanelViewModel, ICommand> commandSelector)
    {
        if (App is null || PanelViewModel is null)
        {
            return;
        }

        var command = commandSelector(PanelViewModel);
        if (command.CanExecute(App))
        {
            command.Execute(App);
        }
    }
}

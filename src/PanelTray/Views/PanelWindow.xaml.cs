using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using PanelTray.Interop;
using PanelTray.Models;
using PanelTray.Services;
using PanelTray.ViewModels;
using PanelTray.Views.Controls;
using Forms = System.Windows.Forms;

namespace PanelTray.Views;

public partial class PanelWindow : Window
{
    private bool _allowClose;
    private bool _isLiveDragging;
    private FrameworkElement? _dragPreview;
    private AppEntryViewModel? _dragApp;
    private AppCard? _dragSourceCard;
    private Point _dragGrabOffset;
    private bool _externalDragActive;
    private DateTime _suppressClicksUntilUtc = DateTime.MinValue;
    private int _auxiliaryUiDepth;

    public PanelWindow(PanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => MarkSelectedCategory();

        DragOver += OnExternalDragOver;
        DragEnter += OnExternalDragEnter;
        DragLeave += OnExternalDragLeave;
        Drop += OnExternalDrop;
    }

    public PanelViewModel ViewModel => (PanelViewModel)DataContext;

    public bool IsLiveDragging => _isLiveDragging;

    public bool ShouldSuppressClick => _isLiveDragging || DateTime.UtcNow < _suppressClicksUntilUtc;

    public void BeginAuxiliaryUi() => _auxiliaryUiDepth++;

    public void EndAuxiliaryUi()
    {
        if (_auxiliaryUiDepth > 0)
        {
            _auxiliaryUiDepth--;
        }
    }

    public bool HasAuxiliaryUiOpen => _auxiliaryUiDepth > 0;

    public void ShowNearTray()
    {
        Show();
        UpdateLayout();
        PositionNearTray();

        // First show can run before WPF has applied DPI/layout; reposition once ready.
        Dispatcher.BeginInvoke(() =>
        {
            UpdateLayout();
            PositionNearTray();
        }, DispatcherPriority.Loaded);

        Activate();
        ViewModel.RefreshProcesses();
    }

    public void AllowClose() => _allowClose = true;

    public void BeginLiveDrag(AppCard sourceCard, AppEntryViewModel app, Point grabOffset)
    {
        if (_isLiveDragging)
        {
            return;
        }

        _isLiveDragging = true;
        _dragApp = app;
        _dragSourceCard = sourceCard;

        var cardTransform = sourceCard.TransformToAncestor(RootGrid);
        var grabOnGrid = cardTransform.Transform(grabOffset);
        var cardTopLeftOnGrid = cardTransform.Transform(new Point(0, 0));
        _dragGrabOffset = new Point(
            grabOnGrid.X - cardTopLeftOnGrid.X,
            grabOnGrid.Y - cardTopLeftOnGrid.Y);

        ViewModel.BeginDragSession(app);
        sourceCard.SetDragPlaceholder(true);

        _dragPreview = CreateDragPreview(sourceCard, app);
        DragOverlay.Children.Add(_dragPreview);

        PreviewMouseMove += OnLiveDragMove;
        PreviewMouseLeftButtonUp += OnLiveDragEnd;
        PreviewKeyDown += OnLiveDragKeyDown;
        Mouse.Capture(this);

        UpdateDragPreviewPosition(GetMousePositionOnGrid());
    }

    private void EndLiveDrag(bool commit)
    {
        if (!_isLiveDragging)
        {
            return;
        }

        _suppressClicksUntilUtc = DateTime.UtcNow.AddMilliseconds(450);

        PreviewMouseMove -= OnLiveDragMove;
        PreviewMouseLeftButtonUp -= OnLiveDragEnd;
        PreviewKeyDown -= OnLiveDragKeyDown;

        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }

        if (_dragPreview is not null)
        {
            DragOverlay.Children.Remove(_dragPreview);
            _dragPreview = null;
        }

        _dragSourceCard?.SetDragPlaceholder(false);
        _dragSourceCard = null;
        _dragApp = null;
        _isLiveDragging = false;

        if (commit)
        {
            ViewModel.CommitDragSession();
        }
        else
        {
            ViewModel.CancelDragSession();
        }
    }

    private void OnLiveDragMove(object sender, MouseEventArgs e)
    {
        if (!_isLiveDragging || _dragApp is null)
        {
            return;
        }

        UpdateDragPreviewPosition(e.GetPosition(RootGrid));
        UpdateLivePreview(e.GetPosition(RootGrid));
    }

    private void OnLiveDragEnd(object sender, MouseButtonEventArgs e)
    {
        EndLiveDrag(commit: true);
        e.Handled = true;
        Dispatcher.BeginInvoke(() => _suppressClicksUntilUtc = DateTime.UtcNow.AddMilliseconds(450));
    }

    private void OnLiveDragKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndLiveDrag(commit: false);
            e.Handled = true;
        }
    }

    private void UpdateDragPreviewPosition(Point gridPoint)
    {
        if (_dragPreview is null)
        {
            return;
        }

        Canvas.SetLeft(_dragPreview, gridPoint.X - _dragGrabOffset.X);
        Canvas.SetTop(_dragPreview, gridPoint.Y - _dragGrabOffset.Y);
    }

    private Point GetMousePositionOnGrid() => Mouse.GetPosition(RootGrid);

    private void UpdateLivePreview(Point gridPoint)
    {
        if (_dragApp is null)
        {
            return;
        }

        var category = HitTestCategory(gridPoint) ?? ViewModel.SelectedCategory;

        var targetCard = HitTestAppCard(gridPoint);
        AppEntryViewModel? target = null;
        if (targetCard?.DataContext is AppEntryViewModel candidate
            && !candidate.IsDropSlot
            && candidate != _dragApp)
        {
            target = candidate;
        }

        if (targetCard?.DataContext is AppEntryViewModel { IsDropSlot: true })
        {
            if (category != ViewModel.SelectedCategory)
            {
                SelectCategory(category);
            }

            return;
        }

        ViewModel.PreviewMoveApp(_dragApp, target, category);

        if (category != ViewModel.SelectedCategory)
        {
            SelectCategory(category);
        }
    }

    private AppCard? HitTestAppCard(Point gridPoint)
    {
        var element = RootGrid.InputHitTest(gridPoint) as DependencyObject;
        while (element is not null)
        {
            if (element is AppCard card)
            {
                return card;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private AppCategory? HitTestCategory(Point gridPoint)
    {
        var element = RootGrid.InputHitTest(gridPoint) as DependencyObject;
        while (element is not null)
        {
            if (element is RadioButton { DataContext: AppCategory category })
            {
                return category;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void SelectCategory(AppCategory category)
    {
        if (ViewModel.SelectedCategory == category)
        {
            MarkSelectedCategory();
            return;
        }

        ViewModel.SelectedCategory = category;
        MarkSelectedCategory();
    }

    private Border CreateDragPreview(AppCard sourceCard, AppEntryViewModel app)
    {
        var cardBg = TryFindResource("CardBgBrush") as Brush ?? Brushes.DimGray;
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.MediumPurple;
        var textBrush = TryFindResource("TextBrush") as Brush ?? Brushes.White;

        var border = new Border
        {
            Width = Math.Max(76, sourceCard.ActualWidth),
            Height = Math.Max(88, sourceCard.ActualHeight),
            Background = cardBg,
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.45
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var iconHost = new Grid
        {
            Width = app.IconSize,
            Height = app.IconSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        iconHost.Children.Add(new Image
        {
            Source = app.Icon,
            Stretch = Stretch.Uniform
        });

        Grid.SetRow(iconHost, 1);
        grid.Children.Add(iconHost);

        if (app.ShowName)
        {
            var name = new TextBlock
            {
                Text = app.DisplayName,
                Foreground = textBrush,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0)
            };
            Grid.SetRow(name, 2);
            grid.Children.Add(name);
        }

        border.Child = grid;
        return border;
    }

    private void PositionNearTray()
    {
        var screen = GetScreenNearTray();
        if (screen is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        var scaleX = GetDpiScaleX();
        var scaleY = GetDpiScaleY();
        var panelWidth = GetPanelWidth();
        var panelHeight = GetPanelHeight();
        var area = screen.WorkingArea;

        Left = area.Right / scaleX - panelWidth - 18;
        Top = area.Bottom / scaleY - panelHeight - 18;
    }

    private static Forms.Screen? GetScreenNearTray()
    {
        var primary = Forms.Screen.PrimaryScreen;
        if (primary is null)
        {
            return null;
        }

        var anchor = new System.Drawing.Point(primary.WorkingArea.Right - 1, primary.WorkingArea.Bottom - 1);
        return Forms.Screen.FromPoint(anchor) ?? primary;
    }

    private double GetPanelWidth() => ActualWidth > 0 ? ActualWidth : ViewModel.Settings.PanelWidth;

    private double GetPanelHeight() => ActualHeight > 0 ? ActualHeight : ViewModel.Settings.PanelHeight;

    private double GetDpiScaleX()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            return source.CompositionTarget.TransformToDevice.M11;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            return GetDpiForWindow(hwnd) / 96.0;
        }

        return 1;
    }

    private double GetDpiScaleY()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            return source.CompositionTarget.TransformToDevice.M22;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            return GetDpiForWindow(hwnd) / 96.0;
        }

        return 1;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => ViewModel.RequestSettings();

    private void OnAddInstalledAppClick(object sender, RoutedEventArgs e)
        => ViewModel.TryAddInstalledApp(this);

    private void OnHidePanelClick(object sender, RoutedEventArgs e)
    {
        if (_isLiveDragging)
        {
            EndLiveDrag(commit: true);
        }

        Hide();
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (_isLiveDragging)
        {
            EndLiveDrag(commit: true);
        }

        if (!ViewModel.Settings.HideOnFocusLost || ViewModel.EditMode || _externalDragActive)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || ViewModel.EditMode || _externalDragActive || HasAuxiliaryUiOpen)
            {
                return;
            }

            Hide();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnExternalDragEnter(object sender, DragEventArgs e)
    {
        if (TryAcceptExternalDrop(e))
        {
            _externalDragActive = true;
        }
    }

    private void OnExternalDragLeave(object sender, DragEventArgs e)
    {
        if (!IsMouseOver)
        {
            _externalDragActive = false;
        }
    }

    private void OnExternalDragOver(object sender, DragEventArgs e)
    {
        if (TryAcceptExternalDrop(e))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnExternalDrop(object sender, DragEventArgs e)
    {
        _externalDragActive = false;

        if (!ViewModel.EditMode || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        var path = files?.FirstOrDefault(ShortcutImportService.CanImport);
        if (path is null)
        {
            return;
        }

        var dropPoint = e.GetPosition(RootGrid);
        var category = HitTestCategory(dropPoint) ?? ViewModel.SelectedCategory;
        if (category != ViewModel.SelectedCategory)
        {
            SelectCategory(category);
        }

        var target = HitTestAppCard(dropPoint)?.DataContext as AppEntryViewModel;
        ViewModel.AddAppFromShortcut(path, category, target);
        e.Handled = true;
    }

    private bool TryAcceptExternalDrop(DragEventArgs e)
    {
        if (!ViewModel.EditMode || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        return files?.Any(ShortcutImportService.CanImport) == true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isLiveDragging)
        {
            EndLiveDrag(commit: true);
        }

        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnCategoryChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: AppCategory category })
        {
            ViewModel.SelectedCategory = category;
        }
    }

    private void MarkSelectedCategory()
    {
        foreach (var radio in FindVisualChildren<RadioButton>(this))
        {
            if (radio.DataContext is AppCategory category && category == ViewModel.SelectedCategory)
            {
                radio.IsChecked = true;
                break;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

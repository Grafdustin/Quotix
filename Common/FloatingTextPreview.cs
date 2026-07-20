using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quotix.Common;

/// <summary>
/// Shows a small floating text preview fixed to the related text box.
/// </summary>
public static class FloatingTextPreview
{
    private static bool _registered;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(FloatingTextPreview),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(HoverState),
            typeof(FloatingTextPreview),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static void RegisterGlobal()
    {
        if (_registered)
            return;

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is TextBox textBox && GetState(textBox) == null)
                    SetIsEnabled(textBox, true);
            }));
    }

    private static HoverState? GetState(DependencyObject obj) => (HoverState?)obj.GetValue(StateProperty);
    private static void SetState(DependencyObject obj, HoverState? value) => obj.SetValue(StateProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            if (GetState(textBox) != null)
                return;

            var state = new HoverState(textBox);
            SetState(textBox, state);
            state.Attach();
        }
        else
        {
            GetState(textBox)?.Detach();
            SetState(textBox, null);
        }
    }

    private sealed class HoverState
    {
        private readonly TextBox _textBox;
        private readonly DispatcherTimer _openTimer;
        private Popup? _popup;
        private TextBlock? _textBlock;
        private FrameworkElement? _popupRoot;
        private Border? _bubbleBorder;
        private System.Windows.Shapes.Path? _arrow;

        public HoverState(TextBox textBox)
        {
            _textBox = textBox;
            _openTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(280)
            };
            _openTimer.Tick += (_, _) =>
            {
                _openTimer.Stop();
                ShowOrUpdate();
            };
        }

        public void Attach()
        {
            _textBox.MouseEnter += OnMouseEnter;
            _textBox.MouseLeave += OnCloseRequested;
            _textBox.GotKeyboardFocus += OnKeyboardFocus;
            _textBox.LostKeyboardFocus += OnCloseRequested;
            _textBox.Unloaded += OnCloseRequested;
            _textBox.PreviewMouseWheel += OnCloseRequested;
            _textBox.TextChanged += OnTextChanged;
            _textBox.IsVisibleChanged += OnIsVisibleChanged;
            _textBox.LayoutUpdated += OnLayoutUpdated;
        }

        public void Detach()
        {
            Close();
            _openTimer.Stop();
            _textBox.MouseEnter -= OnMouseEnter;
            _textBox.MouseLeave -= OnCloseRequested;
            _textBox.GotKeyboardFocus -= OnKeyboardFocus;
            _textBox.LostKeyboardFocus -= OnCloseRequested;
            _textBox.Unloaded -= OnCloseRequested;
            _textBox.PreviewMouseWheel -= OnCloseRequested;
            _textBox.TextChanged -= OnTextChanged;
            _textBox.IsVisibleChanged -= OnIsVisibleChanged;
            _textBox.LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (HasPreviewText())
                _openTimer.Start();
        }

        private void OnKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (HasPreviewText())
                _openTimer.Start();
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_popup?.IsOpen == true)
                MovePopup();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!HasPreviewText())
            {
                Close();
                return;
            }

            if (_popup?.IsOpen == true && _textBlock != null)
                _textBlock.Text = _textBox.Text;
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is false)
                Close();
        }

        private void OnCloseRequested(object sender, EventArgs e)
        {
            Close();
        }

        private bool HasPreviewText() => !string.IsNullOrWhiteSpace(_textBox.Text);

        private void ShowOrUpdate()
        {
            if (!HasPreviewText() || (!_textBox.IsMouseOver && !_textBox.IsKeyboardFocusWithin) || !_textBox.IsVisible)
                return;

            EnsurePopup();
            if (_textBlock == null || _popup == null)
                return;

            _textBlock.Text = _textBox.Text;
            MovePopup();
            _popup.IsOpen = true;
        }

        private void EnsurePopup()
        {
            if (_popup != null)
                return;

            _textBlock = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Foreground = Brushes.White
            };

            var background = new SolidColorBrush(Color.FromArgb(238, 34, 34, 34));
            _bubbleBorder = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7, 10, 8),
                MaxWidth = 460,
                Child = _textBlock,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.22,
                    Color = Colors.Black
                }
            };

            _arrow = new System.Windows.Shapes.Path
            {
                Width = 14,
                Height = 7,
                Stretch = Stretch.Fill,
                Fill = background,
                Data = Geometry.Parse("M 0 0 L 14 0 L 7 7 Z"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, -1, 0, 0)
            };

            var popupRoot = new Grid
            {
                IsHitTestVisible = false
            };
            popupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            popupRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            popupRoot.Children.Add(_bubbleBorder);
            popupRoot.Children.Add(_arrow);
            _popupRoot = popupRoot;

            _popup = new Popup
            {
                AllowsTransparency = true,
                Focusable = false,
                IsHitTestVisible = false,
                Placement = PlacementMode.Top,
                PlacementTarget = _textBox,
                Child = _popupRoot
            };
        }

        private void MovePopup()
        {
            if (_popup == null)
                return;

            if (_popupRoot == null || _bubbleBorder == null || _arrow == null)
                return;

            _popupRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _popupRoot.DesiredSize;
            if (_textBox.ActualWidth <= 0 || _textBox.ActualHeight <= 0)
                return;

            Grid.SetRow(_bubbleBorder, 0);
            Grid.SetRow(_arrow, 1);
            _arrow.Data = Geometry.Parse("M 0 0 L 14 0 L 7 7 Z");

            _popup.HorizontalOffset = (_textBox.ActualWidth - size.Width) / 2;
            _popup.VerticalOffset = -8;

            var arrowLeft = Math.Clamp((size.Width - _arrow.Width) / 2, 8, Math.Max(8, size.Width - _arrow.Width - 8));
            _arrow.Margin = new Thickness(arrowLeft, -1, 0, 0);
        }

        private void Close()
        {
            _openTimer.Stop();
            if (_popup != null)
                _popup.IsOpen = false;
        }
    }
}

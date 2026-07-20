using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Quotix.Common;

/// <summary>
/// Shows a small floating text preview for text boxes while the mouse is hovering.
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
        private System.Windows.Shapes.Path? _arrow;
        private Point _lastPoint;

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
            _textBox.MouseMove += OnMouseMove;
            _textBox.MouseLeave += OnCloseRequested;
            _textBox.LostKeyboardFocus += OnCloseRequested;
            _textBox.Unloaded += OnCloseRequested;
            _textBox.PreviewMouseWheel += OnCloseRequested;
            _textBox.TextChanged += OnTextChanged;
            _textBox.IsVisibleChanged += OnIsVisibleChanged;
        }

        public void Detach()
        {
            Close();
            _openTimer.Stop();
            _textBox.MouseEnter -= OnMouseEnter;
            _textBox.MouseMove -= OnMouseMove;
            _textBox.MouseLeave -= OnCloseRequested;
            _textBox.LostKeyboardFocus -= OnCloseRequested;
            _textBox.Unloaded -= OnCloseRequested;
            _textBox.PreviewMouseWheel -= OnCloseRequested;
            _textBox.TextChanged -= OnTextChanged;
            _textBox.IsVisibleChanged -= OnIsVisibleChanged;
        }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            _lastPoint = e.GetPosition(_textBox);
            if (HasPreviewText())
                _openTimer.Start();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _lastPoint = e.GetPosition(_textBox);
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
            if (!HasPreviewText() || !_textBox.IsMouseOver || !_textBox.IsVisible)
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
            var border = new Border
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
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            };

            _popupRoot = new StackPanel
            {
                Orientation = Orientation.Vertical,
                IsHitTestVisible = false,
                Children =
                {
                    border,
                    _arrow
                }
            };

            _popup = new Popup
            {
                AllowsTransparency = true,
                Focusable = false,
                IsHitTestVisible = false,
                Placement = PlacementMode.RelativePoint,
                PlacementTarget = _textBox,
                Child = _popupRoot
            };
        }

        private void MovePopup()
        {
            if (_popup == null)
                return;

            if (_popupRoot == null || _arrow == null)
                return;

            _popupRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _popupRoot.DesiredSize;
            var screenPoint = _textBox.PointToScreen(_lastPoint);
            var showAbove = screenPoint.Y - size.Height - 12 > SystemParameters.WorkArea.Top;

            _arrow.Data = Geometry.Parse(showAbove
                ? "M 0 0 L 14 0 L 7 7 Z"
                : "M 7 0 L 14 7 L 0 7 Z");

            _popup.HorizontalOffset = _lastPoint.X - (size.Width / 2);
            _popup.VerticalOffset = showAbove
                ? _lastPoint.Y - size.Height - 12
                : _lastPoint.Y + 12;
        }

        private void Close()
        {
            _openTimer.Stop();
            if (_popup != null)
                _popup.IsOpen = false;
        }
    }
}

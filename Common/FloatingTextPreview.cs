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

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(238, 34, 34, 34)),
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

            _popup = new Popup
            {
                AllowsTransparency = true,
                Focusable = false,
                IsHitTestVisible = false,
                Placement = PlacementMode.RelativePoint,
                PlacementTarget = _textBox,
                Child = border
            };
        }

        private void MovePopup()
        {
            if (_popup == null)
                return;

            _popup.HorizontalOffset = _lastPoint.X + 16;
            _popup.VerticalOffset = _lastPoint.Y + 18;
        }

        private void Close()
        {
            _openTimer.Stop();
            if (_popup != null)
                _popup.IsOpen = false;
        }
    }
}

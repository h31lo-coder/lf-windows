using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace LfWindows.Controls;

public class ZoomBorder : Border
{
    private Point _origin;
    private Point _start;

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<ZoomBorder, Stretch>(nameof(Stretch), Stretch.Uniform);

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public static readonly StyledProperty<double> ZoomSpeedProperty =
        AvaloniaProperty.Register<ZoomBorder, double>(nameof(ZoomSpeed), 1.2);

    public double ZoomSpeed
    {
        get => GetValue(ZoomSpeedProperty);
        set => SetValue(ZoomSpeedProperty, value);
    }

    public static readonly StyledProperty<ButtonName> PanButtonProperty =
        AvaloniaProperty.Register<ZoomBorder, ButtonName>(nameof(PanButton), ButtonName.Left);

    public ButtonName PanButton
    {
        get => GetValue(PanButtonProperty);
        set => SetValue(PanButtonProperty, value);
    }

    public static readonly StyledProperty<bool> EnablePanProperty =
        AvaloniaProperty.Register<ZoomBorder, bool>(nameof(EnablePan), true);

    public bool EnablePan
    {
        get => GetValue(EnablePanProperty);
        set => SetValue(EnablePanProperty, value);
    }

    public static readonly StyledProperty<bool> EnableZoomProperty =
        AvaloniaProperty.Register<ZoomBorder, bool>(nameof(EnableZoom), true);

    public bool EnableZoom
    {
        get => GetValue(EnableZoomProperty);
        set => SetValue(EnableZoomProperty, value);
    }

    public enum ButtonName { Left, Middle, Right }

    public ZoomBorder()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private TranslateTransform GetTranslateTransform(TransformGroup group)
    {
        var transform = group.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (transform == null)
        {
            transform = new TranslateTransform();
            group.Children.Add(transform);
        }
        return transform;
    }

    private ScaleTransform GetScaleTransform(TransformGroup group)
    {
        var transform = group.Children.OfType<ScaleTransform>().FirstOrDefault();
        if (transform == null)
        {
            transform = new ScaleTransform();
            group.Children.Add(transform);
        }
        return transform;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!EnableZoom || Child == null) return;

        if (!(Child.RenderTransform is TransformGroup group))
        {
            group = new TransformGroup();
            Child.RenderTransform = group;
        }

        var scale = GetScaleTransform(group);
        var translate = GetTranslateTransform(group);

        double zoom = e.Delta.Y > 0 ? ZoomSpeed : 1 / ZoomSpeed;
        
        // Limit zoom
        if (scale.ScaleX * zoom < 0.1 || scale.ScaleX * zoom > 10) return;

        Point relative = e.GetPosition(Child);
        double abosuluteX = relative.X * scale.ScaleX + translate.X;
        double abosuluteY = relative.Y * scale.ScaleY + translate.Y;

        scale.ScaleX *= zoom;
        scale.ScaleY *= zoom;

        translate.X = abosuluteX - relative.X * scale.ScaleX;
        translate.Y = abosuluteY - relative.Y * scale.ScaleY;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!EnablePan || Child == null) return;

        var properties = e.GetCurrentPoint(this).Properties;
        bool isPressed = false;
        
        switch (PanButton)
        {
            case ButtonName.Left: isPressed = properties.IsLeftButtonPressed; break;
            case ButtonName.Middle: isPressed = properties.IsMiddleButtonPressed; break;
            case ButtonName.Right: isPressed = properties.IsRightButtonPressed; break;
        }

        if (isPressed)
        {
            _origin = e.GetPosition(this);
            if (Child.RenderTransform is TransformGroup group)
            {
                var translate = GetTranslateTransform(group);
                _start = new Point(translate.X, translate.Y);
                e.Pointer.Capture(this);
                Cursor = new Cursor(StandardCursorType.Hand);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Child == null) return;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (Child == null) return;

        if (Child.RenderTransform is not TransformGroup group)
        {
            group = new TransformGroup();
            Child.RenderTransform = group;
        }

        var translate = GetTranslateTransform(group);
        var scale = GetScaleTransform(group);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            var v = _start + (e.GetPosition(this) - _origin);
            translate.X = v.X;
            translate.Y = v.Y;
        }
    }

    public void Reset()
    {
        if (Child != null)
        {
            Child.RenderTransform = new TransformGroup();
        }
    }
}

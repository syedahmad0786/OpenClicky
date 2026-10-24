using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using OpenClicky.Core.Drawing;
using DrawingColor = System.Drawing.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace OpenClicky.Overlay;

/// <summary>
/// One 3D figure in its own WPF window over the part's box: a Viewport3D lit by a grey ambient and a white directional light,
/// each solid in its colour (or the accent) with a white highlight, and an accent label pill over each labelled part. It
/// scales in over 0.5 s (ease-out back) and, when asked, spins about Y at 20°/s; reduced motion fades it in over 120 ms
/// with no scale-in and no spin. Topmost, click-through and never activated. UI thread only.
/// </summary>
internal sealed class FigureWindow : Window
{
    private const double RevealSeconds = 0.5, ReducedFadeSeconds = 0.12, SpinDegreesPerSecond = 20, LabelDip = 11, MinSidePx = 8;
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000, WsExTransparent = 0x00000020, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    private static readonly nint HwndTopMost = -1;

    private readonly Rectangle _bounds;
    private readonly bool _reducedMotion, _spin;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _start;
    private readonly CameraSpec _camera;
    private readonly PerspectiveCamera _view;
    private readonly ScaleTransform3D _scale;
    private readonly AxisAngleRotation3D _turn = new(new Vector3D(0, 1, 0), 0);
    private readonly Transform3D _motion;
    private readonly Canvas _labels = new() { IsHitTestVisible = false, Visibility = Visibility.Hidden };
    private readonly List<(Point3D At, Border Pill)> _pills = [];
    private bool _rendering, _settled;

    /// <param name="bounds">Where the figure floats, in virtual-screen pixels (see <see cref="BoundsOf"/>).</param>
    /// <param name="reveal">When its reveal starts from now; null shows it whole at once (an edit, an undo).</param>
    public FigureWindow(FigureSpec figure, Rectangle bounds, DrawingColor accent, bool reducedMotion, TimeSpan? reveal)
    {
        _bounds = bounds;
        _reducedMotion = reducedMotion;
        _spin = figure.Spin && !reducedMotion;
        _start = reveal is { } r ? Math.Max(0, r.TotalSeconds) : double.NegativeInfinity;

        Title = "OpenClicky Figure";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Left = Top = 0;
        Width = Height = 1; // the real size is set in physical pixels once the handle exists

        var solids = new Model3DGroup();
        var points = new List<Vector3>();
        foreach (var part in figure.Parts)
        {
            var mesh = FigureMesh.Build(part);
            var place = FigureMesh.Placement(part);
            var geometry = new MeshGeometry3D
            {
                Positions = new Point3DCollection(mesh.Positions.Select(p => new Point3D(p.X, p.Y, p.Z))),
                Normals = new Vector3DCollection(mesh.Normals.Select(n => new Vector3D(n.X, n.Y, n.Z))),
                TriangleIndices = new Int32Collection(mesh.Indices),
            };
            geometry.Freeze();
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(new SolidColorBrush(ColourOf(part.Color, accent))));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 24));
            material.Freeze();
            solids.Children.Add(new GeometryModel3D(geometry, material) { Transform = new MatrixTransform3D(ToMatrix(place)) });
            points.AddRange(mesh.Positions.Select(p => Vector3.Transform(p, place)));
            if (part.Label is { Length: > 0 } label)
            {
                var at = Vector3.Transform(Vector3.Zero, place);
                var pill = Pill(label, accent);
                _pills.Add((new Point3D(at.X, at.Y, at.Z), pill));
                _labels.Children.Add(pill);
            }
        }

        var box = Box3.Of(points);
        _camera = FigureCamera.Fit(box);
        var c = box.Centre;
        var initial = _reducedMotion || double.IsNegativeInfinity(_start) ? 1 : 1e-3; // reduced motion fades instead
        _scale = new ScaleTransform3D(initial, initial, initial, c.X, c.Y, c.Z);
        var motion = new Transform3DGroup();
        motion.Children.Add(_scale);
        motion.Children.Add(new RotateTransform3D(_turn, c.X, c.Y, c.Z));
        _motion = motion;
        solids.Transform = motion;

        var distance = Vector3.Distance(_camera.Position, _camera.Target);
        _view = new PerspectiveCamera(Point(_camera.Position), Vector(_camera.Target - _camera.Position), Vector(_camera.Up), _camera.FieldOfViewDegrees)
        {
            NearPlaneDistance = distance * 0.1,
            FarPlaneDistance = distance * 3,
        };
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(MediaColor.FromRgb(0x50, 0x50, 0x50)));
        lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1.5, -2)));
        var viewport = new Viewport3D { Camera = _view, IsHitTestVisible = false, ClipToBounds = true };
        viewport.Children.Add(new ModelVisual3D { Content = lights });
        viewport.Children.Add(new ModelVisual3D { Content = solids });
        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(viewport);
        root.Children.Add(_labels);
        Content = root;
        if (_reducedMotion && !double.IsNegativeInfinity(_start)) Opacity = 0;

        SizeChanged += (_, _) =>
        {
            _view.FieldOfView = FigureCamera.HorizontalFieldOfView(_camera, ActualWidth, ActualHeight);
            if (_labels.Visibility == Visibility.Visible) PlaceLabels();
        };
        IsVisibleChanged += (_, _) => Animate(IsVisible);
        Closed += (_, _) => Animate(false);
    }

    /// <summary>The window's handle (created on first use, before the window is shown).</summary>
    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>
    /// The box a figure part floats over in virtual-screen pixels, or null when it is not drawable: a non-finite or tiny box,
    /// or one that is not whole on the monitor under its centre (dropped, never clamped).
    /// </summary>
    public static Rectangle? BoundsOf(DrawOp op)
    {
        var b = op.Box;
        if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.W) || !float.IsFinite(b.H) || b.W < MinSidePx || b.H < MinSidePx) return null;
        if (Math.Abs(b.X) > 1e6 || Math.Abs(b.Y) > 1e6 || b.W > 1e6 || b.H > 1e6) return null;
        var rect = Rectangle.Round(new System.Drawing.RectangleF(b.X, b.Y, b.W, b.H));
        var monitor = Screen.FromPoint(new System.Drawing.Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)).Bounds;
        monitor.Inflate(1, 1);
        return monitor.Contains(rect) ? rect : null;
    }

    /// <summary>Shows the window (never activating it) at its box, in physical pixels.</summary>
    public void ShowAt()
    {
        var hwnd = Handle;
        Place(hwnd); // before showing: a move onto a monitor of another scale lets WPF rescale first
        if (!IsVisible) Show();
        Place(hwnd);
    }

    /// <summary>The reveal ends now: the figure shows whole (its spin carries on).</summary>
    public void Finish()
    {
        if (double.IsNegativeInfinity(_start)) return;
        _start = double.NegativeInfinity;
        Step();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLongPtr(hwnd, GwlExStyle, (nint)((long)GetWindowLongPtr(hwnd, GwlExStyle) | WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    private void Place(nint hwnd) =>
        SetWindowPos(hwnd, HwndTopMost, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SwpNoActivate | SwpNoOwnerZOrder);

    private void Animate(bool on)
    {
        if (on) Step();
        on &= !_settled;
        if (on == _rendering) return;
        _rendering = on;
        if (on) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e) => Step();

    private void Step()
    {
        var t = _clock.Elapsed.TotalSeconds - _start;
        var revealed = t >= (_reducedMotion ? ReducedFadeSeconds : RevealSeconds);
        if (_reducedMotion) Opacity = Math.Clamp(t / ReducedFadeSeconds, 0, 1);
        else
        {
            var s = revealed ? 1 : t <= 0 ? 1e-3 : Math.Max(1e-3, EaseOutBack(t / RevealSeconds));
            _scale.ScaleX = _scale.ScaleY = _scale.ScaleZ = s;
            if (_spin) _turn.Angle = SpinDegreesPerSecond * _clock.Elapsed.TotalSeconds % 360;
        }
        if (revealed)
        {
            if (_labels.Visibility != Visibility.Visible || _spin) PlaceLabels();
            _labels.Visibility = Visibility.Visible;
        }
        if (revealed && !_spin)
        {
            _settled = true;
            Animate(false);
        }
    }

    /// <summary>Each pill centred on where its part's centre is now (it moves with the spin).</summary>
    private void PlaceLabels()
    {
        float w = (float)ActualWidth, h = (float)ActualHeight;
        foreach (var (at, pill) in _pills)
        {
            var now = _motion.Transform(at);
            if (FigureCamera.Project(new Vector3((float)now.X, (float)now.Y, (float)now.Z), _camera, w, h) is not { } p)
            {
                pill.Visibility = Visibility.Hidden;
                continue;
            }
            pill.Visibility = Visibility.Visible;
            pill.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(pill, p.X - pill.DesiredSize.Width / 2);
            Canvas.SetTop(pill, p.Y - pill.DesiredSize.Height / 2);
        }
    }

    /// <summary>Ease-out back: overshoots a little past 1, then settles.</summary>
    private static double EaseOutBack(double x)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        x = Math.Clamp(x, 0, 1);
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    private static Border Pill(string label, DrawingColor accent)
    {
        var fill = MediaColor.FromRgb(accent.R, accent.G, accent.B);
        var light = 0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B > 160; // ink on a light accent (yellow), white otherwise
        return new Border
        {
            Background = new SolidColorBrush(fill),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 7, 3),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = label,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = LabelDip,
                Foreground = new SolidColorBrush(light ? MediaColor.FromRgb(0x1F, 0x24, 0x30) : Colors.White),
            },
        };
    }

    private static MediaColor ColourOf(string hex, DrawingColor accent) =>
        hex is { Length: 7 } && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? MediaColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : MediaColor.FromRgb(accent.R, accent.G, accent.B);

    private static Matrix3D ToMatrix(Matrix4x4 m) =>
        new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    private static Point3D Point(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3D Vector(Vector3 v) => new(v.X, v.Y, v.Z);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}

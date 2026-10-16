using R3;
using Termina.Layout;
using Termina.Rendering;
using Termina.Terminal;

namespace Termina.Notifications;

internal sealed class ToastOverlayNode : LayoutNode, IInvalidatingNode
{
    private readonly IToastService _toastService;
    private readonly Subject<Unit> _invalidated = new();
    private readonly IDisposable _subscription;
    private ToastMessage? _currentToast;
    private bool _disposed;

    public ToastOverlayNode(IToastService toastService)
    {
        _toastService = toastService;
        WidthConstraint = new SizeConstraint.Fill();
        HeightConstraint = new SizeConstraint.Fill();

        _subscription = _toastService.CurrentToast.Subscribe(toast =>
        {
            _currentToast = toast;
            _invalidated.OnNext(Unit.Default);
        });
    }

    public Observable<Unit> Invalidated => _invalidated;

    public override Size Measure(Size available) => available;

    public override void Render(IRenderContext context, Rect bounds)
    {
        if (!bounds.HasArea || _currentToast is null)
            return;

        var icon = _currentToast.Icon ?? char.ConvertFromUtf32(0x2713);
        var message = $" {icon} {_currentToast.Message} ";
        var width = Math.Min(bounds.Width, DisplayWidth.GetColumnCount(message) + 2);
        var height = 3;
        if (width <= 0 || bounds.Height < height)
            return;

        var (x, y) = CalculatePosition(bounds, width, height, _currentToast.Position);
        var panelBounds = new Rect(x, y, width, height);
        var panelContext = context.CreateSubContext(panelBounds);

        var borderColor = _currentToast.Color ?? Color.BrightGreen;
        panelContext.SetForeground(borderColor);
        panelContext.WriteAt(0, 0, '╭');
        panelContext.WriteAt(1, 0, new string('─', Math.Max(0, width - 2)));
        panelContext.WriteAt(width - 1, 0, '╮');

        panelContext.WriteAt(0, 1, '│');
        panelContext.SetBackground(Color.Black);
        panelContext.SetForeground(Color.White);
        var messageWidth = width - 2;
        var displayMessage = DisplayWidth.GetColumnCount(message) > messageWidth
            ? DisplayWidth.TruncateToColumns(message, messageWidth)
            : message;
        var padding = messageWidth - DisplayWidth.GetColumnCount(displayMessage);
        panelContext.WriteAt(1, 1, padding > 0 ? displayMessage + new string(' ', padding) : displayMessage);
        panelContext.ResetColors();
        panelContext.SetForeground(borderColor);
        panelContext.WriteAt(width - 1, 1, '│');

        panelContext.WriteAt(0, 2, '╰');
        panelContext.WriteAt(1, 2, new string('─', Math.Max(0, width - 2)));
        panelContext.WriteAt(width - 1, 2, '╯');
        panelContext.ResetColors();
    }

    private static (int X, int Y) CalculatePosition(Rect bounds, int width, int height, ToastPosition position)
    {
        var x = position switch
        {
            ToastPosition.BottomLeft or ToastPosition.TopLeft => 1,
            ToastPosition.BottomCenter or ToastPosition.TopCenter => Math.Max(0, (bounds.Width - width) / 2),
            _ => Math.Max(0, bounds.Width - width - 1)
        };

        var y = position switch
        {
            ToastPosition.TopLeft or ToastPosition.TopCenter or ToastPosition.TopRight => 1,
            _ => Math.Max(0, bounds.Height - height - 1)
        };

        return (x, y);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscription.Dispose();
        _invalidated.OnCompleted();
        _invalidated.Dispose();
        base.Dispose();
    }
}

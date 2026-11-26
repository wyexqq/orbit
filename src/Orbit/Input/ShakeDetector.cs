using Orbit.Interop;

namespace Orbit.Input;

internal sealed class ShakeDetector
{
    private const double MinimumTravel = 18;
    private const int ReversalsNeeded = 2;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(650);

    private Axis horizontal = new();
    private Axis vertical = new();

    public void Reset()
    {
        horizontal = new Axis();
        vertical = new Axis();
    }

    public bool Feed(NativePoint point, double scale)
    {
        var now = DateTime.UtcNow;
        var shookHorizontally = horizontal.Feed(point.X / scale, now);
        var shookVertically = vertical.Feed(point.Y / scale, now);
        return shookHorizontally || shookVertically;
    }

    private sealed class Axis
    {
        private readonly List<DateTime> reversals = [];
        private double? extreme;
        private int direction;

        public bool Feed(double value, DateTime now)
        {
            if (extreme is null)
            {
                extreme = value;
                return false;
            }
            var delta = value - extreme.Value;
            var keepsDirection = direction != 0 && Math.Sign(delta) == direction;
            if (keepsDirection)
            {
                extreme = value;
            }
            else if (Math.Abs(delta) >= MinimumTravel)
            {
                if (direction != 0)
                {
                    reversals.Add(now);
                }
                direction = Math.Sign(delta);
                extreme = value;
            }
            reversals.RemoveAll(moment => now - moment > Window);
            return reversals.Count >= ReversalsNeeded;
        }
    }
}

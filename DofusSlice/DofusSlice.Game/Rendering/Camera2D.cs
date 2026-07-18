using Microsoft.Xna.Framework;

namespace DofusSlice.Game.Rendering;

/// <summary>
/// A 2D world camera: it centres on a world point, scales by a zoom factor, and can shake.
/// World content is drawn through <see cref="View"/>; the HUD is drawn untransformed. Mouse
/// picking goes back through <see cref="ScreenToWorld"/>. The camera smoothly follows a target
/// and clamps so it never scrolls past the map's bounds (unless the map is smaller than the
/// view, in which case it stays centred).
/// </summary>
public sealed class Camera2D
{
    private readonly float _viewW, _viewH;
    private readonly Random _rng = new(1);

    public Vector2 Center;
    public float Zoom { get; private set; } = 1f;
    public float MinZoom { get; set; } = 0.6f;
    public float MaxZoom { get; set; } = 2.2f;

    private Vector2 _shakeOffset;
    private float _shakeAmp, _shakeLife, _shakeTime;

    // World-space bounds the view is clamped within.
    private Vector2 _boundsMin, _boundsMax;

    public Camera2D(float viewW, float viewH)
    {
        _viewW = viewW;
        _viewH = viewH;
    }

    public void SetBounds(Vector2 min, Vector2 max)
    {
        _boundsMin = min;
        _boundsMax = max;
    }

    public Matrix View =>
        Matrix.CreateTranslation(-Center.X, -Center.Y, 0f) *
        Matrix.CreateScale(Zoom, Zoom, 1f) *
        Matrix.CreateTranslation(_viewW / 2f + _shakeOffset.X, _viewH / 2f + _shakeOffset.Y, 0f);

    public Vector2 ScreenToWorld(Vector2 screen) => Vector2.Transform(screen, Matrix.Invert(View));

    public void ZoomBy(float delta) =>
        Zoom = Math.Clamp(Zoom + delta, MinZoom, MaxZoom);

    /// <summary>Request a shake; the strongest pending amplitude wins.</summary>
    public void Shake(float amplitude)
    {
        if (amplitude <= 0f) return;
        _shakeAmp = Math.Max(_shakeAmp, amplitude);
        _shakeLife = 0.32f;
        _shakeTime = 0f;
    }

    public void Update(float dt, Vector2 followTarget, float followLerp = 0.12f)
    {
        Center = Vector2.Lerp(Center, followTarget, followLerp);
        ClampToBounds();

        if (_shakeLife > 0f)
        {
            _shakeTime += dt;
            float k = Math.Max(0f, 1f - _shakeTime / _shakeLife);
            float a = _shakeAmp * k;
            _shakeOffset = new Vector2(
                ((float)_rng.NextDouble() * 2f - 1f) * a,
                ((float)_rng.NextDouble() * 2f - 1f) * a);
            if (_shakeTime >= _shakeLife) { _shakeLife = 0f; _shakeAmp = 0f; _shakeOffset = Vector2.Zero; }
        }
        else
        {
            _shakeOffset = Vector2.Zero;
        }
    }

    private void ClampToBounds()
    {
        if (_boundsMax.X <= _boundsMin.X) return;
        float halfW = _viewW / (2f * Zoom);
        float halfH = _viewH / (2f * Zoom);

        Center.X = SpanClamp(Center.X, _boundsMin.X, _boundsMax.X, halfW);
        Center.Y = SpanClamp(Center.Y, _boundsMin.Y, _boundsMax.Y, halfH);
    }

    private static float SpanClamp(float v, float min, float max, float half)
    {
        // If the content is narrower than the view, lock to its centre; else clamp the edges.
        if (max - min <= half * 2f) return (min + max) / 2f;
        return Math.Clamp(v, min + half, max - half);
    }
}

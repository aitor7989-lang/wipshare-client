namespace WipShare.Client.Capture;

/// <summary>
/// A rectangular region on a specific monitor, expressed in that monitor's
/// physical pixels (i.e. <see cref="X"/>/<see cref="Y"/> are zero-based from
/// the monitor's top-left, post DPI conversion). The monitor's HMONITOR
/// identifies which display the region lives on; pair with
/// <c>GraphicsCaptureItem.CreateForMonitor</c> and a GPU subresource copy
/// to crop down to just this region.
/// </summary>
public readonly record struct SelectedRegion(IntPtr Monitor, int X, int Y, int Width, int Height)
{
    public int Right  => X + Width;
    public int Bottom => Y + Height;
}

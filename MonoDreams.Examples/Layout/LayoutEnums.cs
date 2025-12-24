namespace MonoDreams.Examples.Layout;

/// <summary>
/// Direction of layout flow (similar to Figma's auto layout direction).
/// </summary>
public enum LayoutDirection
{
    /// <summary>
    /// Children are arranged horizontally from left to right.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Children are arranged vertically from top to bottom.
    /// </summary>
    Vertical
}

/// <summary>
/// Alignment along the main axis (direction of layout flow).
/// Similar to CSS justify-content / Figma's primary axis alignment.
/// </summary>
public enum MainAxisAlignment
{
    /// <summary>
    /// Children are packed at the start of the container.
    /// </summary>
    Start,

    /// <summary>
    /// Children are centered in the container.
    /// </summary>
    Center,

    /// <summary>
    /// Children are packed at the end of the container.
    /// </summary>
    End,

    /// <summary>
    /// Children are evenly distributed with equal space between them.
    /// </summary>
    SpaceBetween,

    /// <summary>
    /// Children are evenly distributed with equal space around them.
    /// </summary>
    SpaceAround,

    /// <summary>
    /// Children are evenly distributed with equal space between and around them.
    /// </summary>
    SpaceEvenly
}

/// <summary>
/// Alignment along the cross axis (perpendicular to layout flow).
/// Similar to CSS align-items / Figma's counter axis alignment.
/// </summary>
public enum CrossAxisAlignment
{
    /// <summary>
    /// Children are aligned at the start of the cross axis.
    /// </summary>
    Start,

    /// <summary>
    /// Children are centered along the cross axis.
    /// </summary>
    Center,

    /// <summary>
    /// Children are aligned at the end of the cross axis.
    /// </summary>
    End,

    /// <summary>
    /// Children stretch to fill the cross axis.
    /// </summary>
    Stretch
}

/// <summary>
/// Sizing mode for container dimensions.
/// Similar to Figma's "Hug contents" vs "Fill container" vs "Fixed".
/// </summary>
public enum SizingMode
{
    /// <summary>
    /// Size to fit the content (auto in CSS/Yoga).
    /// </summary>
    HugContents,

    /// <summary>
    /// Expand to fill available space in parent (flex-grow: 1).
    /// </summary>
    FillContainer,

    /// <summary>
    /// Use a fixed pixel size.
    /// </summary>
    Fixed
}

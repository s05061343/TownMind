using AgePilot.Core.Planning;

namespace AgePilot.Vision.Geometry;

public static class MouseCoordinateMapper
{
    public static bool TryResolve(VisualToolAction action, NormalizedRect minimap, NormalizedRect commandGrid,
        int gridRows, int gridColumns, out double x, out double y, out string status)
    {
        x = y = 0;
        if (action.Space == VisualCoordinateSpace.CommandGrid)
        {
            if (action.Tool != VisualToolKind.LeftClick || gridRows <= 0 || gridColumns <= 0 ||
                action.Row < 1 || action.Row > gridRows || action.Column < 1 || action.Column > gridColumns)
            { status = "命令面板格位無效"; return false; }
            x = commandGrid.X + commandGrid.Width * (action.Column - 0.5) / gridColumns;
            y = commandGrid.Y + commandGrid.Height * (action.Row - 0.5) / gridRows;
        }
        else
        {
            if (action.X is < 0 or > 1 || action.Y is < 0 or > 1)
            { status = "滑鼠座標超出 normalized 範圍"; return false; }
            if (action.Space == VisualCoordinateSpace.Minimap)
            {
                if (action.Tool == VisualToolKind.Drag) { status = "小地圖不允許拖曳"; return false; }
                x = minimap.X + minimap.Width * action.X;
                y = minimap.Y + minimap.Height * action.Y;
            }
            else { x = action.X; y = action.Y; }
        }
        status = "座標已解析";
        return x is >= 0.01 and <= 0.99 && y is >= 0.065 and <= 0.99;
    }
}

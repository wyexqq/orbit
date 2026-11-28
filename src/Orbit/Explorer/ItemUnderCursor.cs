using System.Windows.Automation;
using Orbit.Core;
using Orbit.Interop;

namespace Orbit.Explorer;

internal static class ItemUnderCursor
{
    private const int AncestorsToCheck = 3;
    private const string NameAutomationId = "System.ItemNameDisplay";
    private const double LabelBelowIconShare = 0.35;

    public static Task<NativeRect?> FindFileBoxAsync(NativePoint press)
    {
        return Task.Run(() => FindFileBox(press));
    }

    private static NativeRect? FindFileBox(NativePoint press)
    {
        var desktopItem = DesktopIcons.ItemRectAt(press);
        if (desktopItem is not null)
        {
            return desktopItem;
        }
        try
        {
            var item = FileItemAt(press);
            return item is null ? null : FileBoxOf(item, press);
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Log.Error("UI Automation не ответила", error);
            return null;
        }
    }

    private static AutomationElement? FileItemAt(NativePoint press)
    {
        var element = AutomationElement.FromPoint(new System.Windows.Point(press.X, press.Y));
        var walker = TreeWalker.ControlViewWalker;
        for (var depth = 0; element is not null && depth <= AncestorsToCheck; depth++)
        {
            var controlType = element.Current.ControlType;
            if (controlType == ControlType.ListItem || controlType == ControlType.DataItem)
            {
                return element;
            }
            if (controlType == ControlType.List || controlType == ControlType.ScrollBar || controlType == ControlType.Header)
            {
                return null;
            }
            element = walker.GetParent(element);
        }
        return null;
    }

    private static NativeRect FileBoxOf(AutomationElement item, NativePoint press)
    {
        var itemBounds = item.Current.BoundingRectangle;
        if (itemBounds.IsEmpty || itemBounds.Width <= 0 || itemBounds.Height <= 0)
        {
            return SquareAround(press, 0);
        }
        if (NameBoundsOf(item) is not System.Windows.Rect label)
        {
            return ToNativeRect(itemBounds);
        }
        var labelIsBelowIcon = label.Top - itemBounds.Top > itemBounds.Height * LabelBelowIconShare;
        if (labelIsBelowIcon)
        {
            return ToNativeRect(itemBounds);
        }
        var rowMiddle = (int)Math.Round(itemBounds.Top + itemBounds.Height / 2);
        return SquareAround(new NativePoint(press.X, rowMiddle), itemBounds.Height);
    }

    private static NativeRect SquareAround(NativePoint center, double side)
    {
        var half = (int)Math.Round(side / 2);
        return new NativeRect
        {
            Left = center.X - half,
            Top = center.Y - half,
            Right = center.X + half,
            Bottom = center.Y + half,
        };
    }

    private static NativeRect ToNativeRect(System.Windows.Rect bounds)
    {
        return new NativeRect
        {
            Left = (int)Math.Round(bounds.Left),
            Top = (int)Math.Round(bounds.Top),
            Right = (int)Math.Round(bounds.Right),
            Bottom = (int)Math.Round(bounds.Bottom),
        };
    }

    private static System.Windows.Rect? NameBoundsOf(AutomationElement item)
    {
        var byId = new PropertyCondition(AutomationElement.AutomationIdProperty, NameAutomationId);
        var nameElement = item.FindFirst(TreeScope.Children, byId)
            ?? item.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit))
            ?? item.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
        if (nameElement is null)
        {
            return null;
        }
        var bounds = nameElement.Current.BoundingRectangle;
        return bounds.IsEmpty || bounds.Height <= 0 ? null : bounds;
    }
}

using System.Windows.Automation;
using Orbit.Core;
using Orbit.Interop;

namespace Orbit.Explorer;

internal static class ItemUnderCursor
{
    private const int AncestorsToCheck = 3;
    private const string NameAutomationId = "System.ItemNameDisplay";
    private const double LabelBelowIconShare = 0.35;

    public static Task<NativePoint?> FindCenterAsync(NativePoint press)
    {
        return Task.Run(() => FindCenter(press));
    }

    private static NativePoint? FindCenter(NativePoint press)
    {
        var desktopIcon = DesktopIcons.IconRectAt(press);
        if (desktopIcon is NativeRect icon)
        {
            return new NativePoint(icon.Left + icon.Width / 2, icon.Top + icon.Height / 2);
        }
        try
        {
            var item = FileItemAt(press);
            return item is null ? null : CenterOf(item, press);
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

    private static NativePoint CenterOf(AutomationElement item, NativePoint press)
    {
        var itemBounds = item.Current.BoundingRectangle;
        if (itemBounds.IsEmpty || itemBounds.Width <= 0 || itemBounds.Height <= 0)
        {
            return press;
        }
        var centerX = (int)Math.Round(itemBounds.Left + itemBounds.Width / 2);
        var centerY = (int)Math.Round(itemBounds.Top + itemBounds.Height / 2);
        var nameBounds = NameBoundsOf(item);
        if (nameBounds is not System.Windows.Rect label)
        {
            return new NativePoint(centerX, centerY);
        }
        var labelIsBelowIcon = label.Top - itemBounds.Top > itemBounds.Height * LabelBelowIconShare;
        if (labelIsBelowIcon)
        {
            var iconMiddleY = (int)Math.Round((itemBounds.Top + label.Top) / 2);
            return new NativePoint(centerX, iconMiddleY);
        }
        return new NativePoint(press.X, centerY);
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace QuickHasteBot.UI;

/// <summary>
/// Kontextmenüs rechts unter dem Mauszeiger. Mit <see cref="PlacementMode.MousePoint"/> folgt WPF der Händigkeit aus
/// den Tablet-Einstellungen von Windows (<see cref="SystemParameters.MenuDropAlignment"/>): bei "Rechtshänder" geht das
/// Menü nach links auf, im Popup also über die linke Spalte, und ein Klick daneben landet in einem anderen Ordner.
/// </summary>
internal static class MenuPlacement
{
    /// <summary>
    /// Öffnet das Menü mit der linken oberen Ecke am Mauszeiger. Passt es rechts oder unten nicht auf den Bildschirm,
    /// geht es nach links bzw. oben auf, wie in Windows sonst auch.
    /// </summary>
    public static void OpenAtMouse(ContextMenu menu, UIElement target)
    {
        // Jetzt merken: bis das Menü steht, kann die Maus schon weiter sein
        var mouse = Mouse.GetPosition(target);
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popupSize, _, _) =>
        {
            // Das gemessene Menü in geräteunabhängigen Pixeln, wie die Punkte; popupSize nur, falls es noch keins gibt
            var size = menu.RenderSize is { Width: > 0, Height: > 0 } measured ? measured : popupSize;
            var left = mouse.X - size.Width;
            var up = mouse.Y - size.Height;
            return
            [
                new(mouse, PopupPrimaryAxis.Horizontal),
                new(new Point(left, mouse.Y), PopupPrimaryAxis.Horizontal),
                new(new Point(mouse.X, up), PopupPrimaryAxis.Horizontal),
                new(new Point(left, up), PopupPrimaryAxis.Horizontal),
            ];
        };
        menu.IsOpen = true;
    }
}

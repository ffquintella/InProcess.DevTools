using Avalonia.Input;
using Avalonia.Input.Raw;

namespace InProcess.DevTools
{
    internal static class KeyGestureExtesions
    {
        public static bool Matches(this KeyGesture gesture, KeyEventArgs keyEvent) =>
            keyEvent.KeyModifiers == gesture.KeyModifiers &&
                ResolveNumPadOperationKey(keyEvent.Key) == ResolveNumPadOperationKey(gesture.Key);

        private static Key ResolveNumPadOperationKey(Key key)
        {
            switch (key)
            {
                case Key.Add:
                    return Key.OemPlus;
                case Key.Subtract:
                    return Key.OemMinus;
                case Key.Decimal:
                    return Key.OemPeriod;
                default:
                    return key;
            }
        }
    }
}

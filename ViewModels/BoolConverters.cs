using Avalonia.Data.Converters;
using Avalonia.Media;

namespace NavigatorEditor.Converters
{
    public static class BoolConverters
    {
        public static readonly IValueConverter TrueToGreenFalseToAmber =
            new FuncValueConverter<bool, IBrush>(b =>
                b ? new SolidColorBrush(Color.FromUInt32(0xFF2E7D32))   // 绿
                    : new SolidColorBrush(Color.FromUInt32(0xFFFFA000))); // 琥珀
    }
}

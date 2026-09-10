using System.Windows;
using System.Windows.Controls;

namespace WinSync
{
    /// <summary>One mirrored-output card: numbered badge, enable toggle, device
    /// picker, delay + volume sliders, live status. WPF port of the old WinForms
    /// OutputRow — same public surface (EnableToggle, DeviceCombo, DelaySlider,
    /// VolumeSlider, StatusText) so MainWindow's code-behind wires it up the same
    /// way the old MainForm did.</summary>
    public partial class OutputRowControl : UserControl
    {
        public static readonly DependencyProperty NumberProperty =
            DependencyProperty.Register(nameof(Number), typeof(string), typeof(OutputRowControl),
                new PropertyMetadata("", OnNumberChanged));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(OutputRowControl),
                new PropertyMetadata("", OnTitleChanged));

        public string Number
        {
            get => (string)GetValue(NumberProperty);
            set => SetValue(NumberProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public OutputRowControl()
        {
            InitializeComponent();

            DelaySlider.ValueChanged += (s, e) => DelayValueText.Text = $"{(int)DelaySlider.Value} ms";
            VolumeSlider.ValueChanged += (s, e) => VolumeValueText.Text = $"{(int)VolumeSlider.Value}%";
        }

        private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((OutputRowControl)d).BadgeText.Text = (string)e.NewValue;

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((OutputRowControl)d).TitleText.Text = (string)e.NewValue;
    }
}

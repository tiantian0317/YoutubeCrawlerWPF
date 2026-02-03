using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoutubeCrawlerWPF
{
    public partial class DetailViewerWindow : Window
    {
        private readonly Dictionary<string, object> _data;

        public DetailViewerWindow(Dictionary<string, object> data, string title)
        {
            InitializeComponent();
            _data = data;
            txtTitle.Text = $"📋 {title}";
            LoadDetails();
        }

        private void LoadDetails()
        {
            spDetails.Children.Clear();

            foreach (var item in _data.OrderBy(x => x.Key))
            {
                var key = item.Key;
                var value = item.Value?.ToString() ?? "(空)";

                // 创建数据项容器
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(221, 221, 221)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 5, 0, 5),
                    Background = new SolidColorBrush(Color.FromRgb(250, 250, 250))
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // 字段名
                var txtKey = new TextBlock
                {
                    Text = key,
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(33, 33, 33)),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(txtKey, 0);

                // 分隔符
                var txtSeparator = new TextBlock
                {
                    Text = ":",
                    FontSize = 13,
                    Margin = new Thickness(5, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(txtSeparator, 1);

                // 字段值
                var txtValue = new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66)),
                    VerticalAlignment = VerticalAlignment.Top
                };

                // 根据字段类型设置不同的颜色
                if (key.Contains("Id") || key.Contains("ID"))
                {
                    txtValue.Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 136)); // 青色 - ID
                    txtValue.FontFamily = new FontFamily("Consolas");
                }
                else if (key.Contains("Count") || key.Contains("Time") || key.Contains("Date"))
                {
                    txtValue.Foreground = new SolidColorBrush(Color.FromRgb(63, 81, 181)); // 靛蓝 - 数字/时间
                }
                else if (key.Contains("Status"))
                {
                    txtValue.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色 - 状态
                    txtValue.FontWeight = FontWeights.SemiBold;
                }
                else if (key.Contains("Error") || key.Contains("error"))
                {
                    txtValue.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // 红色 - 错误
                }

                Grid.SetColumn(txtValue, 2);

                grid.Children.Add(txtKey);
                grid.Children.Add(txtSeparator);
                grid.Children.Add(txtValue);

                border.Child = grid;
                spDetails.Children.Add(border);
            }

            // 添加总计信息
            var summaryBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10, 0, 0),
                CornerRadius = new CornerRadius(5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(129, 199, 132)),
                BorderThickness = new Thickness(1)
            };

            var summaryText = new TextBlock
            {
                Text = $"📊 总计: {_data.Count} 个字段",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32)),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            summaryBorder.Child = summaryText;
            spDetails.Children.Add(summaryBorder);
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"数据详情 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine(new string('=', 50));
                sb.AppendLine();

                foreach (var item in _data.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"{item.Key}: {item.Value}");
                }

                sb.AppendLine();
                sb.AppendLine($"总计: {_data.Count} 个字段");

                Clipboard.SetText(sb.ToString());
                MessageBox.Show("✅ 已复制到剪贴板", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ 复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

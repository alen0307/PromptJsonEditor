using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace JsonArrayEditor
{
    public class LineItem : INotifyPropertyChanged
    {
        private string _text = "";
        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(nameof(Text)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    public class ArrayItem : INotifyPropertyChanged
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(nameof(Key)); }
        }

        // 换成 LineItem
        public ObservableCollection<LineItem> Values { get; }
            = new ObservableCollection<LineItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public class TextLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => "删(" + (value?.ToString()?.Length ?? 0) + ")";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    public class RootModel
    {
        public ObservableCollection<ArrayItem> Groups { get; }
            = new ObservableCollection<ArrayItem>();
    }
    public partial class MainWindow : Window
    {
        private string _filePath;
        private readonly RootModel _model = new RootModel();
        public ObservableCollection<LineItem> Values { get; }
    = new ObservableCollection<LineItem>();
        public MainWindow()
        {
            InitializeComponent();
            ItemsHost.ItemsSource = _model.Groups;

            //BtnOpen.Click += (s, e) => OpenFile();
            //BtnSave.Click += (s, e) => SaveFile();
            //BtnAddGroup.Click += (s, e) =>
            //{
            ////    string name = Microsoft.VisualBasic.Interaction.InputBox("新分组名:", "新增");
            ////    if (!string.IsNullOrWhiteSpace(name))
            //        _model.Groups.Add(new ArrayItem { Key = name });
            //};
        }
        // 记录当前正在编辑的 TextBox
        private TextBox _lastActiveTextBox;
        //---- 1) 启动后自动加载 ----
        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            string last = Properties.Settings.Default.LastOpenedFile;
            if (!string.IsNullOrEmpty(last) && File.Exists(last))
            {
                _filePath = last;          // 让 Save 时不用再弹框
                OpenFile(_filePath);       // 直接用现有方法加载
            }
        }

        //---- 2) 把原来的无参 OpenFile() 拆出一个带路径的重载 ----
        private void OpenFile()   // 原来按钮调用的
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() != true) return;
            //label_info.Content = dlg.FileName;
            OpenFile(dlg.FileName);      // 统一走下面这个重载
        }

        private void OpenFile(string path)   // 新增重载
        {
            _filePath = path;
            string json = File.ReadAllText(_filePath);

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                _model.Groups.Clear();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var item = new ArrayItem { Key = prop.Name };
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        foreach (var v in prop.Value.EnumerateArray())
                            item.Values.Add(new LineItem { Text = v.GetString() });
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                        item.Values.Add(new LineItem { Text = prop.Value.GetString() });

                    _model.Groups.Add(item);
                }
                label_info.Content = _filePath;
                Properties.Settings.Default.LastOpenedFile = _filePath;
                Properties.Settings.Default.Save();
            }
        }

        //---- 3) 保存成功后记住路径 ----
        private void SaveFile()
        {
            if (_filePath == null) return;

            var dict = new Dictionary<string, object>();
            foreach (var g in _model.Groups)
                dict[g.Key] = g.Values.Select(li => li.Text).ToList();

            string json = JsonSerializer.Serialize(dict,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            File.WriteAllText(_filePath, json);

            // 记住路径
            Properties.Settings.Default.LastOpenedFile = _filePath;
            Properties.Settings.Default.Save();

            MessageBox.Show("已保存");
        }
        // 左边任何一只 TextBox 获得焦点
        private void SmallTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            _lastActiveTextBox = tb;
            BigEditBox.Text = tb.Text;          // 送进大编辑池
            BigEditBox.SelectAll();             // 方便直接重写
        }
        
        // 在大编辑池按 Enter
        private void BigEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;               // 阻止换行

                if (_lastActiveTextBox == null) return;

                _lastActiveTextBox.Text = BigEditBox.Text; // 写回
                _lastActiveTextBox.Focus();                // 回到原框
                _lastActiveTextBox.SelectAll();            // 可选：全选
            }
        }
        private void DelLine_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var line = (LineItem)btn.Tag;          // 现在是 LineItem
            var group = (ArrayItem)((ListBox)FindParent<ListBox>(btn)).DataContext;
            group.Values.Remove(line);
        }
        private void BtnOpen_Click(object sender, RoutedEventArgs e) => OpenFile();
        private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveFile();
        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("新分组名:", "新增");
            if (string.IsNullOrWhiteSpace(name)) return;

            var newGroup = new ArrayItem { Key = name };
            newGroup.Values.Add(new LineItem { Text = "" });
            // 默认一行空值
            _model.Groups.Add(newGroup);
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            //var tb = (TextBox)sender;
            //// 获取绑定表达式
            //var be = tb.GetBindingExpression(TextBox.TextProperty);
            //// 立即把用户输入写回源
            //be?.UpdateSource();
            if (_lastActiveTextBox == null) return;

            _lastActiveTextBox.Text = BigEditBox.Text; // 写回
            _lastActiveTextBox.Focus();                // 回到原框
            _lastActiveTextBox.SelectAll();
        }
        // 找父级
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject o = VisualTreeHelper.GetParent(child);
            if (o == null) return null;
            if (o is T t) return t;
            return FindParent<T>(o);
        }

        // 找子级（用于 TextBox 聚焦）
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                t = FindVisualChild<T>(child);
                if (t != null) return t;
            }
            return null;
        }

        //private void OpenFile()
        //{
        //    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        //    if (dlg.ShowDialog() != true) return;

        //    _filePath = dlg.FileName;
        //    string json = File.ReadAllText(_filePath);

        //    // 传统 using 语句，C# 7.3 可用
        //    using (JsonDocument doc = JsonDocument.Parse(json))
        //    {
        //        _model.Groups.Clear();

        //        foreach (var prop in doc.RootElement.EnumerateObject())
        //        {
        //            var item = new ArrayItem { Key = prop.Name };

        //            // 读
        //            if (prop.Value.ValueKind == JsonValueKind.Array)
        //                foreach (var v in prop.Value.EnumerateArray())
        //                    item.Values.Add(new LineItem { Text = v.GetString() });
        //            else if (prop.Value.ValueKind == JsonValueKind.String)
        //                item.Values.Add(new LineItem { Text = prop.Value.GetString() });


        //            _model.Groups.Add(item);
        //        }
        //    }   // doc 在这里释放
   
        //}

        //private void SaveFile()
        //{
        //    if (_filePath == null) return;

        //    var dict = new Dictionary<string, object>();
        //    foreach (var g in _model.Groups)
        //    {

        //        dict[g.Key] = g.Values.Select(li => li.Text).ToList();
        //    }

        //    string json = JsonSerializer.Serialize(dict,
        //        new JsonSerializerOptions
        //        {
        //            WriteIndented = true,
        //            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        //        });
        //    File.WriteAllText(_filePath, json);
        //    MessageBox.Show("已保存");
        //}


        // 删除整组
        private void DelGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = (ArrayItem)((Button)sender).Tag;
            if (MessageBox.Show($"确定删除分组【{group.Key}】？",
                                "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                _model.Groups.Remove(group);
        }
        // 把当前分组名包装成 {Key} 插入到大编辑框的光标处
        private void InsertTag_Click(object sender, RoutedEventArgs e)
        {
            var group = (ArrayItem)((Button)sender).Tag;   // 取到当前分组
            string tag = $"{{{group.Key}}}";               // 包装成 {Key}

            int caret = BigEditBox.CaretIndex;             // 当前光标
            BigEditBox.Text = BigEditBox.Text.Insert(caret, tag+",");
            BigEditBox.CaretIndex = caret + tag.Length;    // 把光标移到插入内容之后
            BigEditBox.Focus();                            // 可选：让大编辑框继续保有焦点
        }
        // 上移分组
        private void MoveGroupUp_Click(object sender, RoutedEventArgs e)
        {
            var group = (ArrayItem)((Button)sender).Tag;
            int idx = _model.Groups.IndexOf(group);
            if (idx > 0)          // 不是第一个
            {
                _model.Groups.Move(idx, idx - 1);   // ObservableCollection 自带 Move
            }
        }

        // 下移分组
        private void MoveGroupDown_Click(object sender, RoutedEventArgs e)
        {
            var group = (ArrayItem)((Button)sender).Tag;
            int idx = _model.Groups.IndexOf(group);
            if (idx < _model.Groups.Count - 1)      // 不是最后一个
            {
                _model.Groups.Move(idx, idx + 1);
            }
        }
        // 新增一行（原来就有，小改：自动聚焦）
        private void AddLine_Click(object sender, RoutedEventArgs e)
        {
            var group = (ArrayItem)((Button)sender).Tag;
            group.Values.Add(new LineItem { Text = "" });

            // 异步等 ListBox 生成容器
            Dispatcher.BeginInvoke((Action)(() =>
            {
                var lb = (ListBox)FindParent<ListBox>(sender as DependencyObject);
                if (lb == null) return;

                int idx = group.Values.Count - 1;
                lb.SelectedIndex = idx;

                var container = lb.ItemContainerGenerator.ContainerFromIndex(idx) as ListBoxItem;
                if (container == null) return;   // 保险

                var tb = FindVisualChild<TextBox>(container);
                tb?.Focus();
            }), DispatcherPriority.Render);
        }

    }
}
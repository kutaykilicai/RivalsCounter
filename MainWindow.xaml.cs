using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;

namespace RivalsCounter
{
    public partial class MainWindow : Window
    {
        enum Mode { Enemy, Team, Me }
        Mode mode = Mode.Enemy;
        readonly List<int> enemies = new(), allies = new();
        int? me, struggling;
        string detectedMap;
        bool dark, busy;
        Brush Fg, Sub, Card, Card2, Line;
        readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        readonly Dictionary<int, ImageSource> img = new();
        readonly Dictionary<Mode, Button> modeBtns = new();
        ToggleButton topBtn;
        static readonly string[] Roles = { "Vanguard", "Duelist", "Strategist" };

        public MainWindow()
        {
            InitializeComponent();
            Config.Load();
            Engine.Load();
            ApplyTheme();
            BuildBars();
            RenderAll();
            PatchText.Text = $"{Engine.D.patch} · meta {Engine.D.tierDate}";
            MetaSub.Text = $"Season 10.5 · {Engine.D.tierDate} · kaynak: marvelrivals.gg + rivalsmeta.com";
            Status.Text = "Ctrl+V yapıştır · Ctrl+Shift+S ekranı yakala · Ctrl+Shift+R göster/gizle · Ctrl+N yeni maç · oyuna bağlanmaz, sadece ekran görüntüsü.";

            Search.TextChanged += (s, e) => { SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed; };
            Search.KeyDown += Search_KeyDown;
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewTextInput += (s, e) => { if (!Search.IsKeyboardFocused && e.Text.Length > 0 && char.IsLetterOrDigit(e.Text[0])) Search.Focus(); };

            DropZone.Drop += DropZone_Drop;
            DropZone.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            DropZone.MouseLeftButtonUp += async (s, e) => await CaptureAndAnalyze();
            BtnCapture.Click += async (s, e) => await CaptureAndAnalyze();
            CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);

            SourceInitialized += OnSourceInitialized;
            if (Environment.GetCommandLineArgs().Contains("--demo"))
            {
                foreach (var d in new[] { 1049, 1024, 1053, 1037, 1058, 1054 }) AddHero(d, Mode.Enemy);
                struggling = 1049; AddHero(1031, Mode.Me); AddHero(1022, Mode.Team); AddHero(1051, Mode.Team);
                detectedMap = "Shin-Shibuya";
                RenderAll();
            }
            SystemEvents.UserPreferenceChanged += (s, e) => Dispatcher.Invoke(() => { ApplyTheme(); BuildBars(); RenderAll(); SetBackdrop(); });
        }

        void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste) { e.Handled = true; _ = PasteClipboard(); }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

        void OnSourceInitialized(object s, EventArgs e)
        {
            SetBackdrop();
            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src.AddHook(WndProc);
            RegisterHotKey(src.Handle, 1, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.R)); // Ctrl+Shift+R
            RegisterHotKey(src.Handle, 2, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.S)); // Ctrl+Shift+S
            Closed += (_, __) => { UnregisterHotKey(src.Handle, 1); UnregisterHotKey(src.Handle, 2); };
        }

        void SetBackdrop()
        {
            var h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
            int d = dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref d, 4);
            int corner = 2; DwmSetWindowAttribute(h, 33, ref corner, 4);
            int mica = 2;
            bool ok = Environment.OSVersion.Version.Build >= 22000 && DwmSetWindowAttribute(h, 38, ref mica, 4) == 0;
            var src = HwndSource.FromHwnd(h);
            if (ok) { src.CompositionTarget.BackgroundColor = Colors.Transparent; Background = Brushes.Transparent; }
            else Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF2, 0xF2, 0xF7));
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312)
            {
                int id = w.ToInt32();
                if (id == 1)
                {
                    if (IsVisible && WindowState != WindowState.Minimized && IsActive) Hide();
                    else { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
                    handled = true;
                }
                else if (id == 2)
                {
                    _ = CaptureAndAnalyze();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        void ApplyTheme()
        {
            try { dark = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 0; } catch { dark = true; }
            Fg = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1E));
            Sub = new SolidColorBrush(dark ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0, 0, 0));
            Card = new SolidColorBrush(dark ? Color.FromArgb(0x38, 0x28, 0x28, 0x2C) : Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            Card2 = new SolidColorBrush(dark ? Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x12, 0, 0, 0));
            Line = new SolidColorBrush(dark ? Color.FromArgb(0x40, 0x0A, 0x84, 0xFF) : Color.FromArgb(0x66, 0x0A, 0x84, 0xFF));
            Foreground = Fg; TitleText.Foreground = Fg; DropTitle.Foreground = Fg; DropHint.Foreground = Sub;
            Search.Foreground = Fg; Search.CaretBrush = Fg; SearchHint.Foreground = Sub;
            SearchBorder.Background = Card2; TeamsCard.Background = Card; RecCard.Background = Card; MetaCard.Background = Card;
            DropZone.Background = Card2; DropZone.BorderBrush = Line;
            StylePill(BtnCapture, true);
        }

        void StylePill(Button b, bool primary)
        {
            b.Template = PillTemplate();
            b.Background = primary ? Accent : Card2;
            b.Foreground = primary ? Brushes.White : Fg;
            b.FontSize = 13;
            if (b.Content is string t) b.Content = new TextBlock { Text = t, Foreground = primary ? Brushes.White : Fg };
        }

        Button Pill(string text, bool primary = false)
        {
            var b = new Button { Content = new TextBlock { Text = text, Foreground = primary ? Brushes.White : Fg, FontSize = 13 }, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand, Focusable = false, Template = PillTemplate(), Background = primary ? Accent : Card2 };
            return b;
        }
        static ControlTemplate PillTemplate()
        {
            var ct = new ControlTemplate(typeof(ButtonBase));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp); ct.VisualTree = bd; return ct;
        }

        void BuildBars()
        {
            ModeBar.Children.Clear(); modeBtns.Clear();
            foreach (var (m, t) in new[] { (Mode.Enemy, "Düşman"), (Mode.Team, "Takım"), (Mode.Me, "Ben") })
            {
                var b = Pill(t); b.Click += (s, e) => SetMode(m); modeBtns[m] = b; ModeBar.Children.Add(b);
            }
            SetMode(mode);
            RightBar.Children.Clear();
            var upd = Pill("Verileri güncelle");
            upd.Click += async (s, e) =>
            {
                upd.IsEnabled = false; Status.Text = "rivalsmeta.com / marvelrivals.gg güncelleniyor…";
                try
                {
                    Status.Text = "✓ " + await Engine.RefreshAsync();
                    PatchText.Text = $"{Engine.D.patch} · meta {Engine.D.tierDate}";
                    MetaSub.Text = $"Season · {Engine.D.tierDate} · {Engine.D.rivalsmetaStatsDate}";
                    RenderAll();
                }
                catch (Exception ex) { Status.Text = "Güncelleme başarısız: " + ex.Message; }
                upd.IsEnabled = true;
            };
            topBtn = new ToggleButton { Content = new TextBlock { Text = "Üstte", Foreground = Fg }, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6, 0, 0, 0), Template = PillTemplate(), Background = Card2, Focusable = false, IsChecked = Topmost, Cursor = Cursors.Hand };
            topBtn.Click += (s, e) => { Topmost = topBtn.IsChecked == true; topBtn.Background = Topmost ? Accent : Card2; ((TextBlock)topBtn.Content).Foreground = Topmost ? Brushes.White : Fg; };
            var clear = Pill("Yeni maç", true); clear.Click += (s, e) => ClearAll();
            foreach (var b in new FrameworkElement[] { upd, topBtn, clear }) { WindowChrome.SetIsHitTestVisibleInChrome(b, true); RightBar.Children.Add(b); }
            WindowChrome.SetIsHitTestVisibleInChrome(BtnCapture, true);
        }

        void SetMode(Mode m)
        {
            mode = m;
            foreach (var kv in modeBtns) { bool on = kv.Key == m; kv.Value.Background = on ? Accent : Card2; ((TextBlock)kv.Value.Content).Foreground = on ? Brushes.White : Fg; }
        }

        ImageSource Img(int id)
        {
            if (img.TryGetValue(id, out var cached)) return cached;
            foreach (var dir in new[] { Engine.UserIcons, Path.Combine(Engine.BaseDir, "Icons"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Icons") })
            {
                try
                {
                    var file = Path.GetFullPath(Path.Combine(dir, id + ".png"));
                    if (!File.Exists(file)) continue;
                    var b = new BitmapImage(); b.BeginInit();
                    b.UriSource = new Uri(file); b.DecodePixelWidth = 128; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                    return img[id] = b;
                }
                catch { }
            }
            return img[id] = Placeholder(id);
        }

        static ImageSource Placeholder(int id)
        {
            Engine.ById.TryGetValue(id, out var h);
            var name = h?.name ?? id.ToString();
            var color = (h?.role) switch
            {
                "Vanguard" => Color.FromRgb(0x3B, 0x82, 0xF6),
                "Duelist" => Color.FromRgb(0xEF, 0x44, 0x44),
                "Strategist" => Color.FromRgb(0x22, 0xC5, 0x5E),
                _ => Color.FromRgb(0x6B, 0x72, 0x80),
            };
            var words = name.Split(new[] { ' ', '-', '&' }, StringSplitOptions.RemoveEmptyEntries);
            var initials = words.Length >= 2 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}" : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
            var text = new FormattedText(initials, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 34, Brushes.White, 96);
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 96, 96), 16, 16);
                dc.DrawText(text, new Point((96 - text.Width) / 2, (96 - text.Height) / 2));
            }
            g.Freeze();
            var di = new DrawingImage(g); di.Freeze();
            return di;
        }

        static string Norm(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        List<Hero> Matches()
        {
            var q = Norm(Search.Text); if (q.Length == 0) return new();
            return Engine.D.heroes.Where(h => Norm(h.name).Contains(q) || h.name.Split(' ', '-', '(').Any(w => Norm(w).StartsWith(q)))
                .OrderBy(h => Norm(h.name).StartsWith(q) ? 0 : 1).ThenBy(h => h.name).ToList();
        }

        void Search_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var m = Matches();
                if (m.Count > 0)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { AddHero(m[0].id, Mode.Enemy); struggling = m[0].id; }
                    else AddHero(m[0].id, mode);
                    RenderAll();
                }
                Search.Clear(); e.Handled = true;
            }
            else if (e.Key == Key.Escape) { Search.Clear(); e.Handled = true; }
        }

        void Window_PreviewKeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = PasteClipboard(); }
            else if (e.Key == Key.F1) { SetMode(Mode.Enemy); e.Handled = true; }
            else if (e.Key == Key.F2) { SetMode(Mode.Team); e.Handled = true; }
            else if (e.Key == Key.F3) { SetMode(Mode.Me); e.Handled = true; }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { ClearAll(); e.Handled = true; }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(Search.Text))
            {
                var l = mode == Mode.Enemy ? enemies : allies;
                if (mode == Mode.Me) me = null;
                else if (l.Count > 0) { if (struggling == l[^1]) struggling = null; l.RemoveAt(l.Count - 1); }
                RenderAll(); e.Handled = true;
            }
        }

        async Task PasteClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var imgSrc = Clipboard.GetImage();
                    if (imgSrc != null) await AnalyzeBytes(ScreenCapture.BitmapSourceToPng(imgSrc));
                    return;
                }
                if (Clipboard.ContainsFileDropList())
                {
                    var f = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                    if (f != null) await AnalyzeBytes(File.ReadAllBytes(f));
                }
            }
            catch (Exception ex) { Status.Text = "Yapıştırma hatası: " + ex.Message; }
        }

        async void DropZone_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    var f = files?.FirstOrDefault();
                    if (f != null) await AnalyzeBytes(File.ReadAllBytes(f));
                }
                else if (e.Data.GetDataPresent(DataFormats.Bitmap))
                {
                    var bmp = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                    if (bmp != null) await AnalyzeBytes(ScreenCapture.BitmapSourceToPng(bmp));
                }
            }
            catch (Exception ex) { Status.Text = "Sürükleme hatası: " + ex.Message; }
        }

        async Task CaptureAndAnalyze()
        {
            if (busy) return;
            try
            {
                // Hide ourselves briefly so we don't capture our own window
                var wasTop = Topmost; var vis = Visibility;
                Topmost = false; Hide();
                await Task.Delay(180);
                byte[] png;
                try { png = ScreenCapture.CaptureVirtualScreenPng(); }
                finally { Show(); Topmost = wasTop; Activate(); }
                await AnalyzeBytes(png);
            }
            catch (Exception ex) { Show(); Status.Text = "Yakalama hatası: " + ex.Message; }
        }

        async Task AnalyzeBytes(byte[] png)
        {
            if (busy) return;
            busy = true;
            try
            {
                Thumb.Source = ScreenCapture.PngToImage(png);
                ThumbWrap.Visibility = Visibility.Visible;
                DropTitle.Text = "Görüntü analiz ediliyor…";
                Status.Text = "Görüntü modeli takımları okuyor…";
                var vr = await Vision.AnalyzeAsync(png);
                ApplyVision(vr);
                DropTitle.Text = "Ekran görüntüsü alındı — gerekirse chip'leri düzelt";
                Status.Text = $"Algı: {vr.screenType ?? "?"} · güven %{vr.confidence * 100:0} · {vr.note}".Trim(' ', '·');
                RenderAll();
                MainStack.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(160)));
            }
            catch (Exception ex)
            {
                Status.Text = "Analiz başarısız: " + ex.Message;
                DropTitle.Text = "Ekran görüntüsü yapıştır veya sürükle";
            }
            finally { busy = false; }
        }

        void ApplyVision(VisionResult vr)
        {
            enemies.Clear(); allies.Clear(); me = null; struggling = null; detectedMap = vr.map;
            foreach (var n in vr.enemies)
            {
                var id = Engine.IdOf(n); if (id == null || enemies.Contains(id.Value) || enemies.Count >= 6) continue;
                enemies.Add(id.Value);
            }
            var meId = Engine.IdOf(vr.me);
            if (meId != null) me = meId;
            foreach (var n in vr.allies)
            {
                var id = Engine.IdOf(n); if (id == null || id == me || allies.Contains(id.Value) || allies.Count >= 5) continue;
                allies.Add(id.Value);
            }
            if (enemies.Count > 0) struggling = enemies[0];
        }

        void AddHero(int id, Mode m)
        {
            if (m == Mode.Enemy) { if (enemies.Contains(id) || enemies.Count >= 6) return; enemies.Add(id); }
            else if (m == Mode.Me) { allies.Remove(id); me = id; }
            else { if (allies.Contains(id) || id == me || allies.Count >= 5) return; allies.Add(id); }
        }

        void ClearAll()
        {
            enemies.Clear(); allies.Clear(); me = null; struggling = null; detectedMap = null; Search.Clear();
            ThumbWrap.Visibility = Visibility.Collapsed; Thumb.Source = null;
            DropTitle.Text = "Ekran görüntüsü yapıştır veya sürükle";
            SetMode(Mode.Enemy); RenderAll();
        }

        void RenderAll()
        {
            RenderSlots(); RenderInsights(); RenderRecs(); RenderMeta();
        }

        void RenderSlots()
        {
            Slots.Children.Clear();
            Slots.Children.Add(ChipRow("Düşman", enemies, true));
            var mine = allies.ToList();
            var row = ChipRow("Takımım", mine, false);
            // prepend me chip
            var panel = (WrapPanel)((StackPanel)row).Children[1];
            panel.Children.Insert(0, HeroChip(me, false, true, () => { me = null; RenderAll(); }, null));
            Slots.Children.Add(row);
        }

        StackPanel ChipRow(string title, List<int> list, bool enemy)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, enemy ? 10 : 0) };
            var head = title;
            if (enemy && struggling != null && Engine.ById.ContainsKey(struggling.Value)) head += $"  ·  odak: {Engine.ById[struggling.Value].name}";
            sp.Children.Add(new TextBlock { Text = head, Foreground = enemy ? Danger : Accent, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6) });
            var wp = new WrapPanel();
            foreach (var id in list.ToList())
            {
                int hid = id;
                wp.Children.Add(HeroChip(hid, enemy && struggling == hid, false,
                    () => { if (struggling == hid) struggling = null; list.Remove(hid); if (!enemy) allies.Remove(hid); else enemies.Remove(hid); RenderAll(); },
                    enemy ? () => { struggling = struggling == hid ? null : hid; RenderAll(); } : null));
            }
            if (list.Count == 0) wp.Children.Add(new TextBlock { Text = enemy ? "Henüz düşman yok — görüntü yapıştır" : "Görüntüden veya aramayla ekle", Foreground = Sub, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 10, 0, 10) });
            sp.Children.Add(wp);
            return sp;
        }

        Border HeroChip(int? id, bool marked, bool isMe, Action remove, Action mark)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 6),
                Background = isMe ? new SolidColorBrush(Color.FromArgb(0x55, 0x0A, 0x84, 0xFF)) : Card2,
                BorderThickness = new Thickness(marked || isMe ? 2 : 0),
                BorderBrush = marked ? Danger : Accent,
                Cursor = id.HasValue ? Cursors.Hand : Cursors.Arrow
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (id.HasValue && Engine.ById.TryGetValue(id.Value, out var h))
            {
                sp.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = new ImageBrush(Img(id.Value)) { Stretch = Stretch.UniformToFill }, Margin = new Thickness(0, 0, 8, 0) });
                var name = new TextBlock { Text = (isMe ? "BEN · " : "") + h.name, Foreground = Fg, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
                sp.Children.Add(name);
                b.Child = sp;
                b.ToolTip = h.name + " · sol tık çıkar" + (mark != null ? " · sağ tık odak" : "");
                b.MouseLeftButtonUp += (s, e) => remove?.Invoke();
                if (mark != null) b.MouseRightButtonUp += (s, e) => mark();
            }
            else
            {
                sp.Children.Add(new TextBlock { Text = isMe ? "BEN · ?" : "+", Foreground = Sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                b.Child = sp;
            }
            return b;
        }

        void RenderInsights()
        {
            InsightPanel.Children.Clear();
            var insight = Engine.Analyze(enemies, allies, me, detectedMap);
            void AddBanner(string text, Brush accent)
            {
                var b = new Border { Background = Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(0, 0, 0, 0) };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(new Border { Background = accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 10, 2) });
                var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Fg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tb, 1); g.Children.Add(tb);
                b.Child = g; InsightPanel.Children.Add(b);
            }
            if (!string.IsNullOrEmpty(insight.MeVerdict)) AddBanner(insight.MeVerdict, insight.MeVerdict.Contains("zayıf") ? Danger : Good);
            if (!string.IsNullOrEmpty(insight.RoleLine)) AddBanner(insight.RoleLine, Accent);
            foreach (var w in insight.Warnings) AddBanner(w, Warn);
            foreach (var t in insight.TeamUps.Take(3)) AddBanner("Team-up · " + t, Accent);
            if (!string.IsNullOrEmpty(insight.MapTip)) AddBanner(insight.MapTip, Accent);
        }

        void RenderRecs()
        {
            Recs.Children.Clear();
            if (enemies.Count == 0)
            {
                Recs.Children.Add(new TextBlock { Text = "Karşı takım gelince burada rol rol en iyi counter'lar çıkar. Maçta Ctrl+Shift+S basman yeterli.", Foreground = Sub, TextWrapping = TextWrapping.Wrap });
                return;
            }
            var res = Engine.Recommend(enemies, struggling, allies, me, 4);
            foreach (var r in Roles)
            {
                if (!res.TryGetValue(r, out var list) || list.Count == 0) continue;
                Recs.Children.Add(new TextBlock { Text = Engine.RoleTr(r).ToUpperInvariant(), Foreground = Sub, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 8) });
                bool first = true;
                foreach (var rec in list)
                {
                    var card = new Border
                    {
                        Background = first ? new SolidColorBrush(Color.FromArgb(dark ? (byte)0x40 : (byte)0x30, 0x0A, 0x84, 0xFF)) : Card2,
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 0, 0, 8),
                        Cursor = Cursors.Hand
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.Children.Add(new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Top, Background = new ImageBrush(Img(rec.Hero.id)) { Stretch = Stretch.UniformToFill } });
                    var sp = new StackPanel(); Grid.SetColumn(sp, 1);
                    var head = new StackPanel { Orientation = Orientation.Horizontal };
                    head.Children.Add(new TextBlock { Text = rec.Hero.name, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Fg });
                    if (rec.Hero.tier != null)
                        head.Children.Add(new Border { Background = rec.Hero.tier is "S" or "A" ? Accent : Card, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 2, 0, 0), Child = new TextBlock { Text = rec.Hero.tier, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White } });
                    head.Children.Add(new TextBlock { Text = $"  {(rec.Score >= 0 ? "+" : "")}{rec.Score:0.0}", Foreground = Sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(head);
                    sp.Children.Add(new TextBlock { Text = rec.Reason, Foreground = Sub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                    grid.Children.Add(sp);
                    card.Child = grid;
                    int hid = rec.Hero.id;
                    card.MouseLeftButtonUp += (s, e) => { AddHero(hid, Mode.Me); RenderAll(); };
                    card.ToolTip = "Tıkla: bunu Ben olarak seç";
                    Recs.Children.Add(card);
                    first = false;
                }
            }
        }

        void RenderMeta()
        {
            MetaPanel.Children.Clear();
            var tierCol = new Dictionary<string, Color> { ["S"] = Color.FromRgb(0xFF, 0x45, 0x3A), ["A"] = Color.FromRgb(0xFF, 0x9F, 0x0A) };
            foreach (var role in Roles)
            {
                MetaPanel.Children.Add(new TextBlock { Text = Engine.RoleTr(role), FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 6, 0, 6) });
                var wp = new WrapPanel();
                foreach (var h in Engine.D.heroes.Where(h => h.role == role && (h.tier == "S" || h.tier == "A")).OrderBy(h => h.tier == "S" ? 0 : 1).ThenByDescending(h => h.winRate ?? 0).Take(6))
                {
                    var tile = new Border { Width = 92, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Background = Card2, Padding = new Thickness(6), Cursor = Cursors.Hand, ToolTip = $"{h.name} · {h.tier} · %{h.winRate:0.0} WR" };
                    var sp = new StackPanel();
                    sp.Children.Add(new Border { Width = 78, Height = 50, CornerRadius = new CornerRadius(8), Background = new ImageBrush(Img(h.id)) { Stretch = Stretch.UniformToFill } });
                    sp.Children.Add(new TextBlock { Text = h.name, FontSize = 10.5, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                    sp.Children.Add(new TextBlock { Text = $"{h.tier} · %{h.winRate:0.0}", FontSize = 10, Foreground = new SolidColorBrush(tierCol[h.tier]), TextAlignment = TextAlignment.Center });
                    tile.Child = sp;
                    int hid = h.id;
                    tile.MouseLeftButtonUp += (s, e) => { AddHero(hid, mode); RenderAll(); };
                    wp.Children.Add(tile);
                }
                MetaPanel.Children.Add(wp);
            }
        }
    }
}

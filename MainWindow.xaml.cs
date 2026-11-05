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
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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
        bool dark, busy, forceTheme;
        bool themeDark = true;
        readonly Dictionary<int, ImageSource> img = new();
        readonly Dictionary<Mode, Button> modeBtns = new();
        ToggleButton topBtn;
        static readonly string[] Roles = { "Vanguard", "Duelist", "Strategist" };

        Brush AccentB => (Brush)FindResource("Accent");
        Brush DangerB => (Brush)FindResource("Danger");
        Brush SuccessB => (Brush)FindResource("Success");
        Brush WarnB => (Brush)FindResource("Warning");
        Brush TextB => (Brush)FindResource("Text");
        Brush Text2 => (Brush)FindResource("TextSecondary");
        Brush Text3 => (Brush)FindResource("TextTertiary");
        Brush SurfaceAlt => (Brush)FindResource("SurfaceAlt");
        Brush SurfaceInset => (Brush)FindResource("SurfaceInset");
        Brush BorderB => (Brush)FindResource("BorderBrushKey");
        Brush PillBg => (Brush)FindResource("PillBg");
        Brush SelectedB => (Brush)FindResource("Selected");
        Brush Sep => (Brush)FindResource("Separator");

        public MainWindow()
        {
            InitializeComponent();
            Config.Load();
            Engine.Load();

            var args = Environment.GetCommandLineArgs();
            if (args.Contains("--light")) { forceTheme = true; themeDark = false; }
            if (args.Contains("--dark")) { forceTheme = true; themeDark = true; }

            ApplyTheme();
            BuildBars();
            RenderAll();
            PatchText.Text = ShortPatch(Engine.D.patch, Engine.D.tierDate);
            MetaSub.Text = "marvelrivals.gg · rivalsmeta.com";
            Status.Text = "Ctrl+V yapıştır · Ctrl+Shift+S yakala · Ctrl+Shift+R göster/gizle · Ctrl+N yeni maç";

            Search.TextChanged += (s, e) => SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
            Search.KeyDown += Search_KeyDown;
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewTextInput += (s, e) => { if (!Search.IsKeyboardFocused && e.Text.Length > 0 && char.IsLetterOrDigit(e.Text[0])) Search.Focus(); };

            DropZone.Drop += DropZone_Drop;
            DropZone.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            DropZone.MouseLeftButtonUp += async (s, e) => { if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) != null) return; await CaptureAndAnalyze(); };
            BtnCapture.Click += async (s, e) => await CaptureAndAnalyze();
            BtnRecapture.Click += async (s, e) => await CaptureAndAnalyze();
            CommandManager.AddPreviewExecutedHandler(this, OnPreviewExecuted);
            DrawDashedDrop();

            SourceInitialized += OnSourceInitialized;
            if (args.Contains("--demo"))
            {
                foreach (var d in new[] { 1049, 1024, 1053, 1037, 1058, 1054 }) AddHero(d, Mode.Enemy);
                struggling = 1049; AddHero(1031, Mode.Me); AddHero(1022, Mode.Team); AddHero(1051, Mode.Team);
                detectedMap = "Shin-Shibuya";
                ShowDropDone(null);
                RenderAll();
            }
            SystemEvents.UserPreferenceChanged += (s, e) => Dispatcher.Invoke(() => { if (!forceTheme) { ApplyTheme(); BuildBars(); RenderAll(); SetBackdrop(); } });
        }

        static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null) { if (d is T t) return t; d = VisualTreeHelper.GetParent(d); }
            return null;
        }

        static string ShortPatch(string patch, string tierDate)
        {
            var p = patch ?? "";
            if (p.Contains("10.5")) p = "Season 10.5";
            var d = tierDate ?? "";
            if (d.StartsWith("Nov", StringComparison.OrdinalIgnoreCase)) d = "4 Kas";
            return string.IsNullOrEmpty(d) ? p : $"{p} · {d}";
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
            RegisterHotKey(src.Handle, 1, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.R));
            RegisterHotKey(src.Handle, 2, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.S));
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
            else Background = (Brush)FindResource("WindowFallback");
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
                else if (id == 2) { _ = CaptureAndAnalyze(); handled = true; }
            }
            return IntPtr.Zero;
        }

        void ApplyTheme()
        {
            if (!forceTheme)
            {
                try { dark = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 0; }
                catch { dark = true; }
            }
            else dark = themeDark;

            var app = Application.Current.Resources.MergedDictionaries;
            // replace first theme dict
            var theme = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
            if (app.Count >= 1) app[0] = theme; else app.Insert(0, theme);
            if (app.Count < 2) app.Add(new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) });

            StylePill(BtnCapture, true);
            StylePill(BtnRecapture, false);
            DrawDashedDrop();
        }

        void DrawDashedDrop()
        {
            // subtle dashed outline via StrokeDashArray rectangle behind content when idle
            DropZone.BorderBrush = BorderB;
            DropZone.BorderThickness = new Thickness(1);
        }

        void StylePill(Button b, bool primary)
        {
            b.Template = PillTemplate(12);
            b.Background = primary ? AccentB : SurfaceAlt;
            b.Foreground = primary ? Brushes.White : TextB;
            b.BorderBrush = primary ? AccentB : BorderB;
            b.BorderThickness = new Thickness(primary ? 0 : 1);
            if (b.Content is string t)
                b.Content = new TextBlock { Text = t, Foreground = primary ? Brushes.White : TextB, FontSize = 13, FontWeight = FontWeights.SemiBold };
        }

        Button Pill(string text, bool primary = false, double radius = 999)
        {
            var tb = new TextBlock { Text = text, Foreground = primary ? Brushes.White : TextB, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            return new Button
            {
                Content = tb,
                Margin = new Thickness(0),
                Padding = new Thickness(14, 6, 14, 6),
                Cursor = Cursors.Hand,
                Focusable = false,
                Template = PillTemplate(radius),
                Background = primary ? AccentB : Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
        }

        static ControlTemplate PillTemplate(double radius)
        {
            var ct = new ControlTemplate(typeof(ButtonBase));
            var grid = new FrameworkElementFactory(typeof(Grid));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "b";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            grid.AppendChild(bd);
            ct.VisualTree = grid;
            return ct;
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
            var upd = Pill("Verileri güncelle", false, 10); upd.Margin = new Thickness(0, 0, 6, 0);
            upd.Padding = new Thickness(12, 7, 12, 7);
            upd.Background = SurfaceAlt; upd.BorderBrush = BorderB; upd.BorderThickness = new Thickness(1);
            ((TextBlock)upd.Content).Foreground = TextB;
            upd.Click += async (s, e) =>
            {
                upd.IsEnabled = false; Status.Text = "Veriler güncelleniyor…";
                try
                {
                    Status.Text = "✓ " + await Engine.RefreshAsync();
                    PatchText.Text = ShortPatch(Engine.D.patch, Engine.D.tierDate);
                    RenderAll();
                }
                catch (Exception ex) { Status.Text = "Güncelleme başarısız: " + ex.Message; }
                upd.IsEnabled = true;
            };

            topBtn = new ToggleButton
            {
                Content = new TextBlock { Text = "Üstte", Foreground = TextB, FontSize = 12.5, FontWeight = FontWeights.SemiBold },
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 6, 0),
                Template = PillTemplate(10), Background = SurfaceAlt, BorderBrush = BorderB, BorderThickness = new Thickness(1),
                Focusable = false, IsChecked = Topmost, Cursor = Cursors.Hand
            };
            topBtn.Click += (s, e) =>
            {
                Topmost = topBtn.IsChecked == true;
                topBtn.Background = Topmost ? AccentB : SurfaceAlt;
                topBtn.BorderBrush = Topmost ? AccentB : BorderB;
                ((TextBlock)topBtn.Content).Foreground = Topmost ? Brushes.White : TextB;
            };

            var clear = Pill("Yeni maç", true, 10); clear.Padding = new Thickness(14, 7, 14, 7);
            clear.Click += (s, e) => ClearAll();

            foreach (var b in new FrameworkElement[] { upd, topBtn, clear })
            {
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
                RightBar.Children.Add(b);
            }
            WindowChrome.SetIsHitTestVisibleInChrome(BtnCapture, true);
            WindowChrome.SetIsHitTestVisibleInChrome(BtnRecapture, true);
        }

        void SetMode(Mode m)
        {
            mode = m;
            foreach (var kv in modeBtns)
            {
                bool on = kv.Key == m;
                kv.Value.Background = on ? AccentB : Brushes.Transparent;
                ((TextBlock)kv.Value.Content).Foreground = on ? Brushes.White : Text2;
            }
        }

        ImageSource Img(int id)
        {
            if (img.TryGetValue(id, out var cached)) return cached;
            foreach (var dir in new[] { Engine.UserIcons, System.IO.Path.Combine(Engine.BaseDir, "Icons") })
            {
                try
                {
                    var file = System.IO.Path.Combine(dir, id + ".png");
                    if (!File.Exists(file)) continue;
                    var b = new BitmapImage(); b.BeginInit();
                    b.UriSource = new Uri(file); b.DecodePixelWidth = 160; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                    return img[id] = b;
                }
                catch { }
            }
            return img[id] = Placeholder(id);
        }

        ImageSource Placeholder(int id)
        {
            Engine.ById.TryGetValue(id, out var h);
            var name = h?.name ?? id.ToString();
            var color = RoleColor(h?.role);
            var words = name.Split(new[] { ' ', '-', '&' }, StringSplitOptions.RemoveEmptyEntries);
            var initials = words.Length >= 2 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}" : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
            var text = new FormattedText(initials, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 36, Brushes.White, 96);
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawEllipse(new SolidColorBrush(color), null, new Point(48, 48), 48, 48);
                dc.DrawText(text, new Point(48 - text.Width / 2, 48 - text.Height / 2));
            }
            g.Freeze();
            var di = new DrawingImage(g); di.Freeze();
            return di;
        }

        static Color RoleColor(string role) => role switch
        {
            "Vanguard" => Color.FromRgb(0x3B, 0x82, 0xF6),
            "Duelist" => Color.FromRgb(0xEF, 0x44, 0x44),
            "Strategist" => Color.FromRgb(0x22, 0xC5, 0x5E),
            _ => Color.FromRgb(0x6B, 0x72, 0x80),
        };

        Brush RoleBrush(string role) => role switch
        {
            "Vanguard" => (Brush)FindResource("RoleVanguard"),
            "Duelist" => (Brush)FindResource("RoleDuelist"),
            "Strategist" => (Brush)FindResource("RoleStrategist"),
            _ => Text3
        };

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
                    var f = Clipboard.GetFileDropList().Cast<string>().FirstOrDefault(p =>
                        p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
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
                    var f = ((string[])e.Data.GetData(DataFormats.FileDrop))?.FirstOrDefault();
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
                var wasTop = Topmost;
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
                ShowDropDone(ScreenCapture.PngToImage(png));
                DropDoneTitle.Text = "Görüntü analiz ediliyor…";
                Status.Text = "Takımlar okunuyor…";
                var vr = await Vision.AnalyzeAsync(png);
                ApplyVision(vr);
                DropDoneTitle.Text = "Görüntü alındı";
                Status.Text = $"{vr.screenType ?? "ekran"} · güven %{vr.confidence * 100:0}" + (string.IsNullOrEmpty(vr.note) ? "" : " · " + vr.note);
                RenderAll();
                FadeIn(MainStack);
            }
            catch (Exception ex)
            {
                Status.Text = "Analiz başarısız: " + ex.Message;
                DropDoneTitle.Text = "Analiz edilemedi — yeniden dene";
            }
            finally { busy = false; }
        }

        void ShowDropDone(ImageSource thumb)
        {
            DropIdle.Visibility = Visibility.Collapsed;
            DropDone.Visibility = Visibility.Visible;
            if (thumb != null) Thumb.Source = thumb;
            DropZone.MinHeight = 88;
            DropZone.Padding = new Thickness(16);
        }

        void ShowDropIdle()
        {
            DropIdle.Visibility = Visibility.Visible;
            DropDone.Visibility = Visibility.Collapsed;
            Thumb.Source = null;
            DropZone.MinHeight = 108;
            DropZone.Padding = new Thickness(22);
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
            ShowDropIdle(); SetMode(Mode.Enemy); RenderAll();
        }

        void RenderAll()
        {
            RenderSlots(); RenderInsights(); RenderRecs(); RenderMeta();
        }

        void FadeIn(UIElement el) => el.BeginAnimation(OpacityProperty, new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(180)));

        void RenderSlots()
        {
            Slots.Children.Clear();
            Slots.Children.Add(ChipRow("Düşman", enemies, true));
            var row = ChipRow("Takımım", allies.ToList(), false);
            var panel = (WrapPanel)((StackPanel)row).Children[1];
            panel.Children.Insert(0, HeroChip(me, false, true, () => { me = null; RenderAll(); }, null));
            Slots.Children.Add(row);
        }

        StackPanel ChipRow(string title, List<int> list, bool enemy)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, enemy ? 14 : 2) };
            var head = title.ToUpperInvariant();
            if (enemy && struggling != null && Engine.ById.ContainsKey(struggling.Value))
                head += $"  ·  odak {Engine.ById[struggling.Value].name}";
            sp.Children.Add(new TextBlock
            {
                Text = head,
                Foreground = enemy ? DangerB : AccentB,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 8),
                Opacity = 0.95
            });
            var wp = new WrapPanel();
            foreach (var id in list.ToList())
            {
                int hid = id;
                wp.Children.Add(HeroChip(hid, enemy && struggling == hid, false,
                    () => { if (struggling == hid) struggling = null; list.Remove(hid); if (!enemy) allies.Remove(hid); else enemies.Remove(hid); RenderAll(); },
                    enemy ? () => { struggling = struggling == hid ? null : hid; RenderAll(); } : null));
            }
            if (list.Count == 0 && !(enemy == false && me != null))
                wp.Children.Add(new TextBlock
                {
                    Text = enemy ? "Görüntü ekle veya ara" : "Takım arkadaşlarını ekle",
                    Foreground = Text3,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 12, 0, 12),
                    FontSize = 12.5
                });
            sp.Children.Add(wp);
            return sp;
        }

        Border HeroChip(int? id, bool marked, bool isMe, Action remove, Action mark)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(5, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 8),
                Background = isMe ? SelectedB : SurfaceAlt,
                BorderThickness = new Thickness(marked ? 1.5 : 1),
                BorderBrush = marked ? DangerB : (isMe ? AccentB : BorderB),
                Cursor = id.HasValue ? Cursors.Hand : Cursors.Arrow
            };
            if (marked)
                b.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 0, Color = Color.FromRgb(0xFF, 0x45, 0x3A), Opacity = 0.45 };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (id.HasValue && Engine.ById.TryGetValue(id.Value, out var h))
            {
                sp.Children.Add(RoundAvatar(id.Value, 30));
                if (isMe)
                {
                    sp.Children.Add(new Border
                    {
                        Background = AccentB, CornerRadius = new CornerRadius(999),
                        Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = "Sen", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold }
                    });
                }
                else sp.Children.Add(new FrameworkElement { Width = 8 });
                sp.Children.Add(new TextBlock
                {
                    Text = h.name,
                    Foreground = TextB,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13
                });
                b.Child = sp;
                b.ToolTip = h.name + " · sol tık çıkar" + (mark != null ? " · sağ tık odak" : "");
                b.MouseLeftButtonUp += (s, e) => remove?.Invoke();
                if (mark != null) b.MouseRightButtonUp += (s, e) => mark();
            }
            else
            {
                sp.Children.Add(new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                    Background = SurfaceInset, Margin = new Thickness(0, 0, 8, 0),
                    Child = new TextBlock { Text = isMe ? "?" : "+", Foreground = Text3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 }
                });
                sp.Children.Add(new TextBlock { Text = isMe ? "Sen" : "Boş", Foreground = Text3, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
                b.Child = sp;
            }
            return b;
        }

        Border RoundAvatar(int id, double size)
        {
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                ClipToBounds = true,
                Background = new ImageBrush(Img(id)) { Stretch = Stretch.UniformToFill }
            };
        }

        void RenderInsights()
        {
            InsightPanel.Children.Clear();
            var insight = Engine.Analyze(enemies, allies, me, detectedMap);
            var rows = new List<(string icon, string text, Brush accent)>();
            if (!string.IsNullOrEmpty(insight.MeVerdict))
                rows.Add(("\uE8FB", insight.MeVerdict, insight.MeVerdict.Contains("zayıf") ? DangerB : SuccessB));
            if (!string.IsNullOrEmpty(insight.RoleLine))
                rows.Add(("\uE716", insight.RoleLine, AccentB));
            foreach (var w in insight.Warnings)
                rows.Add(("\uE7BA", w, WarnB));
            foreach (var t in insight.TeamUps.Take(2))
                rows.Add(("\uE8F2", t, AccentB));
            if (!string.IsNullOrEmpty(insight.MapTip))
                rows.Add(("\uE707", insight.MapTip, AccentB));

            InsightCard.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < rows.Count; i++)
            {
                var (icon, text, accent) = rows[i];
                var row = new DockPanel { Margin = new Thickness(12, 10, 12, 10), LastChildFill = true };
                row.Children.Add(new TextBlock
                {
                    Text = icon, FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 15, Foreground = accent, Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center, Width = 20, TextAlignment = TextAlignment.Center
                });
                DockPanel.SetDock(row.Children[0], Dock.Left);
                row.Children.Add(new TextBlock
                {
                    Text = text, TextWrapping = TextWrapping.Wrap, Foreground = TextB,
                    FontSize = 13, VerticalAlignment = VerticalAlignment.Center
                });
                InsightPanel.Children.Add(row);
                if (i < rows.Count - 1)
                    InsightPanel.Children.Add(new Border { Height = 1, Background = Sep, Margin = new Thickness(44, 0, 12, 0) });
            }
        }

        void RenderRecs()
        {
            Recs.Children.Clear();
            if (enemies.Count == 0)
            {
                Recs.Children.Add(new TextBlock
                {
                    Text = "Karşı takım gelince burada en iyi counter’lar listelenir. Maçta Ctrl+Shift+S yeterli.",
                    Foreground = Text2, TextWrapping = TextWrapping.Wrap, LineHeight = 20
                });
                return;
            }
            var res = Engine.Recommend(enemies, struggling, allies, me, 4);
            foreach (var r in Roles)
            {
                if (!res.TryGetValue(r, out var list) || list.Count == 0) continue;
                var roleHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 8) };
                roleHead.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = RoleBrush(r), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                roleHead.Children.Add(new TextBlock { Text = Engine.RoleTr(r).ToUpperInvariant(), Foreground = Text3, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                Recs.Children.Add(roleHead);

                bool first = true;
                foreach (var rec in list)
                {
                    var card = new Border
                    {
                        Background = first ? SelectedB : SurfaceAlt,
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 0, 0, 8),
                        BorderBrush = first ? AccentB : BorderB,
                        BorderThickness = new Thickness(first ? 1.2 : 1),
                        Cursor = Cursors.Hand
                    };
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.Children.Add(RoundAvatar(rec.Hero.id, 52));

                    var sp = new StackPanel { Margin = new Thickness(4, 0, 0, 0) }; Grid.SetColumn(sp, 1);
                    var head = new DockPanel { LastChildFill = false };
                    head.Children.Add(new TextBlock { Text = rec.Hero.name, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center });
                    var capsules = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(capsules, Dock.Right);
                    capsules.Children.Add(Capsule(Engine.RoleTr(rec.Hero.role), RoleBrush(rec.Hero.role)));
                    if (rec.Hero.tier != null) capsules.Children.Add(Capsule(rec.Hero.tier, rec.Hero.tier is "S" or "A" ? AccentB : Text3));
                    if (rec.Hero.winRate != null) capsules.Children.Add(Capsule($"%{rec.Hero.winRate:0.0}", Text2));
                    head.Children.Add(capsules);
                    sp.Children.Add(head);
                    sp.Children.Add(new TextBlock
                    {
                        Text = rec.Reason,
                        Foreground = Text2,
                        FontSize = 12.5,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        LineHeight = 17
                    });
                    grid.Children.Add(sp);
                    card.Child = grid;
                    int hid = rec.Hero.id;
                    card.MouseLeftButtonUp += (s, e) => { AddHero(hid, Mode.Me); RenderAll(); };
                    card.ToolTip = "Tıkla: Sen olarak seç";
                    Recs.Children.Add(card);
                    first = false;
                }
            }
        }

        Border Capsule(string text, Brush fg)
        {
            return new Border
            {
                Background = SurfaceInset,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = text, Foreground = fg, FontSize = 10.5, FontWeight = FontWeights.SemiBold }
            };
        }

        void RenderMeta()
        {
            MetaPanel.Children.Clear();
            foreach (var role in Roles)
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
                head.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = RoleBrush(role), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                head.Children.Add(new TextBlock { Text = Engine.RoleTr(role), FontWeight = FontWeights.SemiBold, Foreground = TextB, FontSize = 13 });
                MetaPanel.Children.Add(head);

                var wp = new WrapPanel();
                foreach (var h in Engine.D.heroes.Where(h => h.role == role && (h.tier == "S" || h.tier == "A"))
                             .OrderBy(h => h.tier == "S" ? 0 : 1).ThenByDescending(h => h.winRate ?? 0).Take(6))
                {
                    var tile = new Border
                    {
                        Width = 88, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12),
                        Background = SurfaceAlt, BorderBrush = BorderB, BorderThickness = new Thickness(1),
                        Padding = new Thickness(8), Cursor = Cursors.Hand,
                        ToolTip = $"{h.name} · {h.tier} · %{h.winRate:0.0} WR"
                    };
                    var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    sp.Children.Add(RoundAvatar(h.id, 44));
                    sp.Children.Add(new TextBlock
                    {
                        Text = h.name, FontSize = 11, Foreground = TextB, TextTrimming = TextTrimming.CharacterEllipsis,
                        TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 4), MaxWidth = 72
                    });
                    var cap = Capsule(h.tier, h.tier == "S" ? AccentB : Text2);
                    cap.HorizontalAlignment = HorizontalAlignment.Center;
                    cap.Margin = new Thickness(0);
                    sp.Children.Add(cap);
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

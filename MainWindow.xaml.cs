using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Shell;

namespace RivalsCounter
{
    public partial class MainWindow : Window
    {
        enum Mode { Enemy, Team, Me }
        Mode mode = Mode.Enemy;
        readonly List<int> enemies = new(), allies = new();
        int? me, struggling;
        bool dark;
        Brush Fg, Sub, Card, Card2, Accent = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)), Danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        readonly Dictionary<int, ImageSource> img = new();
        readonly Dictionary<int, Border> tiles = new();
        readonly Dictionary<Mode, Button> modeBtns = new();
        bool metaView; string metaRole = null; bool metaBuilt;
        Button vCounter, vMeta;
        ToggleButton topBtn;
        static readonly string[] Roles = { "Vanguard", "Duelist", "Strategist" };

        public MainWindow()
        {
            InitializeComponent();
            Engine.Load();
            ApplyTheme();
            BuildBars(); BuildGrid(); RenderSlots(); RenderRecs();
            PatchText.Text = $"{Engine.D.patch} · istatistik {Engine.D.rivalsmetaStatsDate}";
            Status.Text = "Sol tık: ekle · Sağ tık: düşmana ekle + zorlandığım · Slot sol tık: çıkar, sağ tık: zorlandığım · F1/F2/F3 mod · F4 Counter/Meta · Ctrl+N yeni maç · Ctrl+Shift+R göster/gizle · Kaynak: rivalsmeta.com eşleşmeleri, marvelrivals.gg tier";
            Search.TextChanged += (s, e) => { SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; FilterGrid(); };
            Search.KeyDown += Search_KeyDown;
            PreviewKeyDown += Window_PreviewKeyDown;
            PreviewTextInput += (s, e) => { if (!Search.IsKeyboardFocused && e.Text.Length > 0 && char.IsLetterOrDigit(e.Text[0])) { Search.Focus(); } };
            SourceInitialized += OnSourceInitialized;
            if (Environment.GetCommandLineArgs().Contains("--meta")) SetView(true);
            if (Environment.GetCommandLineArgs().Contains("--demo")) { foreach (var d in new[]{1029,1024,1053,1037,1058,1054}) AddHero(d, Mode.Enemy); struggling = 1029; AddHero(1048, Mode.Me); AddHero(1031, Mode.Team); AddHero(1051, Mode.Team); }
            SystemEvents.UserPreferenceChanged += (s, e) => Dispatcher.Invoke(() => { ApplyTheme(); BuildBars(); BuildGrid(); RenderSlots(); RenderRecs(); SetBackdrop(); metaBuilt = false; if (metaView) BuildMeta(); });
        }

        // ---------- theme / Mica ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        void OnSourceInitialized(object s, EventArgs e)
        {
            SetBackdrop();
            var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            src.AddHook(WndProc);
            RegisterHotKey(src.Handle, 1, 0x0002 | 0x0004 | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Key.R)); // Ctrl+Shift+R
            Closed += (_, __) => UnregisterHotKey(src.Handle, 1);
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
            if (msg == 0x0312 && w.ToInt32() == 1)
            {
                if (IsVisible && WindowState != WindowState.Minimized && IsActive) Hide();
                else { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Search.Focus(); }
                handled = true;
            }
            return IntPtr.Zero;
        }
        void ApplyTheme()
        {
            try { dark = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 0; } catch { dark = true; }
            Fg = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1E));
            Sub = new SolidColorBrush(dark ? Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x99, 0, 0, 0));
            Card = new SolidColorBrush(dark ? Color.FromArgb(0x30, 0x20, 0x20, 0x24) : Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
            Card2 = new SolidColorBrush(dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0, 0, 0));
            Foreground = Fg; TitleText.Foreground = Fg; Search.Foreground = Fg; Search.CaretBrush = Fg;
            SearchBorder.Background = Card2; SlotsCard.Background = Card; GridCard.Background = Card; RecCard.Background = Card;
        }

        /// <summary>Kahraman ikonu. Uygulama ikon dağıtmaz: kullanıcı isterse kendi ikonlarını
        /// %LOCALAPPDATA%\RivalsCounter\Icons\<id>.png (ya da uygulama klasöründe Icons\<id>.png) olarak koyar.
        /// Dosya yoksa rol renginde, baş harfli bir kart çizilir.</summary>
        ImageSource Img(int id)
        {
            if (img.TryGetValue(id, out var cached)) return cached;
            foreach (var dir in new[] { Engine.UserIcons, System.IO.Path.Combine(Engine.BaseDir, "Icons") })
            {
                var file = System.IO.Path.Combine(dir, id + ".png");
                if (!System.IO.File.Exists(file)) continue;
                try
                {
                    var b = new BitmapImage(); b.BeginInit();
                    b.UriSource = new Uri(file);
                    b.DecodePixelWidth = 96; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
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
            var initials = words.Length >= 2 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}"
                                             : name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
            var text = new FormattedText(initials, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 34, Brushes.White, 1.0);
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 96, 96));
                dc.DrawText(text, new Point((96 - text.Width) / 2, (96 - text.Height) / 2));
            }
            g.Freeze();
            var di = new DrawingImage(g); di.Freeze();
            return di;
        }

        // ---------- top bar ----------
        Button Pill(string text, bool primary = false)
        {
            var t = new TextBlock { Text = text, Foreground = primary ? Brushes.White : Fg, FontSize = 13 };
            var b = new Button { Content = t, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(14, 6, 14, 6), Cursor = Cursors.Hand, Focusable = false };
            b.Template = PillTemplate();
            b.Background = primary ? Accent : Card2;
            return b;
        }
        static ControlTemplate PillTemplate()
        {
            var ct = new ControlTemplate(typeof(ButtonBase));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter)); cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp); ct.VisualTree = bd; return ct;
        }
        void BuildBars()
        {
            ModeBar.Children.Clear(); modeBtns.Clear();
            foreach (var (m, t) in new[] { (Mode.Enemy, "Düşman  F1"), (Mode.Team, "Takımım  F2"), (Mode.Me, "Ben  F3") })
            {
                var b = Pill(t); b.Click += (s, e) => SetMode(m); modeBtns[m] = b; ModeBar.Children.Add(b);
                WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            }
            SetMode(mode);
            ViewBar.Children.Clear();
            vCounter = Pill("Counter"); vMeta = Pill("Meta  F4");
            vCounter.Click += (s, e) => SetView(false); vMeta.Click += (s, e) => SetView(true);
            foreach (var b in new[] { vCounter, vMeta }) { b.Margin = new Thickness(0); WindowChrome.SetIsHitTestVisibleInChrome(b, true); }
            var seg = new StackPanel { Orientation = Orientation.Horizontal }; seg.Children.Add(vCounter); seg.Children.Add(vMeta);
            ViewBar.Children.Add(new Border { Child = seg, CornerRadius = new CornerRadius(9), Background = Card2, Padding = new Thickness(2) });
            SetView(metaView);
            RightBar.Children.Clear();
            var clear = Pill("Yeni maç  Ctrl+N", true); clear.Click += (s, e) => ClearAll();
            topBtn = new ToggleButton { Content = new TextBlock { Text = "📌 Üstte tut", Foreground = Fg }, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(6, 0, 0, 0), Template = PillTemplate(), Background = Card2, Focusable = false, IsChecked = Topmost, Cursor = Cursors.Hand };
            topBtn.Click += (s, e) => { Topmost = topBtn.IsChecked == true; topBtn.Background = Topmost ? Accent : Card2; ((TextBlock)topBtn.Content).Foreground = Topmost ? Brushes.White : Fg; };
            var upd = Pill("Verileri güncelle"); upd.Click += async (s, e) =>
            {
                upd.IsEnabled = false; Status.Text = "rivalsmeta.com'dan güncel eşleşmeler indiriliyor…";
                try { Status.Text = "✓ " + await Engine.RefreshAsync(); metaBuilt = false; if (metaView) BuildMeta(); PatchText.Text = $"{Engine.D.patch} · istatistik {Engine.D.rivalsmetaStatsDate}"; RenderRecs(); }
                catch (Exception ex) { Status.Text = "Güncelleme başarısız, yerel veri kullanılıyor: " + ex.Message; }
                upd.IsEnabled = true;
            };
            foreach (var b in new ButtonBase[] { upd, topBtn, clear }) { WindowChrome.SetIsHitTestVisibleInChrome(b, true); RightBar.Children.Add(b); }
        }
        void SetMode(Mode m)
        {
            mode = m;
            foreach (var kv in modeBtns) { bool on = kv.Key == m; kv.Value.Background = on ? Accent : Card2; ((TextBlock)kv.Value.Content).Foreground = on ? Brushes.White : Fg; }
        }

        // ---------- meta view ----------
        void SetView(bool meta)
        {
            metaView = meta;
            foreach (var (b, on) in new[] { (vCounter, !meta), (vMeta, meta) }) { b.Background = on ? Accent : Brushes.Transparent; ((TextBlock)b.Content).Foreground = on ? Brushes.White : Fg; }
            if (meta && !metaBuilt) BuildMeta();
            HeroGrid.Visibility = meta ? Visibility.Collapsed : Visibility.Visible;
            MetaPanel.Visibility = meta ? Visibility.Visible : Visibility.Collapsed;
            LeftScroll.ScrollToTop();
            if (meta) MetaPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(140)));
        }
        void BuildMeta()
        {
            metaBuilt = true; MetaPanel.Children.Clear();
            var d = Engine.D;
            var head = new DockPanel { Margin = new Thickness(4, 6, 0, 4), LastChildFill = false };
            head.Children.Add(new TextBlock { Text = $"Meta tier listesi · {d.patch}", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Fg, VerticalAlignment = VerticalAlignment.Center });
            var filt = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(filt, Dock.Right);
            foreach (var (r, t) in new[] { ((string)null, "Tümü"), ("Vanguard", "Öncü"), ("Duelist", "Düellocu"), ("Strategist", "Stratejist") })
            {
                var b = Pill(t, r == metaRole); b.Padding = new Thickness(10, 4, 10, 4); b.Click += (s, e) => { metaRole = r; BuildMeta(); }; filt.Children.Add(b);
            }
            head.Children.Add(filt);
            MetaPanel.Children.Add(head);
            MetaPanel.Children.Add(new TextBlock { Text = $"Tier: marvelrivals.gg meta tier list (Grand Master+, güncelleme {d.tierDate}) · WR/PR: rivalsmeta.com tüm ranklar, {d.rivalsmetaStatsDate} · Tıkla: aktif moda ekle, sağ tık: düşman + zorlandığım", Foreground = Sub, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8) });
            var tierCol = new Dictionary<string, Color> { ["S"] = Color.FromRgb(0xFF, 0x45, 0x3A), ["A"] = Color.FromRgb(0xFF, 0x9F, 0x0A), ["B"] = Color.FromRgb(0x30, 0xD1, 0x58), ["C"] = Color.FromRgb(0x0A, 0x84, 0xFF), ["D"] = Color.FromRgb(0x8E, 0x8E, 0x93) };
            var roleOrder = new Dictionary<string, int> { ["Vanguard"] = 0, ["Duelist"] = 1, ["Strategist"] = 2 };
            foreach (var t in new[] { "S", "A", "B", "C", "D" })
            {
                var hs = d.heroes.Where(h => h.tier == t && (metaRole == null || h.role == metaRole)).OrderBy(h => roleOrder[h.role]).ThenByDescending(h => h.winRate ?? 0).ToList();
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(tierCol[t]), Child = new TextBlock { Text = t, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                var wp = new WrapPanel { Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(wp, 1);
                if (hs.Count == 0) wp.Children.Add(new TextBlock { Text = "—", Foreground = Sub, Margin = new Thickness(8, 20, 0, 20) });
                foreach (var h in hs)
                {
                    var tile = HeroTile(h, 56);
                    ((StackPanel)tile.Child).Children.Add(new TextBlock { Text = $"%{h.winRate:0.0} WR · %{h.pickRate:0.#} PR", FontSize = 9.5, Foreground = Sub, TextAlignment = TextAlignment.Center });
                    tile.ToolTip = $"{h.name} · {Engine.RoleTr(h.role)} · {t} tier · kazanma %{h.winRate:0.00} · seçilme %{h.pickRate:0.0} · {h.matches:N0} maç (rivalsmeta)";
                    var id = h.id;
                    tile.MouseLeftButtonUp += (s, e) => AddHero(id, mode);
                    tile.MouseRightButtonUp += (s, e) => { if (!enemies.Contains(id)) AddHero(id, Mode.Enemy); if (enemies.Contains(id)) { struggling = id; RenderSlots(); RenderRecs(); } };
                    wp.Children.Add(tile);
                }
                row.Children.Add(wp); MetaPanel.Children.Add(row);
            }
        }

        // ---------- hero grid ----------
        void BuildGrid()
        {
            HeroGrid.Children.Clear(); tiles.Clear();
            foreach (var r in Roles)
            {
                HeroGrid.Children.Add(new TextBlock { Text = Engine.RoleTr(r).ToUpper(new System.Globalization.CultureInfo("tr-TR")), Foreground = Sub, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 8, 0, 6) });
                var wp = new WrapPanel();
                foreach (var h in Engine.D.heroes.Where(x => x.role == r).OrderBy(x => x.name))
                {
                    var tile = HeroTile(h, 64);
                    tile.MouseLeftButtonUp += (s, e) => AddHero(h.id, mode);
                    tile.MouseRightButtonUp += (s, e) => { if (!enemies.Contains(h.id)) AddHero(h.id, Mode.Enemy); if (enemies.Contains(h.id)) { struggling = h.id; RenderSlots(); RenderRecs(); } };
                    tiles[h.id] = tile; wp.Children.Add(tile);
                }
                HeroGrid.Children.Add(wp);
            }
        }
        Border HeroTile(Hero h, double size)
        {
            var im = new Image { Source = Img(h.id), Width = size, Height = size, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            var imgBorder = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(8), Background = new ImageBrush(Img(h.id)) { Stretch = Stretch.UniformToFill } };
            var name = new TextBlock { Text = h.name, FontSize = 10.5, Foreground = Sub, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = size + 6, Margin = new Thickness(0, 3, 0, 0) };
            var sp = new StackPanel(); sp.Children.Add(imgBorder); sp.Children.Add(name);
            var b = new Border { Child = sp, Margin = new Thickness(4), Padding = new Thickness(2), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, ToolTip = $"{h.name} · {h.tier} tier · %{h.winRate:0.0} WR" };
            b.MouseEnter += (s, e) => b.Background = Card2; b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }
        static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        List<Hero> Matches()
        {
            var q = Norm(Search.Text); if (q.Length == 0) return new();
            return Engine.D.heroes.Where(h => Norm(h.name).Contains(q) || h.name.Split(' ', '-', '(').Any(w => Norm(w).StartsWith(q)))
                .OrderBy(h => Norm(h.name).StartsWith(q) ? 0 : h.name.Split(' ', '-').Any(w => Norm(w).StartsWith(q)) ? 1 : 2).ThenBy(h => h.name).ToList();
        }
        void FilterGrid()
        {
            var m = Matches(); var set = m.Select(h => h.id).ToHashSet(); bool any = Search.Text.Length > 0;
            foreach (var kv in tiles)
            {
                kv.Value.Opacity = !any || set.Contains(kv.Key) ? 1 : 0.18;
                kv.Value.BorderBrush = any && m.Count > 0 && m[0].id == kv.Key ? Accent : Brushes.Transparent;
            }
        }
        void Search_KeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { var m = Matches(); if (m.Count > 0) { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { AddHero(m[0].id, Mode.Enemy); struggling = m[0].id; RenderSlots(); RenderRecs(); } else AddHero(m[0].id, mode); } Search.Clear(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Search.Clear(); e.Handled = true; }
        }
        void Window_PreviewKeyDown(object s, KeyEventArgs e)
        {
            if (e.Key == Key.F4 || (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)) { SetView(!metaView); e.Handled = true; }
            else if (e.Key == Key.F1) { SetMode(Mode.Enemy); e.Handled = true; }
            else if (e.Key == Key.F2) { SetMode(Mode.Team); e.Handled = true; }
            else if (e.Key == Key.F3) { SetMode(Mode.Me); e.Handled = true; }
            else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { ClearAll(); e.Handled = true; }
            else if (e.Key == Key.Back && Search.Text.Length == 0)
            {
                var l = mode == Mode.Enemy ? enemies : allies;
                if (mode == Mode.Me) me = null; else if (l.Count > 0) { if (struggling == l[^1]) struggling = null; l.RemoveAt(l.Count - 1); }
                RenderSlots(); RenderRecs(); e.Handled = true;
            }
        }

        // ---------- state ----------
        void AddHero(int id, Mode m)
        {
            if (m == Mode.Enemy) { if (enemies.Contains(id) || enemies.Count >= 6) return; enemies.Add(id); if (enemies.Count == 6 && mode == Mode.Enemy) SetMode(me == null ? Mode.Me : Mode.Team); }
            else if (m == Mode.Me) { allies.Remove(id); me = id; SetMode(Mode.Team); }
            else { if (allies.Contains(id) || id == me || allies.Count >= 5) return; allies.Add(id); }
            RenderSlots(); RenderRecs();
        }
        void ClearAll() { enemies.Clear(); allies.Clear(); me = null; struggling = null; Search.Clear(); SetMode(Mode.Enemy); RenderSlots(); RenderRecs(); Search.Focus(); }

        void RenderSlots()
        {
            Slots.Children.Clear();
            Slots.Children.Add(SlotRow("Düşman takım", enemies, 6, true));
            var row = SlotRow("Takımım", allies, 5, false);
            var meCell = Slot(me, false, () => { me = null; RenderSlots(); RenderRecs(); }, null, "BEN");
            meCell.Margin = new Thickness(18, 0, 0, 0); ((Panel)row.Children[1]).Children.Insert(0, meCell);
            Slots.Children.Add(row);
        }
        StackPanel SlotRow(string title, List<int> list, int n, bool enemy)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, enemy ? 10 : 0) };
            var t = new TextBlock { Text = title + (enemy && struggling != null ? $"   ·   Zorlandığım: {Engine.ById[struggling.Value].name}" : ""), Foreground = enemy ? Danger : Accent, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6) };
            sp.Children.Add(t);
            var wp = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < n; i++)
            {
                int? id = i < list.Count ? list[i] : null;
                wp.Children.Add(Slot(id, enemy && id != null && id == struggling,
                    () => { if (id == struggling) struggling = null; list.Remove(id.Value); RenderSlots(); RenderRecs(); },
                    enemy ? () => { struggling = struggling == id ? null : id; RenderSlots(); RenderRecs(); } : null, null));
            }
            sp.Children.Add(wp); return sp;
        }
        Border Slot(int? id, bool marked, Action remove, Action mark, string label)
        {
            const double S = 56;
            var b = new Border { Width = S, Height = S, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(marked ? 3 : 1), BorderBrush = marked ? Danger : Card2, Background = Card2 };
            if (id.HasValue)
            {
                var g = new Grid();
                g.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(Img(id.Value)) { Stretch = Stretch.UniformToFill } });
                if (marked) g.Children.Add(new Border { Background = Danger, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2), Padding = new Thickness(4, 0, 4, 1), Child = new TextBlock { Text = "!", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 11 } });
                b.Child = g; b.Cursor = Cursors.Hand; b.ToolTip = Engine.ById[id.Value].name + (mark != null ? " · sağ tık: zorlandığım" : "") + " · sol tık: çıkar";
                b.MouseLeftButtonUp += (s, e) => remove();
                if (mark != null) b.MouseRightButtonUp += (s, e) => mark();
            }
            else b.Child = new TextBlock { Text = label ?? "+", Foreground = Sub, FontSize = label != null ? 11 : 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            return b;
        }

        // ---------- recommendations ----------
        void RenderRecs()
        {
            Recs.Children.Clear();
            Recs.Children.Add(new TextBlock { Text = "Önerilen counter'lar", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Fg, Margin = new Thickness(0, 0, 0, 2) });
            if (enemies.Count == 0)
            {
                Recs.Children.Add(new TextBlock { Text = "Düşman kahramanlarını seç. Zorlandığın kahramana sağ tıkla, öneriler ona göre ağırlıklandırılır.", Foreground = Sub, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                return;
            }
            Recs.Children.Add(new TextBlock { Text = struggling != null ? $"Odak: {Engine.ById[struggling.Value].name} (3× ağırlık)" : $"{enemies.Count} düşmana göre", Foreground = Sub, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
            var res = Engine.Recommend(enemies, struggling, allies, me);
            foreach (var r in Roles)
            {
                if (!res.TryGetValue(r, out var list)) continue;
                Recs.Children.Add(new TextBlock { Text = Engine.RoleTr(r).ToUpper(new System.Globalization.CultureInfo("tr-TR")), Foreground = Sub, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
                bool first = true;
                foreach (var rec in list)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Top, Background = new ImageBrush(Img(rec.Hero.id)) { Stretch = Stretch.UniformToFill }, BorderThickness = new Thickness(first ? 2 : 0), BorderBrush = Accent });
                    var sp = new StackPanel(); Grid.SetColumn(sp, 1);
                    var head = new StackPanel { Orientation = Orientation.Horizontal };
                    head.Children.Add(new TextBlock { Text = rec.Hero.name, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Fg });
                    if (rec.Hero.tier != null) head.Children.Add(new Border { Background = rec.Hero.tier == "S" ? Accent : Card2, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 1, 0, 0), Child = new TextBlock { Text = rec.Hero.tier, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = rec.Hero.tier == "S" ? Brushes.White : Fg } });
                    head.Children.Add(new TextBlock { Text = $"  {(rec.Score >= 0 ? "+" : "")}{rec.Score:0.0}", Foreground = Sub, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                    sp.Children.Add(head);
                    sp.Children.Add(new TextBlock { Text = rec.Reason, Foreground = Sub, FontSize = 11.5, TextWrapping = TextWrapping.Wrap });
                    row.Children.Add(sp);
                    if (allies.Count > 0 || me != null) { } 
                    row.Cursor = Cursors.Hand; row.ToolTip = "Tıkla: kendine seç (Ben)"; var hid = rec.Hero.id; row.MouseLeftButtonUp += (s, e) => AddHero(hid, Mode.Me);
                    Recs.Children.Add(row); first = false;
                }
            }
            Recs.Children.Add(new TextBlock { Text = $"Puan = Σ eşleşme puanı (rivalsmeta Diamond+ 'Counters' kazanma oranları) + tier (marvelrivals.gg) + team-up. Veri: {Engine.D.rivalsmetaStatsDate}.", Foreground = Sub, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            Recs.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(140)));
        }
    }
}

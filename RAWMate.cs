// RAWMate - a dependency-free Windows WPF photo culler for JPG/JPEG + Sony ARW pairs.
// Build command is in BUILD_RAWMate.cmd. The application uses the Windows Recycle Bin,
// never a permanent delete operation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("RAWMate")]
[assembly: AssemblyDescription("JPG / Sony ARW photo culling and organization tool")]
[assembly: AssemblyCompany("ren8484")]
[assembly: AssemblyProduct("RAWMate")]
[assembly: AssemblyCopyright("Copyright © 2026 ren8484")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyInformationalVersion("1.1.1")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "error.log"), DateTime.Now.ToString("s") + Environment.NewLine + e.Exception + Environment.NewLine + Environment.NewLine);
                MessageBox.Show("RAWMate 出现错误，详细信息已写入 error.log：\n\n" + e.Exception.Message, "RAWMate 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            e.Handled = true;
        };
        app.Run(new MainWindow());
    }
}

internal sealed class MainWindow : Window
{
    private TextBox folderBox = new TextBox();
    private Button folderHistoryButton = new Button();
    private WrapPanel gallery = new WrapPanel();
    private ScrollViewer galleryScrollViewer = new ScrollViewer();
    private TextBlock status = new TextBlock();
    private TextBlock folderHint = new TextBlock();
    private TextBlock jpgCount = new TextBlock();
    private TextBlock rawCount = new TextBlock();
    private TextBlock pendingCount = new TextBlock();
    private TextBlock cullProgressCount = new TextBlock();
    private TextBlock cullProgressPercent = new TextBlock();
    private TextBlock pickedProgressCount = new TextBlock();
    private TextBlock rejectedProgressCount = new TextBlock();
    private TextBlock unprocessedProgressCount = new TextBlock();
    private TextBlock ratedProgressCount = new TextBlock();
    private ColumnDefinition cullProgressFillColumn = new ColumnDefinition();
    private ColumnDefinition cullProgressRemainingColumn = new ColumnDefinition();
    private TextBlock galleryCaption = new TextBlock();
    private TextBlock photoInfoTitle = new TextBlock();
    private TextBlock photoInfo = new TextBlock();
    private Button organizeButton = new Button();
    private Button unorganizeButton = new Button();
    private Button recycleSelectedButton = new Button();
    private Button recycleRejectedButton = new Button();
    private Button filterButton = new Button();
    private Button autoAdvanceButton = new Button();
    private Image singleImage = new Image();
    private ScrollViewer singleViewer = new ScrollViewer();
    private TextBlock singleCaption = new TextBlock();
    private TextBox zoomBox = new TextBox();
    private Canvas navigatorCanvas = new Canvas();
    private Image navigatorImage = new Image();
    private Border navigatorViewport = new Border();
    private bool navigatorDragging;
    private bool navigatorUpdateQueued;
    private StackPanel filmStrip = new StackPanel();
    private ScrollViewer filmStripScrollViewer = new ScrollViewer();
    private readonly Dictionary<Border, string> tileFiles = new Dictionary<Border, string>();
    private readonly Dictionary<Border, string> filmTiles = new Dictionary<Border, string>();
    private readonly Dictionary<Border, string> lazyFilmThumbnailFiles = new Dictionary<Border, string>();
    private readonly HashSet<Border> loadingFilmThumbnailTiles = new HashSet<Border>();
    private readonly Dictionary<Border, string> lazyThumbnailFiles = new Dictionary<Border, string>();
    private readonly HashSet<Border> loadingThumbnailTiles = new HashSet<Border>();
    private readonly HashSet<string> selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> galleryFiles = new List<string>();
    private readonly HashSet<string> pickedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rejectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> starRatings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private Border selectionAnchor;
    private string currentSingleFile;
    private bool singleViewMode;
    private bool fitSingleImage = true;
    private double singleZoom = 1.0;
    private Point singlePanStart;
    private double singlePanHorizontal;
    private double singlePanVertical;
    private bool singlePanning;
    private int singleLoadVersion;
    private int galleryThumbnailLoadVersion;
    private bool thumbnailLoadQueued;
    private int filmThumbnailLoadVersion;
    private bool filmThumbnailLoadQueued;
    private readonly object fullImageCacheLock = new object();
    private readonly Dictionary<string, BitmapSource> fullImageCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<BitmapSource>> fullImageLoadTasks = new Dictionary<string, Task<BitmapSource>>(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> fullImageCacheLru = new LinkedList<string>();
    private readonly Dictionary<string, DateTime?> captureDateCache = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
    private int fullImageCacheGeneration;
    private readonly List<string> recentFolders = new List<string>();
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate", "settings.txt");
    private readonly string cullMarksPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RAWMate", "cull-marks.txt");
    private string activeFilter = "all";
    private string lastFolder;
    private string gallerySortMode = "name_asc";
    private double gridTileWidth = 196;
    private bool autoAdvanceEnabled;
    private static readonly bool darkMode = true;
    private const double DefaultWindowWidth = 1380;
    private const double DefaultWindowHeight = 860;
    private const double PreferredMinimumWindowWidth = 1000;
    private const double PreferredMinimumWindowHeight = 640;
    private const double SidebarWidth = 320;
    // The sidebar column also contains outer margins, border and padding; 280 DIPs
    // matches the original usable content width so large screens are not rescaled.
    private const double SidebarContentWidth = 280;
    private const string ButtonBlue = "#436794";

    private static readonly string[] JpgExtensions = { ".jpg", ".jpeg" };
    private static readonly string[] RawExtensions = { ".arw" };

    public MainWindow()
    {
        Title = "RAWMate";
        ApplyInitialWindowSize();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        LoadSettings();
        LoadCullMarks();
        if (!String.IsNullOrWhiteSpace(lastFolder)) folderBox.Text = lastFolder;
        Background = Brush("#F4F7FB");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        try { Icon = BitmapFrame.Create(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RAWMate.ico"), UriKind.Absolute)); } catch { }
        Content = BuildUi();
        SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
        PreviewKeyDown += MainWindowPreviewKeyDown;
        Loaded += delegate
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(RestoreSession));
        };
        Closing += delegate { SaveSettings(); SaveCullMarks(); };
    }

    private void ApplyInitialWindowSize()
    {
        // WPF reports the work area in device-independent units, so this also accounts
        // for Windows display scaling (for example 1920x1080 at 150% is about 1280x720).
        var workArea = SystemParameters.WorkArea;
        var availableWidth = workArea.Width > 0 ? workArea.Width : DefaultWindowWidth;
        var availableHeight = workArea.Height > 0 ? workArea.Height : DefaultWindowHeight;
        const double edgeAllowance = 8;
        availableWidth = Math.Max(1, availableWidth - edgeAllowance);
        availableHeight = Math.Max(1, availableHeight - edgeAllowance);

        MinWidth = Math.Min(PreferredMinimumWindowWidth, availableWidth);
        MinHeight = Math.Min(PreferredMinimumWindowHeight, availableHeight);
        Width = Math.Min(DefaultWindowWidth, availableWidth);
        Height = Math.Min(DefaultWindowHeight, availableHeight);
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Background = Brush("#F4F7FB") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border { Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Margin = new Thickness(6, 6, 3, 6), Padding = new Thickness(14), ClipToBounds = true };
        var sidebarLayout = new Grid();
        sidebarLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sidebarLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sidebarContent = new Grid { Width = SidebarContentWidth, HorizontalAlignment = HorizontalAlignment.Left };
        sidebarContent.Children.Add(BuildSidebar());
        var sidebarViewbox = new Viewbox
        {
            Child = sidebarContent,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // Keep the removed brand area's space from reappearing on tall/4K windows.
            // The Viewbox may still scale the sidebar down on short/high-DPI work areas,
            // but any unused vertical space must remain below the controls.
            VerticalAlignment = VerticalAlignment.Top
        };
        sidebarLayout.Children.Add(sidebarViewbox);

        var versionText = new TextBlock
        {
            Text = GetDisplayVersion(),
            Foreground = Brush("#8F9CAA"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 8, 0, 0)
        };
        Grid.SetRow(versionText, 1);
        sidebarLayout.Children.Add(versionText);
        sidebar.Child = sidebarLayout;
        root.Children.Add(sidebar);

        var main = new Grid { Margin = new Thickness(3, 6, 6, 6) };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var galleryCard = Card();
        galleryCard.Padding = new Thickness(10);
        galleryCard.Child = BuildGalleryCard();
        main.Children.Add(galleryCard);
        var infoCard = Card();
        infoCard.Padding = new Thickness(12, 8, 12, 8);
        infoCard.Margin = new Thickness(0, 6, 0, 0);
        infoCard.Child = BuildPhotoInfoCard();
        Grid.SetRow(infoCard, 1);
        main.Children.Add(infoCard);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        return root;
    }

    private static string GetDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var attribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();
        var raw = attribute == null ? null : attribute.InformationalVersion;
        if (String.IsNullOrWhiteSpace(raw)) raw = assembly.GetName().Version.ToString(3);

        var metadataIndex = raw.IndexOf('+');
        if (metadataIndex >= 0) raw = raw.Substring(0, metadataIndex);
        var parts = raw.Split('.');
        if (parts.Length >= 3) return "V" + parts[0] + "." + parts[1] + parts[2];
        return "V" + raw;
    }

    private UIElement BuildSidebar()
    {
        var panel = new StackPanel();
        panel.Children.Add(BuildPathCard());
        panel.Children.Add(Divider());
        panel.Children.Add(BuildStatCard());
        panel.Children.Add(Divider());
        panel.Children.Add(BuildFooter());
        return panel;
    }

    private UIElement BuildPathCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("照片目录"));
        var folderRow = new Grid();
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        folderBox.Height = 40;
        folderBox.FontSize = 15;
        folderBox.Padding = new Thickness(10, 0, 10, 0);
        folderBox.VerticalContentAlignment = VerticalAlignment.Center;
        folderBox.BorderThickness = new Thickness(0);
        folderBox.Background = darkMode ? Brush("#171D24") : Brush("#FFFFFF");
        folderBox.Foreground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937");
        folderBox.CaretBrush = folderBox.Foreground;
        StyleRoundedTextBox(folderBox, new CornerRadius(6, 0, 0, 6));
        folderBox.TextChanged -= FolderBoxTextChanged;
        folderBox.TextChanged += FolderBoxTextChanged;
        folderRow.Children.Add(folderBox);
        folderHistoryButton.Content = "▼";
        StyleFolderHistoryButton(folderHistoryButton);
        folderHistoryButton.Click -= OpenFolderHistory;
        folderHistoryButton.Click += OpenFolderHistory;
        Grid.SetColumn(folderHistoryButton, 1);
        folderRow.Children.Add(folderHistoryButton);
        panel.Children.Add(new Border
        {
            Child = folderRow,
            Margin = new Thickness(0, 10, 0, 0),
            BorderBrush = darkMode ? Brush("#596675") : Brush("#B7C3D0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true
        });
        folderHint.Margin = new Thickness(2, 6, 2, 0);
        folderHint.FontSize = 12;
        folderHint.Foreground = Brush("#6A7787");
        folderHint.Text = "选择照片主目录";
        folderHint.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(folderHint);
        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var browse = SecondaryButton("选择目录", 0);
        browse.Height = 34;
        browse.HorizontalAlignment = HorizontalAlignment.Stretch;
        browse.Click += ChooseFolder;
        actions.Children.Add(browse);
        var scan = PrimaryButton("刷新目录", 0);
        scan.Height = 34;
        scan.HorizontalAlignment = HorizontalAlignment.Stretch;
        scan.Click += delegate { RefreshCurrentFolder(); };
        Grid.SetColumn(scan, 2);
        actions.Children.Add(scan);
        panel.Children.Add(actions);
        return panel;
    }

    private UIElement BuildStatCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionTitle("目录概览"));

        var stats = new Grid { Margin = new Thickness(2, 12, 2, 0) };
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var statA = Stat("JPG", jpgCount, "#2678D4");
        var statB = Stat("RAW", rawCount, "#8167C8");
        var statC = Stat("待分类", pendingCount, "#E28825");
        stats.Children.Add(statA);
        Grid.SetColumn(statB, 1);
        stats.Children.Add(statB);
        Grid.SetColumn(statC, 2);
        stats.Children.Add(statC);
        panel.Children.Add(stats);
        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        organizeButton.Content = "创建分类";
        StyleButton(organizeButton, true, 0);
        organizeButton.Height = 34;
        organizeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        organizeButton.Click -= OrganizeFiles;
        organizeButton.Click += OrganizeFiles;
        actions.Children.Add(organizeButton);
        unorganizeButton.Content = "取消分类";
        StyleButton(unorganizeButton, true, 0);
        unorganizeButton.Height = 34;
        unorganizeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        unorganizeButton.Click -= UnorganizeFiles;
        unorganizeButton.Click += UnorganizeFiles;
        Grid.SetColumn(unorganizeButton, 2);
        actions.Children.Add(unorganizeButton);
        panel.Children.Add(actions);
        panel.Children.Add(BuildCullProgress());
        return panel;
    }

    private UIElement BuildCullProgress()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "挑片进度",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#1F2937")
        });
        cullProgressCount.FontSize = 13;
        cullProgressCount.FontWeight = FontWeights.SemiBold;
        cullProgressCount.Foreground = Brush("#DCEBFA");
        cullProgressCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(cullProgressCount, 1);
        header.Children.Add(cullProgressCount);
        panel.Children.Add(header);

        var progress = new Grid
        {
            Background = Brushes.Transparent
        };
        cullProgressFillColumn = new ColumnDefinition();
        cullProgressRemainingColumn = new ColumnDefinition();
        progress.ColumnDefinitions.Add(cullProgressFillColumn);
        progress.ColumnDefinitions.Add(cullProgressRemainingColumn);
        var fill = new Border { Background = Brush(ButtonBlue), CornerRadius = new CornerRadius(5) };
        progress.Children.Add(fill);
        cullProgressPercent.FontSize = 12;
        cullProgressPercent.FontWeight = FontWeights.SemiBold;
        cullProgressPercent.Foreground = Brushes.White;
        cullProgressPercent.HorizontalAlignment = HorizontalAlignment.Stretch;
        cullProgressPercent.VerticalAlignment = VerticalAlignment.Center;
        cullProgressPercent.TextAlignment = TextAlignment.Center;
        Grid.SetColumnSpan(cullProgressPercent, 2);
        Panel.SetZIndex(cullProgressPercent, 1);
        progress.Children.Add(cullProgressPercent);
        panel.Children.Add(new Border
        {
            Child = progress,
            Height = 22,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(1),
            Background = Brush("#171D24"),
            BorderBrush = Brush("#596675"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true
        });

        var counts = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        for (var index = 0; index < 4; index++)
            counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddProgressStat(counts, pickedProgressCount, "✓", "#58D26E", "保留", 0);
        AddProgressStat(counts, rejectedProgressCount, "✕", "#F0667A", "废片", 1);
        AddProgressStat(counts, unprocessedProgressCount, "○", "#A6AFBA", "未处理", 2);
        AddProgressStat(counts, ratedProgressCount, "★", "#FFB84D", "已评分（可与旗标重叠）", 3);
        panel.Children.Add(counts);
        UpdateCullProgress();
        return panel;
    }

    private static void AddProgressStat(Grid host, TextBlock block, string symbol, string color, string toolTip, int column)
    {
        block.FontSize = 12;
        block.FontWeight = FontWeights.SemiBold;
        block.Foreground = Brush(color);
        block.HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : column == 3 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        block.ToolTip = toolTip;
        block.Text = symbol + " 0";
        Grid.SetColumn(block, column);
        host.Children.Add(block);
    }

    private void UpdateCullProgress()
    {
        var currentFiles = new List<string>();
        var root = RootFolder(false);
        if (root != null) currentFiles = FilesIn(Path.Combine(root, "jpg")).Where(IsJpg).ToList();
        var total = currentFiles.Count;
        var picked = currentFiles.Count(pickedFiles.Contains);
        var rejected = currentFiles.Count(rejectedFiles.Contains);
        var processed = picked + rejected;
        var unprocessed = Math.Max(0, total - processed);
        var rated = currentFiles.Count(file => starRatings.ContainsKey(file) && starRatings[file] > 0);
        var percent = total > 0 ? (int)Math.Round(processed * 100.0 / total) : 0;

        cullProgressCount.Text = processed.ToString("N0") + " / " + total.ToString("N0");
        cullProgressPercent.Text = percent + "%";
        pickedProgressCount.Text = "✓ " + picked.ToString("N0");
        rejectedProgressCount.Text = "✕ " + rejected.ToString("N0");
        unprocessedProgressCount.Text = "○ " + unprocessed.ToString("N0");
        ratedProgressCount.Text = "★ " + rated.ToString("N0");
        cullProgressFillColumn.Width = new GridLength(total > 0 ? processed : 0, GridUnitType.Star);
        cullProgressRemainingColumn.Width = new GridLength(total > 0 ? total - processed : 1, GridUnitType.Star);
    }

    private UIElement BuildGalleryCard()
    {
        return singleViewMode ? BuildSingleView() : BuildGridView();
    }

    private UIElement BuildGridView()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(SectionTitle("图库"));
        var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        galleryCaption.Text = tileFiles.Count > 0 ? tileFiles.Count + " 张 JPG · 单击选择，双击进入单张视图" : "创建分类后，这里会显示缩略图。";
        galleryCaption.Foreground = Brush("#66778B");
        galleryCaption.VerticalAlignment = VerticalAlignment.Center;
        topRight.Children.Add(galleryCaption);
        topRight.Children.Add(BuildFilterButton());
        topRight.Children.Add(BuildSortButton());
        topRight.Children.Add(BuildThumbnailSizeButton());
        topRight.Children.Add(BuildAutoAdvanceButton());
        topRight.Children.Add(BuildViewButton(true));
        topRight.Children.Add(BuildViewButton(false));
        Grid.SetColumn(topRight, 1);
        title.Children.Add(topRight);
        panel.Children.Add(title);
        galleryScrollViewer = new ScrollViewer { Margin = new Thickness(0, 14, 0, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brush("#FBFCFE"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 10, 20, 10) };
        var miniScrollBar = CreateMiniVerticalScrollBar();
        var syncingMiniScrollBar = false;
        galleryScrollViewer.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
        {
            syncingMiniScrollBar = true;
            miniScrollBar.Maximum = Math.Max(0, e.ExtentHeight - e.ViewportHeight);
            miniScrollBar.ViewportSize = Math.Max(0, e.ViewportHeight);
            miniScrollBar.Value = Math.Min(miniScrollBar.Maximum, Math.Max(0, e.VerticalOffset));
            miniScrollBar.Visibility = miniScrollBar.Maximum > 0.5 ? Visibility.Visible : Visibility.Collapsed;
            syncingMiniScrollBar = false;
            QueueVisibleThumbnailLoads();
        };
        miniScrollBar.ValueChanged += delegate
        {
            if (!syncingMiniScrollBar) galleryScrollViewer.ScrollToVerticalOffset(miniScrollBar.Value);
        };
        galleryScrollViewer.SizeChanged += delegate { QueueVisibleThumbnailLoads(); };
        galleryScrollViewer.Loaded += delegate { QueueVisibleThumbnailLoads(); };
        gallery.HorizontalAlignment = HorizontalAlignment.Stretch;
        galleryScrollViewer.Content = gallery;
        Grid.SetRow(galleryScrollViewer, 1);
        panel.Children.Add(galleryScrollViewer);
        Grid.SetRow(miniScrollBar, 1);
        panel.Children.Add(miniScrollBar);
        return panel;
    }

    private UIElement BuildSingleView()
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(SectionTitle("单张视图"));
        singleCaption.HorizontalAlignment = HorizontalAlignment.Center;
        singleCaption.VerticalAlignment = VerticalAlignment.Center;
        singleCaption.Foreground = Brush("#66778B");
        singleCaption.FontSize = 13;
        Grid.SetColumn(singleCaption, 1);
        header.Children.Add(singleCaption);
        var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        topRight.Children.Add(BuildAutoAdvanceButton());
        topRight.Children.Add(BuildViewButton(true));
        topRight.Children.Add(BuildViewButton(false));
        Grid.SetColumn(topRight, 2);
        header.Children.Add(topRight);
        panel.Children.Add(header);

        var frame = new Border { Margin = new Thickness(0, 8, 0, 0), Background = Brush("#151A20"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        var stage = new Grid();
        singleViewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Background = Brushes.Transparent, Focusable = true, Cursor = Cursors.Hand };
        singleImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        singleViewer.Content = singleImage;
        singleViewer.PreviewMouseWheel += SingleViewerMouseWheel;
        singleViewer.PreviewMouseLeftButtonDown += SingleViewerMouseLeftButtonDown;
        singleViewer.PreviewMouseMove += SingleViewerMouseMove;
        singleViewer.PreviewMouseLeftButtonUp += SingleViewerMouseLeftButtonUp;
        singleViewer.ScrollChanged += delegate { QueueNavigatorUpdate(); };
        singleViewer.SizeChanged += delegate { if (fitSingleImage) ApplySingleZoom(); };
        stage.Children.Add(singleViewer);
        stage.Children.Add(BuildNavigator());
        var stripFrame = BuildFilmStrip();
        // Keep navigation and zoom as separate layers: zoom above, filmstrip below.
        stripFrame.Margin = new Thickness(0, 0, 0, 12);
        stripFrame.HorizontalAlignment = HorizontalAlignment.Center;
        stripFrame.VerticalAlignment = VerticalAlignment.Bottom;
        stage.Children.Add(stripFrame);
        var controlsFrame = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 80), Padding = new Thickness(5, 4, 5, 4), Background = Brush("#202833"), BorderBrush = Brush("#3F4B58"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var previous = SingleToolButton("‹", 30, "上一张");
        previous.Margin = new Thickness(0, 0, 4, 0);
        previous.Click += delegate { NavigateSingle(-1); };
        controls.Children.Add(previous);
        var zoomOut = SingleToolButton("−", 28, "缩小");
        zoomOut.Margin = new Thickness(0, 0, 4, 0);
        zoomOut.Click += delegate { ChangeSingleZoom(1.0 / 1.25); };
        controls.Children.Add(zoomOut);
        zoomBox.Width = 52;
        zoomBox.Height = 26;
        zoomBox.Padding = new Thickness(2, 3, 2, 2);
        zoomBox.TextAlignment = TextAlignment.Center;
        zoomBox.VerticalContentAlignment = VerticalAlignment.Center;
        zoomBox.FontSize = 11;
        zoomBox.Background = Brush("#171D24");
        zoomBox.Foreground = Brushes.White;
        zoomBox.BorderBrush = Brush("#4B5A6B");
        zoomBox.BorderThickness = new Thickness(1);
        zoomBox.Margin = new Thickness(0, 0, 4, 0);
        StyleZoomBox(zoomBox);
        zoomBox.KeyDown += ZoomBoxKeyDown;
        zoomBox.LostFocus += delegate { ApplyZoomText(); };
        controls.Children.Add(zoomBox);
        var fit = SingleToolButton("适应", 42, "适应窗口");
        fit.Margin = new Thickness(0, 0, 4, 0);
        fit.Click += delegate { fitSingleImage = true; ApplySingleZoom(); };
        controls.Children.Add(fit);
        var zoomIn = SingleToolButton("+", 28, "放大");
        zoomIn.Margin = new Thickness(0, 0, 4, 0);
        zoomIn.Click += delegate { ChangeSingleZoom(1.25); };
        controls.Children.Add(zoomIn);
        var next = SingleToolButton("›", 30, "下一张");
        next.Click += delegate { NavigateSingle(1); };
        controls.Children.Add(next);
        controlsFrame.Child = controls;
        stage.Children.Add(controlsFrame);
        frame.Child = stage;
        Grid.SetRow(frame, 1);
        panel.Children.Add(frame);
        LoadSinglePhoto();
        return panel;
    }

    private UIElement BuildNavigator()
    {
        var frame = new Border
        {
            Width = 208,
            Height = 154,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12),
            Padding = new Thickness(8, 6, 8, 8),
            Background = darkMode ? Brush("#E61B222B") : Brush("#F2FFFFFF"),
            BorderBrush = darkMode ? Brush("#526170") : Brush("#B8C5D2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        frame.ToolTip = "点击或拖动取景框以定位；适应窗口时会按当前适应比例切换到 100% 或 200%";

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(new TextBlock
        {
            Text = "导航器",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = darkMode ? Brush("#D8E2EE") : Brush("#34475A"),
            Margin = new Thickness(1, 0, 0, 5)
        });

        navigatorCanvas = new Canvas
        {
            Width = 190,
            Height = 116,
            Background = Brushes.Black,
            ClipToBounds = true,
            Cursor = Cursors.Cross
        };
        navigatorImage = new Image
        {
            Width = 190,
            Height = 116,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(navigatorImage, BitmapScalingMode.HighQuality);
        navigatorViewport = new Border
        {
            BorderBrush = Brush("#66AEFF"),
            BorderThickness = new Thickness(1.5),
            Background = Brush("#1A4F9BFF"),
            IsHitTestVisible = false
        };
        navigatorCanvas.Children.Add(navigatorImage);
        navigatorCanvas.Children.Add(navigatorViewport);
        navigatorCanvas.PreviewMouseLeftButtonDown += NavigatorMouseLeftButtonDown;
        navigatorCanvas.PreviewMouseMove += NavigatorMouseMove;
        navigatorCanvas.PreviewMouseLeftButtonUp += NavigatorMouseLeftButtonUp;
        navigatorCanvas.LostMouseCapture += delegate { navigatorDragging = false; };
        Grid.SetRow(navigatorCanvas, 1);
        panel.Children.Add(navigatorCanvas);
        frame.Child = panel;
        return frame;
    }

    private Rect NavigatorImageRect()
    {
        var source = navigatorImage.Source as BitmapSource;
        var width = navigatorCanvas.ActualWidth > 0 ? navigatorCanvas.ActualWidth : navigatorCanvas.Width;
        var height = navigatorCanvas.ActualHeight > 0 ? navigatorCanvas.ActualHeight : navigatorCanvas.Height;
        if (source == null || width <= 0 || height <= 0 || source.Width <= 0 || source.Height <= 0)
            return Rect.Empty;
        var scale = Math.Min(width / source.Width, height / source.Height);
        var displayWidth = source.Width * scale;
        var displayHeight = source.Height * scale;
        return new Rect((width - displayWidth) / 2.0, (height - displayHeight) / 2.0, displayWidth, displayHeight);
    }

    private void QueueNavigatorUpdate()
    {
        if (navigatorUpdateQueued || Dispatcher.HasShutdownStarted) return;
        navigatorUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
        {
            navigatorUpdateQueued = false;
            UpdateNavigatorViewport();
        }));
    }

    private void UpdateNavigatorViewport()
    {
        var source = singleImage.Source as BitmapSource;
        var imageRect = NavigatorImageRect();
        if (source == null || imageRect.IsEmpty)
        {
            navigatorViewport.Visibility = Visibility.Collapsed;
            return;
        }

        navigatorViewport.Visibility = Visibility.Visible;
        var scaledWidth = source.Width * singleZoom;
        var scaledHeight = source.Height * singleZoom;
        var viewportWidth = Math.Max(1, singleViewer.ViewportWidth);
        var viewportHeight = Math.Max(1, singleViewer.ViewportHeight);
        var rectWidth = scaledWidth <= viewportWidth ? imageRect.Width : imageRect.Width * viewportWidth / scaledWidth;
        var rectHeight = scaledHeight <= viewportHeight ? imageRect.Height : imageRect.Height * viewportHeight / scaledHeight;
        rectWidth = Math.Max(8, Math.Min(imageRect.Width, rectWidth));
        rectHeight = Math.Max(8, Math.Min(imageRect.Height, rectHeight));

        var x = imageRect.Left;
        var y = imageRect.Top;
        if (singleViewer.ScrollableWidth > 0.5)
            x += (imageRect.Width - rectWidth) * singleViewer.HorizontalOffset / singleViewer.ScrollableWidth;
        if (singleViewer.ScrollableHeight > 0.5)
            y += (imageRect.Height - rectHeight) * singleViewer.VerticalOffset / singleViewer.ScrollableHeight;

        navigatorViewport.Width = rectWidth;
        navigatorViewport.Height = rectHeight;
        Canvas.SetLeft(navigatorViewport, x);
        Canvas.SetTop(navigatorViewport, y);
    }

    private void NavigatorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (navigatorImage.Source == null) return;
        navigatorDragging = true;
        navigatorCanvas.CaptureMouse();
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        e.Handled = true;
    }

    private void NavigatorMouseMove(object sender, MouseEventArgs e)
    {
        if (!navigatorDragging || !navigatorCanvas.IsMouseCaptured) return;
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        e.Handled = true;
    }

    private void NavigatorMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!navigatorDragging) return;
        CenterSingleViewFromNavigator(e.GetPosition(navigatorCanvas));
        navigatorDragging = false;
        navigatorCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CenterSingleViewFromNavigator(Point point)
    {
        var source = singleImage.Source as BitmapSource;
        var imageRect = NavigatorImageRect();
        if (source == null || imageRect.IsEmpty) return;

        var normalizedX = Math.Max(0, Math.Min(1, (point.X - imageRect.Left) / imageRect.Width));
        var normalizedY = Math.Max(0, Math.Min(1, (point.Y - imageRect.Top) / imageRect.Height));
        if (fitSingleImage || (singleViewer.ScrollableWidth < 0.5 && singleViewer.ScrollableHeight < 0.5))
        {
            fitSingleImage = false;
            singleZoom = GetSingleInspectionZoom();
            ApplySingleZoom();
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
        {
            var scaledWidth = source.Width * singleZoom;
            var scaledHeight = source.Height * singleZoom;
            var horizontal = normalizedX * scaledWidth - singleViewer.ViewportWidth / 2.0;
            var vertical = normalizedY * scaledHeight - singleViewer.ViewportHeight / 2.0;
            singleViewer.ScrollToHorizontalOffset(Math.Max(0, Math.Min(singleViewer.ScrollableWidth, horizontal)));
            singleViewer.ScrollToVerticalOffset(Math.Max(0, Math.Min(singleViewer.ScrollableHeight, vertical)));
            QueueNavigatorUpdate();
        }));
    }

    private Button BuildFilterButton()
    {
        filterButton = SecondaryButton(FilterLabel(), 0);
        filterButton.Width = 94;
        filterButton.Height = 28;
        filterButton.FontSize = 11;
        filterButton.Margin = new Thickness(10, 0, 0, 0);
        filterButton.ToolTip = "筛选挑片标记";
        filterButton.Click += OpenFilterMenu;
        return filterButton;
    }

    private Button BuildSortButton()
    {
        var button = SecondaryButton(SortLabel(), 88);
        button.Height = 28;
        button.FontSize = 11;
        button.Margin = new Thickness(8, 0, 0, 0);
        button.ToolTip = "选择图库排序方式";
        button.Click += delegate
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
            AddSortMenuItem(menu, "名称升序", "name_asc");
            AddSortMenuItem(menu, "名称降序", "name_desc");
            AddSortMenuItem(menu, "拍摄时间升序", "capture_asc");
            AddSortMenuItem(menu, "拍摄时间降序", "capture_desc");
            menu.IsOpen = true;
        };
        return button;
    }

    private void AddSortMenuItem(ContextMenu menu, string text, string value)
    {
        var item = new MenuItem { Header = text };
        StyleContextMenuItem(item);
        item.Click += delegate
        {
            gallerySortMode = value;
            SaveSettings();
            RebuildUi();
        };
        menu.Items.Add(item);
    }

    private string SortLabel()
    {
        switch (gallerySortMode)
        {
            case "name_desc": return "名称 ↓";
            case "capture_asc": return "拍摄时间 ↑";
            case "capture_desc": return "拍摄时间 ↓";
            default: return "名称 ↑";
        }
    }

    private Button BuildThumbnailSizeButton()
    {
        var button = SecondaryButton(ThumbnailSizeLabel(), 76);
        button.Height = 28;
        button.FontSize = 11;
        button.Margin = new Thickness(8, 0, 0, 0);
        button.ToolTip = "选择网格缩略图大小";
        button.Click += delegate
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
            AddThumbnailSizeMenuItem(menu, "小", 160);
            AddThumbnailSizeMenuItem(menu, "中", 196);
            AddThumbnailSizeMenuItem(menu, "大", 232);
            AddThumbnailSizeMenuItem(menu, "特大", 280);
            menu.IsOpen = true;
        };
        return button;
    }

    private Button BuildAutoAdvanceButton()
    {
        autoAdvanceButton = new Button
        {
            Width = 88,
            Height = 28,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            ToolTip = "Caps Lock 切换自动前进"
        };
        UpdateAutoAdvanceButtonAppearance();
        autoAdvanceButton.Click += delegate { ToggleAutoAdvance(); };
        return autoAdvanceButton;
    }

    private void UpdateAutoAdvanceButtonAppearance()
    {
        if (autoAdvanceButton == null) return;
        autoAdvanceButton.Content = autoAdvanceEnabled ? "自动前进 开" : "自动前进 关";
        autoAdvanceButton.Background = Brush(ButtonBlue);
        autoAdvanceButton.BorderBrush = autoAdvanceEnabled ? Brush("#A9C7E8") : Brush(ButtonBlue);
        autoAdvanceButton.Foreground = Brushes.White;
        autoAdvanceButton.Opacity = autoAdvanceEnabled ? 1.0 : 0.82;
        ApplyRoundedButtonTemplate(autoAdvanceButton, 7);
    }

    private void ToggleAutoAdvance()
    {
        autoAdvanceEnabled = !autoAdvanceEnabled;
        SaveSettings();
        UpdateAutoAdvanceButtonAppearance();
        SetStatus("自动前进已" + (autoAdvanceEnabled ? "开启" : "关闭") + "。Caps Lock 可随时切换。", false);
    }

    private void AddThumbnailSizeMenuItem(ContextMenu menu, string text, double width)
    {
        var item = new MenuItem { Header = "缩略图：" + text };
        StyleContextMenuItem(item);
        item.Click += delegate
        {
            gridTileWidth = width;
            SaveSettings();
            RebuildUi();
        };
        menu.Items.Add(item);
    }

    private string ThumbnailSizeLabel()
    {
        if (gridTileWidth <= 170) return "缩略图 小";
        if (gridTileWidth <= 210) return "缩略图 中";
        if (gridTileWidth <= 250) return "缩略图 大";
        return "缩略图 特大";
    }

    private string FilterLabel()
    {
        switch (activeFilter)
        {
            case "picked": return "保留";
            case "rejected": return "废片";
            case "rated": return "已评分";
            case "unmarked": return "未标记";
            default: return "全部照片";
        }
    }

    private void OpenFilterMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = filterButton, Placement = PlacementMode.Bottom, Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
        AddFilterMenuItem(menu, "全部照片", "all");
        AddFilterMenuItem(menu, "旗标：保留", "picked");
        AddFilterMenuItem(menu, "旗标：废片", "rejected");
        AddFilterMenuItem(menu, "已有星级", "rated");
        AddFilterMenuItem(menu, "未标记", "unmarked");
        menu.IsOpen = true;
    }

    private void AddFilterMenuItem(ContextMenu menu, string text, string value)
    {
        var item = new MenuItem { Header = text };
        StyleContextMenuItem(item);
        item.Click += delegate { activeFilter = value; RebuildUi(); };
        menu.Items.Add(item);
    }

    private Border BuildFilmStrip()
    {
        EnsureGalleryFiles();
        filmStrip = new StackPanel { Orientation = Orientation.Horizontal };
        filmTiles.Clear();
        lazyFilmThumbnailFiles.Clear();
        loadingFilmThumbnailTiles.Clear();
        filmThumbnailLoadVersion++;
        foreach (var file in galleryFiles)
        {
            var tile = new Border { Width = 62, Height = 48, Margin = new Thickness(2, 0, 2, 0), Background = Brush("#151A20"), BorderBrush = Brush("#3F4955"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(file) };
            tile.Child = new TextBlock { Text = "…", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            lazyFilmThumbnailFiles[tile] = file;
            tile.MouseLeftButtonDown += delegate { currentSingleFile = file; ShowPhotoInfo(file); LoadSinglePhoto(); UpdateFilmStripSelection(); };
            filmTiles[tile] = file;
            filmStrip.Children.Add(tile);
        }
        filmStripScrollViewer = new ScrollViewer { Content = filmStrip, Width = 450, Height = 52, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Brushes.Transparent };
        filmStripScrollViewer.ScrollChanged += delegate { QueueVisibleFilmThumbnailLoads(); };
        filmStripScrollViewer.SizeChanged += delegate { QueueVisibleFilmThumbnailLoads(); };
        filmStripScrollViewer.Loaded += delegate
        {
            UpdateFilmStripSelection();
            QueueVisibleFilmThumbnailLoads();
        };
        return new Border { Child = filmStripScrollViewer, Padding = new Thickness(3, 2, 3, 2), Background = Brush("#202833"), BorderBrush = Brush("#3F4B58"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MaxWidth = 460 };
    }

    private void UpdateFilmStripSelection()
    {
        Border activeTile = null;
        foreach (var item in filmTiles)
        {
            var active = String.Equals(item.Value, currentSingleFile, StringComparison.OrdinalIgnoreCase);
            item.Key.BorderBrush = active ? Brush("#2678D4") : Brush("#3F4955");
            item.Key.BorderThickness = new Thickness(active ? 2 : 1);
            item.Key.Opacity = active ? 1.0 : 0.65;
            if (active) activeTile = item.Key;
        }
        if (activeTile != null && activeTile.IsLoaded) activeTile.BringIntoView();
        QueueVisibleFilmThumbnailLoads();
    }

    private void QueueVisibleFilmThumbnailLoads()
    {
        if (!singleViewMode || filmThumbnailLoadQueued || Dispatcher.HasShutdownStarted) return;
        filmThumbnailLoadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            filmThumbnailLoadQueued = false;
            LoadVisibleFilmThumbnails();
        }));
    }

    private void LoadVisibleFilmThumbnails()
    {
        if (!singleViewMode || filmStripScrollViewer == null || !filmStripScrollViewer.IsLoaded) return;
        var version = filmThumbnailLoadVersion;
        foreach (var item in lazyFilmThumbnailFiles.ToList())
        {
            var tile = item.Key;
            if (tile.Child is Image || loadingFilmThumbnailTiles.Contains(tile) || !IsFilmThumbnailNearViewport(tile)) continue;
            loadingFilmThumbnailTiles.Add(tile);
            var file = item.Value;
            Task.Factory.StartNew(delegate { return LoadOrientedJpeg(file, 110); }).ContinueWith(delegate(Task<BitmapSource> task)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    loadingFilmThumbnailTiles.Remove(tile);
                    string expected;
                    if (version != filmThumbnailLoadVersion || !lazyFilmThumbnailFiles.TryGetValue(tile, out expected) ||
                        !String.Equals(expected, file, StringComparison.OrdinalIgnoreCase)) return;
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        var image = new Image
                        {
                            Source = task.Result,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                        tile.Child = image;
                    }
                    else
                    {
                        tile.Child = new TextBlock { Text = "×", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    }
                }));
            });
        }
    }

    private bool IsFilmThumbnailNearViewport(Border tile)
    {
        if (!tile.IsLoaded || filmStripScrollViewer.ViewportWidth <= 0) return false;
        try
        {
            var bounds = tile.TransformToAncestor(filmStripScrollViewer).TransformBounds(
                new Rect(0, 0, Math.Max(1, tile.ActualWidth), Math.Max(1, tile.ActualHeight)));
            var buffer = filmStripScrollViewer.ViewportWidth;
            return bounds.Right >= -buffer && bounds.Left <= filmStripScrollViewer.ViewportWidth + buffer;
        }
        catch { return false; }
    }

    private Button BuildViewButton(bool gridView)
    {
        var active = singleViewMode != gridView;
        var button = new Button { Content = gridView ? "▦" : "▣", ToolTip = gridView ? "网格视图" : "单张视图", Margin = new Thickness(8, 0, 0, 0) };
        button.Width = 32;
        button.Height = 28;
        button.FontSize = 18;
        button.Padding = new Thickness(0, -2, 0, 0);
        button.Cursor = Cursors.Hand;
        button.BorderThickness = new Thickness(1);
        button.Background = Brush(ButtonBlue);
        button.BorderBrush = active ? Brush("#A9C7E8") : Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        button.Opacity = active ? 1.0 : 0.82;
        StyleViewButton(button);
        button.Click += delegate { SetViewMode(!gridView); };
        return button;
    }

    private static Button SingleToolButton(string text, double width, string toolTip)
    {
        var button = new Button { Content = text, Width = width, Height = 26, ToolTip = toolTip, Cursor = Cursors.Hand, FontSize = 13, FontWeight = FontWeights.SemiBold, Padding = new Thickness(0), Background = Brush(ButtonBlue), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, button.Background);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
        return button;
    }

    private static void StyleZoomBox(TextBox box)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, box.Background);
        border.SetValue(Border.BorderBrushProperty, box.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, box.BorderThickness);
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.BackgroundProperty, Brushes.Transparent);
        host.SetValue(ScrollViewer.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox));
        template.VisualTree = border;
        box.Template = template;
    }

    private static void StyleRoundedTextBox(TextBox box, CornerRadius radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, box.Background);
        border.SetValue(Border.BorderBrushProperty, box.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, box.BorderThickness);
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.BackgroundProperty, Brushes.Transparent);
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox));
        template.VisualTree = border;
        box.Template = template;
    }

    private UIElement BuildPhotoInfoCard()
    {
        var panel = new Grid();
        if (String.IsNullOrWhiteSpace(photoInfo.Text)) photoInfo.Text = "点击缩略图查看拍摄信息";
        photoInfo.FontSize = 13;
        photoInfo.Foreground = Brush("#66778B");
        photoInfo.TextWrapping = TextWrapping.NoWrap;
        photoInfo.TextTrimming = TextTrimming.CharacterEllipsis;
        photoInfo.VerticalAlignment = VerticalAlignment.Center;
        photoInfo.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(photoInfo);
        return panel;
    }

    private UIElement BuildFooter()
    {
        var footer = new StackPanel();
        footer.Children.Add(SectionTitle("操作"));
        var checkPairs = SecondaryButton("检查配对", 0);
        checkPairs.HorizontalAlignment = HorizontalAlignment.Stretch;
        checkPairs.Margin = new Thickness(0, 12, 0, 0);
        checkPairs.Click += CheckMissingPairs;
        footer.Children.Add(checkPairs);
        recycleSelectedButton.Content = "选中项移至回收站";
        StyleButton(recycleSelectedButton, true, 0);
        recycleSelectedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        recycleSelectedButton.Margin = new Thickness(0, 8, 0, 0);
        recycleSelectedButton.IsHitTestVisible = false;
        recycleSelectedButton.Opacity = 0.52;
        recycleSelectedButton.Click -= RecycleSelectedPairs;
        recycleSelectedButton.Click += RecycleSelectedPairs;
        footer.Children.Add(recycleSelectedButton);
        recycleRejectedButton.Content = "移除所有废片";
        StyleDestructiveButton(recycleRejectedButton, 0);
        recycleRejectedButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        recycleRejectedButton.Margin = new Thickness(0, 8, 0, 0);
        recycleRejectedButton.IsHitTestVisible = rejectedFiles.Count > 0;
        recycleRejectedButton.Opacity = rejectedFiles.Count > 0 ? 1.0 : 0.52;
        recycleRejectedButton.Click -= RecycleRejectedPairs;
        recycleRejectedButton.Click += RecycleRejectedPairs;
        footer.Children.Add(recycleRejectedButton);
        return footer;
    }

    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        using (var dialog = new Forms.FolderBrowserDialog { Description = "选择相机照片所在的目录", ShowNewFolderButton = false })
        {
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                folderBox.Text = dialog.SelectedPath;
                RememberFolder(dialog.SelectedPath);
                ScanDirectory();
            }
        }
    }

    private void FolderBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        ClearFullImageCache();
        ClearGallery();
        SetStatus("目录已变更，请扫描或创建分类。", false);
    }

    private void OpenFolderHistory(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Background = darkMode ? Brush("#242A31") : Brush("#FFFFFF"), Foreground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937"), PlacementTarget = folderHistoryButton, Placement = PlacementMode.Bottom };
        if (recentFolders.Count == 0)
        {
            var empty = new MenuItem { Header = "暂无最近目录", IsEnabled = false };
            StyleContextMenuItem(empty);
            menu.Items.Add(empty);
        }
        else
        {
            foreach (var folder in recentFolders)
            {
                var item = new MenuItem { Header = folder, Background = menu.Background, Foreground = menu.Foreground };
                StyleContextMenuItem(item);
                item.Click += delegate { folderBox.Text = folder; RefreshCurrentFolder(); };
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    private void ResetUiControls(string currentFolder)
    {
        folderBox = new TextBox { Text = currentFolder };
        folderHistoryButton = new Button();
        gallery = new WrapPanel();
        galleryScrollViewer = new ScrollViewer();
        status = new TextBlock();
        folderHint = new TextBlock();
        jpgCount = new TextBlock();
        rawCount = new TextBlock();
        pendingCount = new TextBlock();
        cullProgressCount = new TextBlock();
        cullProgressPercent = new TextBlock();
        pickedProgressCount = new TextBlock();
        rejectedProgressCount = new TextBlock();
        unprocessedProgressCount = new TextBlock();
        ratedProgressCount = new TextBlock();
        cullProgressFillColumn = new ColumnDefinition();
        cullProgressRemainingColumn = new ColumnDefinition();
        galleryCaption = new TextBlock();
        photoInfoTitle = new TextBlock();
        photoInfo = new TextBlock();
        singleImage = new Image();
        singleViewer = new ScrollViewer();
        singleCaption = new TextBlock();
        zoomBox = new TextBox();
        navigatorCanvas = new Canvas();
        navigatorImage = new Image();
        navigatorViewport = new Border();
        navigatorDragging = false;
        navigatorUpdateQueued = false;
        organizeButton = new Button();
        unorganizeButton = new Button();
        recycleSelectedButton = new Button();
        recycleRejectedButton = new Button();
        filterButton = new Button();
        filmStrip = new StackPanel();
        filmStripScrollViewer = new ScrollViewer();
        galleryThumbnailLoadVersion++;
        thumbnailLoadQueued = false;
        lazyThumbnailFiles.Clear();
        loadingThumbnailTiles.Clear();
        filmThumbnailLoadVersion++;
        filmThumbnailLoadQueued = false;
        lazyFilmThumbnailFiles.Clear();
        loadingFilmThumbnailTiles.Clear();
        tileFiles.Clear();
        filmTiles.Clear();
        selectedFiles.Clear();
        galleryFiles.Clear();
        selectionAnchor = null;
    }

    private void RememberFolder(string folder)
    {
        if (String.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        recentFolders.RemoveAll(item => String.Equals(item, folder, StringComparison.OrdinalIgnoreCase));
        recentFolders.Insert(0, folder);
        if (recentFolders.Count > 3) recentFolders.RemoveRange(3, recentFolders.Count - 3);
        SaveSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            foreach (var line in File.ReadAllLines(settingsPath))
            {
                if (line.StartsWith("last_folder=", StringComparison.OrdinalIgnoreCase))
                    lastFolder = line.Substring(12);
                else if (line.StartsWith("view=", StringComparison.OrdinalIgnoreCase))
                    singleViewMode = String.Equals(line.Substring(5), "single", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("current_photo=", StringComparison.OrdinalIgnoreCase))
                    currentSingleFile = line.Substring(14);
                else if (line.StartsWith("single_fit=", StringComparison.OrdinalIgnoreCase))
                    fitSingleImage = String.Equals(line.Substring(11), "true", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("single_zoom=", StringComparison.OrdinalIgnoreCase))
                {
                    double zoom;
                    if (Double.TryParse(line.Substring(12), NumberStyles.Float, CultureInfo.InvariantCulture, out zoom))
                        singleZoom = Math.Max(0.1, Math.Min(6.0, zoom));
                }
                else if (line.StartsWith("grid_tile_width=", StringComparison.OrdinalIgnoreCase))
                {
                    double width;
                    if (Double.TryParse(line.Substring(16), NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                        gridTileWidth = Math.Max(160, Math.Min(280, width));
                }
                else if (line.StartsWith("sort=", StringComparison.OrdinalIgnoreCase))
                    gallerySortMode = NormalizeSortMode(line.Substring(5));
                else if (line.StartsWith("filter=", StringComparison.OrdinalIgnoreCase))
                    activeFilter = NormalizeFilter(line.Substring(7));
                else if (line.StartsWith("auto_advance=", StringComparison.OrdinalIgnoreCase))
                    autoAdvanceEnabled = String.Equals(line.Substring(13), "true", StringComparison.OrdinalIgnoreCase);
                else if (line.StartsWith("folder=", StringComparison.OrdinalIgnoreCase))
                {
                    var folder = line.Substring(7);
                    if (!String.IsNullOrWhiteSpace(folder) && !recentFolders.Any(item => String.Equals(item, folder, StringComparison.OrdinalIgnoreCase)))
                        recentFolders.Add(folder);
                }
            }
            if (recentFolders.Count > 3) recentFolders.RemoveRange(3, recentFolders.Count - 3);
            if (String.IsNullOrWhiteSpace(lastFolder)) lastFolder = recentFolders.FirstOrDefault();
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var lines = new List<string>();
            var currentFolder = folderBox == null ? lastFolder : (folderBox.Text ?? String.Empty).Trim();
            lines.Add("last_folder=" + (currentFolder ?? String.Empty));
            lines.Add("view=" + (singleViewMode ? "single" : "grid"));
            lines.Add("current_photo=" + (currentSingleFile ?? String.Empty));
            lines.Add("single_fit=" + (fitSingleImage ? "true" : "false"));
            lines.Add("single_zoom=" + singleZoom.ToString("0.########", CultureInfo.InvariantCulture));
            lines.Add("grid_tile_width=" + gridTileWidth.ToString("0", CultureInfo.InvariantCulture));
            lines.Add("sort=" + gallerySortMode);
            lines.Add("filter=" + activeFilter);
            lines.Add("auto_advance=" + (autoAdvanceEnabled ? "true" : "false"));
            lines.AddRange(recentFolders.Take(3).Select(folder => "folder=" + folder));
            File.WriteAllLines(settingsPath, lines);
        }
        catch { }
    }

    private static string NormalizeSortMode(string value)
    {
        switch ((value ?? String.Empty).ToLowerInvariant())
        {
            case "name_desc":
            case "capture_asc":
            case "capture_desc":
                return value.ToLowerInvariant();
            // Migrate the two former modification-time settings to EXIF capture-time sorting.
            case "modified_asc":
                return "capture_asc";
            case "modified_desc":
                return "capture_desc";
            default:
                return "name_asc";
        }
    }

    private static string NormalizeFilter(string value)
    {
        switch ((value ?? String.Empty).ToLowerInvariant())
        {
            case "picked":
            case "rejected":
            case "rated":
            case "unmarked":
                return value.ToLowerInvariant();
            default:
                return "all";
        }
    }

    private void RestoreSession()
    {
        var folder = (folderBox.Text ?? String.Empty).Trim();
        if (!Directory.Exists(folder))
        {
            singleViewMode = false;
            SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
            return;
        }

        ScanDirectory();
        if (singleViewMode)
        {
            EnsureGalleryFiles();
            if (String.IsNullOrWhiteSpace(currentSingleFile) || !galleryFiles.Contains(currentSingleFile, StringComparer.OrdinalIgnoreCase))
                currentSingleFile = galleryFiles.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(currentSingleFile))
            {
                singleViewMode = false;
                RebuildUi();
                return;
            }
            ShowPhotoInfo(currentSingleFile);
            LoadSinglePhoto();
            UpdateFilmStripSelection();
        }
        else
        {
            RefreshGallery();
            RestoreGridCurrentPhoto();
        }
    }

    private void LoadCullMarks()
    {
        try
        {
            if (!File.Exists(cullMarksPath)) return;
            foreach (var line in File.ReadAllLines(cullMarksPath))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                var file = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                if (String.IsNullOrWhiteSpace(file)) continue;
                if (parts[0] == "P") pickedFiles.Add(file);
                else if (parts[0] == "X") rejectedFiles.Add(file);
                else if (parts[0] == "R" && parts.Length >= 3)
                {
                    int rating;
                    if (Int32.TryParse(parts[2], out rating) && rating > 0) starRatings[file] = rating;
                }
            }
        }
        catch { }
    }

    private void SaveCullMarks()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cullMarksPath));
            var lines = new List<string>();
            foreach (var file in pickedFiles) lines.Add("P|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var file in rejectedFiles) lines.Add("X|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(file)));
            foreach (var item in starRatings.Where(item => item.Value > 0)) lines.Add("R|" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(item.Key)) + "|" + item.Value);
            File.WriteAllLines(cullMarksPath, lines);
        }
        catch { }
    }

    private void EnsureGalleryFiles()
    {
        if (galleryFiles.Count > 0) return;
        var root = RootFolder(false);
        if (root != null) galleryFiles.AddRange(SortGalleryFiles(FilesIn(Path.Combine(root, "jpg")).Where(IsJpg)));
    }

    private List<string> SortGalleryFiles(IEnumerable<string> files)
    {
        var source = files.ToList();
        switch (gallerySortMode)
        {
            case "name_desc":
                return source.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            case "capture_asc":
                return source.OrderBy(CaptureSortBucket).ThenBy(CaptureSortValue).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            case "capture_desc":
                return source.OrderBy(CaptureSortBucket).ThenByDescending(CaptureSortValue).ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            default:
                return source.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private int CaptureSortBucket(string file)
    {
        return ReadCaptureDate(file).HasValue ? 0 : 1;
    }

    private DateTime CaptureSortValue(string file)
    {
        return ReadCaptureDate(file) ?? DateTime.MinValue;
    }

    private DateTime? ReadCaptureDate(string file)
    {
        DateTime? cached;
        if (captureDateCache.TryGetValue(file, out cached)) return cached;
        DateTime? result = null;
        try
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                var metadata = decoder.Frames.Count > 0 ? decoder.Frames[0].Metadata as BitmapMetadata : null;
                result = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36867}"));
                if (!result.HasValue) result = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36868}"));
                if (!result.HasValue) result = ParseExifDate(ReadMetadataValue(metadata, "/app1/ifd/{ushort=306}"));
            }
        }
        catch { }
        captureDateCache[file] = result;
        return result;
    }

    private void ScanDirectory()
    {
        var root = RootFolder(false);
        if (root == null) return;
        RememberFolder(root);
        var topLevel = FilesIn(root).ToList();
        var pendingJpg = topLevel.Count(IsJpg);
        var pendingRaw = topLevel.Count(IsRaw);
        var jpgFolder = Path.Combine(root, "jpg");
        var rawFolder = Path.Combine(root, "arw");
        var jpgTotal = FilesIn(jpgFolder).Count(IsJpg);
        var rawTotal = FilesIn(rawFolder).Count(IsRaw);
        jpgCount.Text = jpgTotal.ToString("N0") + " 张";
        rawCount.Text = rawTotal.ToString("N0") + " 张";
        pendingCount.Text = (pendingJpg + pendingRaw).ToString("N0") + " 个";
        UpdateCullProgress();
        organizeButton.IsHitTestVisible = pendingJpg + pendingRaw > 0;
        organizeButton.Opacity = pendingJpg + pendingRaw > 0 ? 1.0 : 0.52;
        unorganizeButton.IsHitTestVisible = jpgTotal + rawTotal > 0;
        unorganizeButton.Opacity = jpgTotal + rawTotal > 0 ? 1.0 : 0.52;
        folderHint.Text = pendingJpg + pendingRaw > 0 ? "目录顶层有待分类的照片" : "目录顶层没有待分类照片";
        var missing = FindMissingPairs(root);
        var pairSummary = missing.Item1.Count == 0 && missing.Item2.Count == 0
            ? "配对完整。"
            : "缺少 ARW 的 JPG " + missing.Item1.Count + " 张，缺少 JPG 的 ARW " + missing.Item2.Count + " 个。";
        SetStatus("扫描完成：jpg 文件夹 " + jpgTotal + " 张，arw 文件夹 " + rawTotal + " 张；" + pairSummary, false);
    }

    private void OrganizeFiles(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null) return;
        var files = FilesIn(root).ToList();
        var jpgs = files.Where(IsJpg).ToList();
        var raws = files.Where(IsRaw).ToList();
        if (jpgs.Count == 0 && raws.Count == 0)
        {
            MessageBox.Show("所选目录顶层没有 JPG、JPEG 或 ARW 文件。", "没有可分类文件", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var message = "将移动 " + jpgs.Count + " 个 JPG/JPEG 和 " + raws.Count + " 个 ARW：\n\n"
                    + Path.Combine(root, "jpg") + "\n" + Path.Combine(root, "arw")
                    + "\n\n同名文件不会被覆盖。继续吗？";
        if (MessageBox.Show(message, "确认分类", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Directory.CreateDirectory(Path.Combine(root, "jpg"));
        Directory.CreateDirectory(Path.Combine(root, "arw"));
        int moved = 0;
        int skipped = 0;
        foreach (var file in jpgs.Concat(raws))
        {
            var destination = Path.Combine(root, IsJpg(file) ? "jpg" : "arw", Path.GetFileName(file));
            if (File.Exists(destination)) { skipped++; continue; }
            try
            {
                File.Move(file, destination);
                RemapPhotoState(file, destination);
                moved++;
            }
            catch { skipped++; }
        }
        SaveCullMarks();
        ScanDirectory();
        RefreshGallery();
        var result = "已分类 " + moved + " 个文件。" + (skipped > 0 ? " " + skipped + " 个未移动（同名文件或访问错误）。" : "");
        SetStatus(result, false);
        MessageBox.Show(result, "分类完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UnorganizeFiles(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null) return;
        var jpgFolder = Path.Combine(root, "jpg");
        var rawFolder = Path.Combine(root, "arw");
        var jpgs = FilesIn(jpgFolder).Where(IsJpg).ToList();
        var raws = FilesIn(rawFolder).Where(IsRaw).ToList();
        if (jpgs.Count == 0 && raws.Count == 0)
        {
            MessageBox.Show("jpg 与 arw 文件夹中没有可还原的照片。", "没有可取消的分类", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var conflicts = jpgs.Concat(raws).Where(file => File.Exists(Path.Combine(root, Path.GetFileName(file)))).ToList();
        if (conflicts.Count > 0)
        {
            var preview = String.Join("\n", conflicts.Take(8).Select(Path.GetFileName));
            if (conflicts.Count > 8) preview += "\n……以及另外 " + (conflicts.Count - 8) + " 个文件";
            MessageBox.Show("根目录已存在以下同名文件，取消分类已停止，未移动任何照片：\n\n" + preview,
                "存在同名文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var message = "将把 " + jpgs.Count + " 个 JPG/JPEG 和 " + raws.Count + " 个 ARW 还原到：\n\n"
                    + root + "\n\n此操作不会覆盖同名文件。继续吗？";
        if (MessageBox.Show(message, "确认取消分类", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        int moved = 0;
        var failures = new List<string>();
        foreach (var file in jpgs.Concat(raws))
        {
            var destination = Path.Combine(root, Path.GetFileName(file));
            try
            {
                if (File.Exists(destination)) failures.Add(Path.GetFileName(file) + "（根目录已有同名文件）");
                else
                {
                    File.Move(file, destination);
                    RemapPhotoState(file, destination);
                    moved++;
                }
            }
            catch (Exception ex) { failures.Add(Path.GetFileName(file) + "（" + ex.Message + "）"); }
        }

        SaveCullMarks();

        TryDeleteEmptyPhotoFolder(jpgFolder);
        TryDeleteEmptyPhotoFolder(rawFolder);

        ScanDirectory();
        RefreshGallery();
        var result = "已还原 " + moved + " 个文件。";
        if (failures.Count > 0) result += " " + failures.Count + " 个未移动：\n" + String.Join("\n", failures.Take(8));
        SetStatus(result, failures.Count > 0);
        MessageBox.Show(result, failures.Count > 0 ? "取消分类部分完成" : "取消分类完成", MessageBoxButton.OK,
            failures.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static void TryDeleteEmptyPhotoFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder, false);
        }
        catch { }
    }

    private void RemapPhotoState(string source, string destination)
    {
        if (pickedFiles.Remove(source)) pickedFiles.Add(destination);
        if (rejectedFiles.Remove(source)) rejectedFiles.Add(destination);
        int rating;
        if (starRatings.TryGetValue(source, out rating))
        {
            starRatings.Remove(source);
            starRatings[destination] = rating;
        }
        if (selectedFiles.Remove(source)) selectedFiles.Add(destination);
        if (String.Equals(currentSingleFile, source, StringComparison.OrdinalIgnoreCase)) currentSingleFile = destination;
    }

    private void RefreshGallery()
    {
        ClearGallery();
        var root = RootFolder(false);
        if (root == null) return;
        var jpgFolder = Path.Combine(root, "jpg");
        var jpgs = SortGalleryFiles(FilesIn(jpgFolder).Where(IsJpg));
        galleryFiles.AddRange(jpgs);
        var visibleJpgs = jpgs.Where(MatchesFilter).ToList();
        if (visibleJpgs.Count == 0)
        {
            gallery.Children.Add(new TextBlock { Text = jpgs.Count == 0 ? "这里还没有 JPG。点击“创建分类”即可。" : "当前筛选没有匹配的照片。", Margin = new Thickness(20), FontSize = 15, Foreground = Brush("#6A7787") });
            galleryCaption.Text = jpgs.Count == 0 ? "没有可浏览的 JPG" : "筛选：" + FilterLabel() + " · 0 张";
            return;
        }
        foreach (var jpg in visibleJpgs) gallery.Children.Add(CreateTile(jpg));
        QueueVisibleThumbnailLoads();
        galleryCaption.Text = "筛选：" + FilterLabel() + " · " + visibleJpgs.Count + "/" + jpgs.Count + " 张 · 单击选择，双击单张浏览";
        SetStatus("快捷键：P 标记保留 · X / Delete 标记废片 · 1–5 星评分\n← / → 切换照片 · 空格切换单张与网格视图", false);
    }

    private bool MatchesFilter(string file)
    {
        switch (activeFilter)
        {
            case "picked": return pickedFiles.Contains(file);
            case "rejected": return rejectedFiles.Contains(file);
            case "rated": return starRatings.ContainsKey(file) && starRatings[file] > 0;
            case "unmarked": return !pickedFiles.Contains(file) && !rejectedFiles.Contains(file) && (!starRatings.ContainsKey(file) || starRatings[file] <= 0);
            default: return true;
        }
    }

    private void RefreshCurrentFolder()
    {
        ClearFullImageCache();
        ScanDirectory();
        RefreshGallery();
    }

    private void ClearGallery()
    {
        galleryThumbnailLoadVersion++;
        thumbnailLoadQueued = false;
        lazyThumbnailFiles.Clear();
        loadingThumbnailTiles.Clear();
        gallery.Children.Clear();
        tileFiles.Clear();
        galleryFiles.Clear();
        selectedFiles.Clear();
        selectionAnchor = null;
        recycleSelectedButton.IsHitTestVisible = false;
        recycleSelectedButton.Opacity = 0.52;
        recycleRejectedButton.IsHitTestVisible = rejectedFiles.Count > 0;
        recycleRejectedButton.Opacity = rejectedFiles.Count > 0 ? 1.0 : 0.52;
        photoInfoTitle.Text = "照片信息";
        photoInfo.Text = "点击缩略图查看拍摄信息";
        UpdateCullProgress();
    }

    private Border CreateTile(string file)
    {
        var previewHeight = Math.Round(gridTileWidth * 0.724);
        var tile = new Border { Width = gridTileWidth, Margin = new Thickness(6), Padding = new Thickness(6), Background = Brush("#FFFFFF"), BorderBrush = Brush("#D6E0EA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand };
        var stack = new StackPanel();
        var previewBorder = new Border { Height = previewHeight, Background = Brush("#EAF0F5"), CornerRadius = new CornerRadius(4), ClipToBounds = true };
        previewBorder.Child = new TextBlock { Text = "正在加载…", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        lazyThumbnailFiles[previewBorder] = file;
        var previewGrid = new Grid { Height = previewHeight };
        previewGrid.Children.Add(previewBorder);
        var mark = MarkLabel(file);
        if (!String.IsNullOrWhiteSpace(mark))
        {
            var badge = new Border { Background = Brush("#202833"), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.92 };
            badge.Child = new TextBlock { Text = mark, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            previewGrid.Children.Add(badge);
        }
        stack.Children.Add(previewGrid);
        stack.Children.Add(new TextBlock { Text = Path.GetFileName(file), Margin = new Thickness(1, 7, 1, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Path.GetFileName(file), FontSize = 12, Foreground = Brush("#2D3A4A") });
        tile.Child = stack;
        tileFiles.Add(tile, file);
        tile.MouseLeftButtonDown += TileMouseDown;
        tile.ContextMenu = BuildTileMenu(file);
        return tile;
    }

    private void QueueVisibleThumbnailLoads()
    {
        if (singleViewMode || thumbnailLoadQueued || Dispatcher.HasShutdownStarted) return;
        thumbnailLoadQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
        {
            thumbnailLoadQueued = false;
            LoadVisibleThumbnails();
        }));
    }

    private void LoadVisibleThumbnails()
    {
        if (singleViewMode || galleryScrollViewer == null || !galleryScrollViewer.IsLoaded) return;
        var version = galleryThumbnailLoadVersion;
        foreach (var item in lazyThumbnailFiles.ToList())
        {
            var preview = item.Key;
            if (preview.Child is Image || loadingThumbnailTiles.Contains(preview) || !IsThumbnailNearViewport(preview)) continue;
            loadingThumbnailTiles.Add(preview);
            var file = item.Value;
            var decodeWidth = (int)Math.Round(gridTileWidth * 1.33);
            Task.Factory.StartNew(delegate { return LoadOrientedJpeg(file, decodeWidth); }).ContinueWith(delegate(Task<BitmapSource> task)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    loadingThumbnailTiles.Remove(preview);
                    string expected;
                    if (version != galleryThumbnailLoadVersion || !lazyThumbnailFiles.TryGetValue(preview, out expected) ||
                        !String.Equals(expected, file, StringComparison.OrdinalIgnoreCase)) return;
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        var image = new Image
                        {
                            Source = task.Result,
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                        preview.Child = image;
                    }
                    else
                    {
                        preview.Child = new TextBlock { Text = "无法预览", Foreground = Brush("#75869A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    }
                }));
            });
        }
    }

    private bool IsThumbnailNearViewport(Border preview)
    {
        if (!preview.IsLoaded || galleryScrollViewer.ViewportHeight <= 0) return false;
        try
        {
            var bounds = preview.TransformToAncestor(galleryScrollViewer).TransformBounds(
                new Rect(0, 0, Math.Max(1, preview.ActualWidth), Math.Max(1, preview.ActualHeight)));
            var buffer = galleryScrollViewer.ViewportHeight;
            return bounds.Bottom >= -buffer && bounds.Top <= galleryScrollViewer.ViewportHeight + buffer;
        }
        catch { return false; }
    }

    private string MarkLabel(string file)
    {
        var parts = new List<string>();
        if (pickedFiles.Contains(file)) parts.Add("P");
        if (rejectedFiles.Contains(file)) parts.Add("X");
        int rating;
        if (starRatings.TryGetValue(file, out rating) && rating > 0) parts.Add(new String('★', rating));
        return String.Join(" ", parts.ToArray());
    }

    private ContextMenu BuildTileMenu(string file)
    {
        var menu = new ContextMenu { Background = Brush("#FFFFFF"), Foreground = Brush("#1F2937"), Padding = new Thickness(0), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1) };
        var open = new MenuItem { Header = "打开原图" };
        StyleContextMenuItem(open);
        open.Click += delegate { OpenPhoto(file); };
        menu.Items.Add(open);
        var pick = new MenuItem { Header = "标记为保留 (P)" };
        StyleContextMenuItem(pick);
        pick.Click += delegate { TogglePick(file); };
        menu.Items.Add(pick);
        var reject = new MenuItem { Header = "标记为废片 (X)" };
        StyleContextMenuItem(reject);
        reject.Click += delegate { ToggleReject(file); };
        menu.Items.Add(reject);
        var deleteJpg = new MenuItem { Header = "删除 JPG" };
        StyleContextMenuItem(deleteJpg);
        deleteJpg.Click += delegate { RecycleJpgOnly(file); };
        menu.Items.Add(deleteJpg);
        var deletePair = new MenuItem { Header = "删除 JPG + 同名 ARW" };
        StyleContextMenuItem(deletePair);
        deletePair.Click += delegate { RecycleSinglePair(file); };
        menu.Items.Add(deletePair);
        return menu;
    }

    private void TileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) OpenTile(sender, e);
        else ToggleTile(sender, e);
    }

    private void ToggleTile(object sender, MouseButtonEventArgs e)
    {
        var tile = sender as Border;
        if (tile == null || !tileFiles.ContainsKey(tile)) return;
        var file = tileFiles[tile];
        currentSingleFile = file;
        ShowPhotoInfo(file);
        var modifiers = Keyboard.Modifiers;
        var controlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (shiftPressed && selectionAnchor != null && tileFiles.ContainsKey(selectionAnchor))
        {
            if (!controlPressed) selectedFiles.Clear();
            var tiles = gallery.Children.OfType<Border>().Where(item => tileFiles.ContainsKey(item)).ToList();
            var start = tiles.IndexOf(selectionAnchor);
            var end = tiles.IndexOf(tile);
            if (start >= 0 && end >= 0)
            {
                var first = Math.Min(start, end);
                var last = Math.Max(start, end);
                for (var index = first; index <= last; index++) selectedFiles.Add(tileFiles[tiles[index]]);
            }
        }
        else if (controlPressed)
        {
            if (!selectedFiles.Add(file)) selectedFiles.Remove(file);
            selectionAnchor = tile;
        }
        else
        {
            selectedFiles.Clear();
            selectedFiles.Add(file);
            selectionAnchor = tile;
        }

        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = selectedFiles.Count > 0;
        recycleSelectedButton.Opacity = selectedFiles.Count > 0 ? 1.0 : 0.52;
        galleryCaption.Text = selectedFiles.Count > 0 ? "已选择 " + selectedFiles.Count + " 张 · 将配对 ARW 一并移至回收站" : tileFiles.Count + " 张 JPG · 单击选择，双击打开原图";
    }

    private void RestoreGridCurrentPhoto()
    {
        if (String.IsNullOrWhiteSpace(currentSingleFile) || !File.Exists(currentSingleFile)) return;
        var tile = tileFiles.FirstOrDefault(item => String.Equals(item.Value, currentSingleFile, StringComparison.OrdinalIgnoreCase)).Key;
        if (tile == null) return;
        selectedFiles.Clear();
        selectedFiles.Add(currentSingleFile);
        selectionAnchor = tile;
        ShowPhotoInfo(currentSingleFile);
        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = true;
        recycleSelectedButton.Opacity = 1.0;
    }

    private void UpdateTileSelectionAppearance()
    {
        foreach (var item in tileFiles)
        {
            var selected = selectedFiles.Contains(item.Value);
            item.Key.Background = selected ? Brush("#EAF3FF") : Brush("#FFFFFF");
            item.Key.BorderBrush = selected ? Brush("#2678D4") : Brush("#D6E0EA");
            item.Key.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.CapsLock)
        {
            if (!e.IsRepeat) ToggleAutoAdvance();
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is PasswordBox || Keyboard.FocusedElement is ComboBox) return;
        if (e.Key == Key.Left)
        {
            if (singleViewMode) NavigateSingle(-1); else SelectRelativeInGrid(-1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Right)
        {
            if (singleViewMode) NavigateSingle(1); else SelectRelativeInGrid(1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space)
        {
            SetViewMode(!singleViewMode);
            e.Handled = true;
            return;
        }
        var active = ActivePhotoFile();
        if (String.IsNullOrWhiteSpace(active)) return;
        if (e.Key == Key.P)
        {
            var next = NextPhotoForAutoAdvance(active);
            TogglePick(active, next, autoAdvanceEnabled);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.X || e.Key == Key.Delete)
        {
            var next = NextPhotoForAutoAdvance(active);
            ToggleReject(active, next, autoAdvanceEnabled);
            e.Handled = true;
            return;
        }
        var stars = StarKeyValue(e.Key);
        if (stars >= 0)
        {
            var shouldAdvance = autoAdvanceEnabled && stars > 0;
            var next = shouldAdvance ? NextPhotoForAutoAdvance(active) : null;
            SetStarRating(active, stars, next, shouldAdvance);
            e.Handled = true;
        }
    }

    private static int StarKeyValue(Key key)
    {
        if (key >= Key.D0 && key <= Key.D5) return (int)key - (int)Key.D0;
        if (key >= Key.NumPad0 && key <= Key.NumPad5) return (int)key - (int)Key.NumPad0;
        return -1;
    }

    private string ActivePhotoFile()
    {
        if (singleViewMode && !String.IsNullOrWhiteSpace(currentSingleFile)) return currentSingleFile;
        if (!String.IsNullOrWhiteSpace(currentSingleFile) && selectedFiles.Contains(currentSingleFile)) return currentSingleFile;
        var selected = selectedFiles.FirstOrDefault();
        if (!String.IsNullOrWhiteSpace(selected)) return selected;
        return galleryFiles.FirstOrDefault();
    }

    private string NextPhotoForAutoAdvance(string active)
    {
        if (!autoAdvanceEnabled || String.IsNullOrWhiteSpace(active)) return null;
        EnsureGalleryFiles();
        var sequence = singleViewMode ? galleryFiles.ToList() : galleryFiles.Where(MatchesFilter).ToList();
        var index = sequence.FindIndex(item => String.Equals(item, active, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < sequence.Count ? sequence[index + 1] : null;
    }

    private void SelectRelativeInGrid(int direction)
    {
        EnsureGalleryFiles();
        if (galleryFiles.Count == 0) return;
        var active = selectedFiles.FirstOrDefault();
        var index = galleryFiles.FindIndex(item => String.Equals(item, active, StringComparison.OrdinalIgnoreCase));
        if (index < 0) index = direction > 0 ? -1 : 0;
        index = (index + direction + galleryFiles.Count) % galleryFiles.Count;
        selectedFiles.Clear();
        selectedFiles.Add(galleryFiles[index]);
        currentSingleFile = galleryFiles[index];
        selectionAnchor = tileFiles.FirstOrDefault(item => String.Equals(item.Value, galleryFiles[index], StringComparison.OrdinalIgnoreCase)).Key;
        ShowPhotoInfo(galleryFiles[index]);
        UpdateTileSelectionAppearance();
        recycleSelectedButton.IsHitTestVisible = true;
        recycleSelectedButton.Opacity = 1.0;
    }

    private void TogglePick(string file, string nextFile = null, bool advanceRequested = false)
    {
        if (pickedFiles.Contains(file)) pickedFiles.Remove(file);
        else { pickedFiles.Add(file); rejectedFiles.Remove(file); }
        SaveCullMarks();
        ApplyCullChange(file, "保留旗标已更新。", nextFile, advanceRequested);
    }

    private void ToggleReject(string file, string nextFile = null, bool advanceRequested = false)
    {
        if (rejectedFiles.Contains(file)) rejectedFiles.Remove(file);
        else { rejectedFiles.Add(file); pickedFiles.Remove(file); }
        SaveCullMarks();
        ApplyCullChange(file, "废片旗标已更新。可点击“移除所有废片”统一清理。", nextFile, advanceRequested);
    }

    private void SetStarRating(string file, int rating, string nextFile = null, bool advanceRequested = false)
    {
        if (rating <= 0) starRatings.Remove(file);
        else starRatings[file] = rating;
        SaveCullMarks();
        ApplyCullChange(file, rating <= 0 ? "已清除星级。" : "已评为 " + rating + " 星。", nextFile, advanceRequested);
    }

    private void ApplyCullChange(string file, string message, string nextFile, bool advanceRequested)
    {
        var advanced = advanceRequested && !String.IsNullOrWhiteSpace(nextFile) && File.Exists(nextFile);
        currentSingleFile = advanced ? nextFile : file;
        if (singleViewMode)
        {
            RebuildUi();
            ShowPhotoInfo(currentSingleFile);
        }
        else RebuildUi();
        if (advanceRequested)
            message += advanced ? " 已自动前进到下一张。" : " 已到当前序列最后一张。";
        SetStatus(message, false);
    }

    private void OpenTile(object sender, MouseButtonEventArgs e)
    {
        var tile = sender as Border;
        if (tile == null || !tileFiles.ContainsKey(tile)) return;
        ShowSinglePhoto(tileFiles[tile]);
    }

    private void SetViewMode(bool single)
    {
        if (single && String.IsNullOrWhiteSpace(currentSingleFile))
        {
            currentSingleFile = selectedFiles.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(currentSingleFile)) currentSingleFile = galleryFiles.FirstOrDefault();
            if (String.IsNullOrWhiteSpace(currentSingleFile))
            {
                SetStatus("请先扫描并选择一张 JPG。", true);
                return;
            }
        }
        singleViewMode = single;
        RebuildUi();
        if (single && !String.IsNullOrWhiteSpace(currentSingleFile)) ShowPhotoInfo(currentSingleFile);
    }

    private void ShowSinglePhoto(string file)
    {
        if (String.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        currentSingleFile = file;
        singleViewMode = true;
        fitSingleImage = true;
        RebuildUi();
        ShowPhotoInfo(file);
    }

    private void RebuildUi()
    {
        // Every visible control must be recreated. Reusing even the folder input while the old tree is active
        // causes WPF to throw "already a logical child" during a grid/single-view switch.
        var currentFolder = folderBox.Text;
        ResetUiControls(currentFolder);
        Content = BuildUi();
        if (Directory.Exists(currentFolder.Trim()))
        {
            ScanDirectory();
            // BuildSingleView already enumerates the JPG folder for the filmstrip.
            // Rebuilding the hidden grid here decodes every thumbnail a second time
            // and was the main source of the visible pause when pressing Space.
            if (!singleViewMode)
            {
                RefreshGallery();
                RestoreGridCurrentPhoto();
            }
        }
        else SetStatus("选择一个相机照片目录，然后扫描或创建分类。", false);
    }

    private void NavigateSingle(int direction)
    {
        if (galleryFiles.Count == 0)
        {
            var root = RootFolder(false);
            if (root != null) galleryFiles.AddRange(SortGalleryFiles(FilesIn(Path.Combine(root, "jpg")).Where(IsJpg)));
        }
        var index = galleryFiles.FindIndex(item => String.Equals(item, currentSingleFile, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || galleryFiles.Count == 0) return;
        index = (index + direction + galleryFiles.Count) % galleryFiles.Count;
        currentSingleFile = galleryFiles[index];
        ShowPhotoInfo(currentSingleFile);
        LoadSinglePhoto();
        UpdateFilmStripSelection();
    }

    private void LoadSinglePhoto()
    {
        if (singleImage == null) return;
        var file = currentSingleFile;
        if (String.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        var targetImage = singleImage;
        var version = ++singleLoadVersion;
        singleCaption.Text = Path.GetFileName(file);
        singleViewer.ContextMenu = BuildTileMenu(file);
        targetImage.Source = null;
        navigatorImage.Source = null;
        GetFullImageTask(file).ContinueWith(delegate(Task<BitmapSource> task)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                if (version != singleLoadVersion || !singleViewMode || targetImage != singleImage ||
                    !String.Equals(file, currentSingleFile, StringComparison.OrdinalIgnoreCase)) return;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    targetImage.Source = task.Result;
                    navigatorImage.Source = task.Result;
                    ApplySingleZoom();
                    QueueNavigatorUpdate();
                    PreloadAdjacentPhotos(file);
                }
                else
                {
                    targetImage.Source = null;
                    navigatorImage.Source = null;
                    singleCaption.Text = "无法载入原图";
                    zoomBox.Text = "—";
                }
            }));
        });
    }

    private Task<BitmapSource> GetFullImageTask(string file)
    {
        lock (fullImageCacheLock)
        {
            BitmapSource cached;
            if (fullImageCache.TryGetValue(file, out cached))
            {
                TouchFullImageCacheLocked(file);
                var completion = new TaskCompletionSource<BitmapSource>();
                completion.SetResult(cached);
                return completion.Task;
            }

            Task<BitmapSource> existing;
            if (fullImageLoadTasks.TryGetValue(file, out existing)) return existing;

            var generation = fullImageCacheGeneration;
            var task = Task.Factory.StartNew(delegate { return LoadOrientedJpeg(file, 0); });
            fullImageLoadTasks[file] = task;
            task.ContinueWith(delegate(Task<BitmapSource> completed)
            {
                lock (fullImageCacheLock)
                {
                    fullImageLoadTasks.Remove(file);
                    if (completed.Status == TaskStatus.RanToCompletion && generation == fullImageCacheGeneration)
                        AddFullImageCacheLocked(file, completed.Result);
                }
            });
            return task;
        }
    }

    private void PreloadAdjacentPhotos(string file)
    {
        if (galleryFiles.Count < 2) return;
        var index = galleryFiles.FindIndex(item => String.Equals(item, file, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var previous = galleryFiles[(index - 1 + galleryFiles.Count) % galleryFiles.Count];
        var next = galleryFiles[(index + 1) % galleryFiles.Count];
        if (File.Exists(previous)) GetFullImageTask(previous);
        if (!String.Equals(previous, next, StringComparison.OrdinalIgnoreCase) && File.Exists(next)) GetFullImageTask(next);
    }

    private void AddFullImageCacheLocked(string file, BitmapSource image)
    {
        fullImageCache[file] = image;
        TouchFullImageCacheLocked(file);
        while (fullImageCacheLru.Count > 5)
        {
            var oldest = fullImageCacheLru.First.Value;
            fullImageCacheLru.RemoveFirst();
            fullImageCache.Remove(oldest);
        }
    }

    private void TouchFullImageCacheLocked(string file)
    {
        var node = fullImageCacheLru.Find(file);
        if (node != null) fullImageCacheLru.Remove(node);
        fullImageCacheLru.AddLast(file);
    }

    private void ClearFullImageCache()
    {
        lock (fullImageCacheLock)
        {
            fullImageCacheGeneration++;
            fullImageCache.Clear();
            fullImageLoadTasks.Clear();
            fullImageCacheLru.Clear();
        }
        captureDateCache.Clear();
    }

    private void SingleViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        ChangeSingleZoom(e.Delta > 0 ? 1.25 : 1.0 / 1.25);
        e.Handled = true;
    }

    private void SingleViewerMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (singleImage.Source == null) return;
        singlePanStart = e.GetPosition(singleViewer);
        singlePanHorizontal = singleViewer.HorizontalOffset;
        singlePanVertical = singleViewer.VerticalOffset;
        singlePanning = false;
        singleViewer.CaptureMouse();
    }

    private void SingleViewerMouseMove(object sender, MouseEventArgs e)
    {
        if (!singleViewer.IsMouseCaptured) return;
        var point = e.GetPosition(singleViewer);
        var dx = point.X - singlePanStart.X;
        var dy = point.Y - singlePanStart.Y;
        if (!singlePanning && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
        singlePanning = true;
        singleViewer.Cursor = Cursors.SizeAll;
        singleViewer.ScrollToHorizontalOffset(singlePanHorizontal - dx);
        singleViewer.ScrollToVerticalOffset(singlePanVertical - dy);
        e.Handled = true;
    }

    private void SingleViewerMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!singleViewer.IsMouseCaptured) return;
        singleViewer.ReleaseMouseCapture();
        singleViewer.Cursor = Cursors.Hand;
        if (singlePanning) { e.Handled = true; return; }
        ToggleSingleClickZoom();
        e.Handled = true;
    }

    private void ToggleSingleClickZoom()
    {
        if (singleImage.Source == null) return;
        if (fitSingleImage)
        {
            fitSingleImage = false;
            singleZoom = GetSingleInspectionZoom();
            ApplySingleZoom();
        }
        else
        {
            fitSingleImage = true;
            ApplySingleZoom();
        }
    }

    private double GetSingleInspectionZoom()
    {
        var source = singleImage.Source as BitmapSource;
        if (source == null) return 1.0;
        return CalculateSingleFitZoom(source) >= 1.0 ? 2.0 : 1.0;
    }

    private double CalculateSingleFitZoom(BitmapSource source)
    {
        if (source == null) return 1.0;
        var width = singleViewer.ActualWidth - 16;
        var height = singleViewer.ActualHeight - 16;
        if (width <= 0 || height <= 0 || source.Width <= 0 || source.Height <= 0)
            return Math.Max(0.1, Math.Min(6.0, singleZoom));
        return Math.Max(0.1, Math.Min(6.0, Math.Min(width / source.Width, height / source.Height)));
    }

    private void ZoomBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyZoomText();
        singleViewer.Focus();
        e.Handled = true;
    }

    private void ApplyZoomText()
    {
        var text = (zoomBox.Text ?? String.Empty).Trim().TrimEnd('%').Trim();
        double percent;
        if (!Double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out percent) && !Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            ApplySingleZoom();
            return;
        }
        fitSingleImage = false;
        singleZoom = Math.Max(0.1, Math.Min(6.0, percent / 100.0));
        ApplySingleZoom();
    }

    private void ChangeSingleZoom(double factor)
    {
        if (singleImage.Source == null) return;
        fitSingleImage = false;
        singleZoom = Math.Max(0.1, Math.Min(6.0, singleZoom * factor));
        ApplySingleZoom();
    }

    private void ApplySingleZoom()
    {
        var source = singleImage.Source as BitmapSource;
        if (source == null) return;
        if (fitSingleImage)
            singleZoom = CalculateSingleFitZoom(source);
        singleImage.LayoutTransform = new ScaleTransform(singleZoom, singleZoom);
        zoomBox.Text = Math.Round(singleZoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
        QueueNavigatorUpdate();
    }

    private void OpenPhoto(string file)
    {
        try { Process.Start(file); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "无法打开照片", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RecycleJpgOnly(string jpg)
    {
        if (!File.Exists(jpg)) return;
        var message = "将把 “" + Path.GetFileName(jpg) + "” 移到 Windows 回收站。\nARW 会保留，可稍后使用“检查配对”清理。\n\n确定继续吗？";
        if (MessageBox.Show(message, "确认删除 JPG", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = RecycleFiles(new[] { jpg });
        RefreshCurrentFolder();
        ShowRecycleResult(result.Item1, result.Item2);
    }

    private void RecycleSinglePair(string jpg)
    {
        var root = RootFolder(true);
        if (root == null || !File.Exists(jpg)) return;
        var targets = new List<string> { jpg };
        string raw;
        var hasRaw = RawByStem(Path.Combine(root, "arw")).TryGetValue(Stem(jpg), out raw) && File.Exists(raw);
        if (hasRaw) targets.Add(raw);
        var message = "将把 “" + Path.GetFileName(jpg) + "”" + (hasRaw ? " 及同名 ARW" : "（没有找到同名 ARW）") + " 移到 Windows 回收站。\n\n确定继续吗？";
        if (MessageBox.Show(message, "确认删除照片", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = RecycleFiles(targets);
        RefreshCurrentFolder();
        ShowRecycleResult(result.Item1, result.Item2);
    }

    private void RecycleSelectedPairs(object sender, RoutedEventArgs e)
    {
        if (selectedFiles.Count == 0) return;
        var root = RootFolder(true);
        if (root == null) return;
        var rawByStem = RawByStem(Path.Combine(root, "arw"));
        var targets = new List<string>();
        var paired = 0;
        foreach (var jpg in selectedFiles)
        {
            if (File.Exists(jpg)) targets.Add(jpg);
            string raw;
            if (rawByStem.TryGetValue(Stem(jpg), out raw) && File.Exists(raw)) { targets.Add(raw); paired++; }
        }
        var message = "将把 " + selectedFiles.Count + " 张选中的 JPG 和 " + paired + " 个同名 ARW 移到 Windows 回收站。\n\n文件可以从回收站恢复。确定继续吗？";
        if (MessageBox.Show(message, "确认淘汰选中照片", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = RecycleFiles(targets);
        RefreshGallery();
        ScanDirectory();
        ShowRecycleResult(result.Item1, result.Item2);
    }

    private void RecycleRejectedPairs(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null) return;
        var targetsJpg = rejectedFiles.Where(File.Exists).ToList();
        if (targetsJpg.Count == 0)
        {
            MessageBox.Show("还没有标记为废片的 JPG。按 X 或 Delete 可标记废片。", "没有废片", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rawByStem = RawByStem(Path.Combine(root, "arw"));
        var targets = new List<string>();
        var paired = 0;
        foreach (var jpg in targetsJpg)
        {
            targets.Add(jpg);
            string raw;
            if (rawByStem.TryGetValue(Stem(jpg), out raw) && File.Exists(raw)) { targets.Add(raw); paired++; }
        }
        var message = "将把 " + targetsJpg.Count + " 张标记为废片的 JPG 与 " + paired + " 个同名 ARW 移到 Windows 回收站。\n\n文件可以从回收站恢复。确定继续吗？";
        if (MessageBox.Show(message, "确认移除废片", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = RecycleFiles(targets);
        foreach (var jpg in targetsJpg)
        {
            rejectedFiles.Remove(jpg);
            pickedFiles.Remove(jpg);
            starRatings.Remove(jpg);
        }
        SaveCullMarks();
        RefreshCurrentFolder();
        ShowRecycleResult(result.Item1, result.Item2);
    }

    private void CheckMissingPairs(object sender, RoutedEventArgs e)
    {
        var root = RootFolder(true);
        if (root == null) return;
        var missing = FindMissingPairs(root);
        var jpgWithoutRaw = missing.Item1;
        var rawWithoutJpg = missing.Item2;
        if (jpgWithoutRaw.Count == 0 && rawWithoutJpg.Count == 0)
        {
            var pairs = FilesIn(Path.Combine(root, "jpg")).Count(IsJpg);
            MessageBox.Show("已核对 " + pairs + " 组照片。\n\n每张 JPG 都有同名 ARW，每个 ARW 也都有同名 JPG。", "配对完整", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("配对检查完成：JPG 与 ARW 一一对应。", false);
            return;
        }

        var text = "JPG 缺少同名 ARW：" + jpgWithoutRaw.Count + " 张\n"
                 + PairExamples(jpgWithoutRaw)
                 + "\n\nARW 缺少同名 JPG：" + rawWithoutJpg.Count + " 个\n"
                 + PairExamples(rawWithoutJpg);

        if (rawWithoutJpg.Count == 0)
        {
            MessageBox.Show(text + "\n\n没有可清理的无主 ARW；本次检查不会移动或删除文件。", "发现缺失配对", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("配对检查：缺少 ARW 的 JPG " + jpgWithoutRaw.Count + " 张。", false);
            return;
        }

        var question = text
                     + "\n\n是否把这 " + rawWithoutJpg.Count + " 个无主 ARW 移到 Windows 回收站？"
                     + "\n缺少 ARW 的 JPG 不会被处理。";
        if (MessageBox.Show(question, "检查配对", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            SetStatus("配对检查：缺少 ARW 的 JPG " + jpgWithoutRaw.Count + " 张，无主 ARW " + rawWithoutJpg.Count + " 个。", false);
            return;
        }

        var result = RecycleFiles(rawWithoutJpg);
        RefreshCurrentFolder();
        ShowRecycleResult(result.Item1, result.Item2);
    }

    private static string PairExamples(List<string> files)
    {
        if (files.Count == 0) return "无";
        var examples = String.Join("\n", files.Take(6).Select(file => "• " + Path.GetFileName(file)).ToArray());
        if (files.Count > 6) examples += "\n…另外 " + (files.Count - 6) + " 个";
        return examples;
    }

    private static Tuple<List<string>, List<string>> FindMissingPairs(string root)
    {
        var jpgs = FilesIn(Path.Combine(root, "jpg")).Where(IsJpg).ToList();
        var raws = FilesIn(Path.Combine(root, "arw")).Where(IsRaw).ToList();
        var jpgStems = new HashSet<string>(jpgs.Select(Stem), StringComparer.OrdinalIgnoreCase);
        var rawStems = new HashSet<string>(raws.Select(Stem), StringComparer.OrdinalIgnoreCase);
        var jpgWithoutRaw = jpgs.Where(file => !rawStems.Contains(Stem(file))).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        var rawWithoutJpg = raws.Where(file => !jpgStems.Contains(Stem(file))).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        return Tuple.Create(jpgWithoutRaw, rawWithoutJpg);
    }

    private static Tuple<int, List<string>> RecycleFiles(IEnumerable<string> files)
    {
        int completed = 0;
        var failures = new List<string>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file)) continue;
            if (MoveToRecycleBin(file)) completed++;
            else failures.Add(Path.GetFileName(file));
        }
        return Tuple.Create(completed, failures);
    }

    private void ShowRecycleResult(int completed, List<string> failures)
    {
        var text = "已移入 Windows 回收站：" + completed + " 个文件。";
        if (failures.Count > 0) text += "\n\n无法处理 " + failures.Count + " 个：\n" + String.Join("\n", failures.Take(8).ToArray());
        SetStatus(text.Split('\n')[0], failures.Count > 0);
        MessageBox.Show(text, failures.Count > 0 ? "清理完成，但有错误" : "清理完成", MessageBoxButton.OK, failures.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private string RootFolder(bool notify)
    {
        var root = folderBox.Text.Trim();
        if (Directory.Exists(root)) return root;
        if (notify) MessageBox.Show("请选择一个有效的照片目录。", "请选择目录", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private static IEnumerable<string> FilesIn(string folder)
    {
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly) : Enumerable.Empty<string>();
    }

    private static BitmapSource LoadOrientedJpeg(string file, int decodePixelWidth)
    {
        var orientation = ReadExifOrientation(file);
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(file, UriKind.Absolute);
        if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (orientation == 3) image.Rotation = Rotation.Rotate180;
        else if (orientation == 5 || orientation == 6 || orientation == 7) image.Rotation = Rotation.Rotate90;
        else if (orientation == 8) image.Rotation = Rotation.Rotate270;
        image.EndInit();
        image.Freeze();

        BitmapSource source = image;
        ScaleTransform mirror = null;
        if (orientation == 2 || orientation == 5) mirror = new ScaleTransform(-1, 1);
        else if (orientation == 4 || orientation == 7) mirror = new ScaleTransform(1, -1);
        if (mirror != null)
        {
            mirror.Freeze();
            var transformed = new TransformedBitmap(source, mirror);
            transformed.Freeze();
            source = transformed;
        }
        return source;
    }

    private static int ReadExifOrientation(string file)
    {
        try
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                var metadata = decoder.Frames.Count > 0 ? decoder.Frames[0].Metadata as BitmapMetadata : null;
                var value = ReadMetadataValue(metadata, "/app1/ifd/{ushort=274}");
                var orientation = value == null ? 1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return orientation >= 1 && orientation <= 8 ? orientation : 1;
            }
        }
        catch { return 1; }
    }

    private static bool IsJpg(string path) { return JpgExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
    private static bool IsRaw(string path) { return RawExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
    private static string Stem(string path) { return Path.GetFileNameWithoutExtension(path); }

    private static Dictionary<string, string> RawByStem(string folder)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in FilesIn(folder).Where(IsRaw)) if (!result.ContainsKey(Stem(raw))) result.Add(Stem(raw), raw);
        return result;
    }

    private void ShowPhotoInfo(string file)
    {
        BitmapMetadata metadata = null;
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(file, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            metadata = decoder.Frames.Count > 0 ? decoder.Frames[0].Metadata as BitmapMetadata : null;
        }
        catch { }

        var date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36867}"));
        if (date == "—") date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=36868}"));
        if (date == "—") date = FormatExifDate(ReadMetadataValue(metadata, "/app1/ifd/{ushort=306}"));
        var model = ToDisplayText(ReadMetadataValue(metadata, "/app1/ifd/{ushort=272}"));
        var iso = ToDisplayText(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=34855}"));
        var aperture = FormatAperture(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=33437}"));
        var exposure = FormatExposure(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=33434}"));
        var focalLength = FormatFocalLength(ReadMetadataValue(metadata, "/app1/ifd/exif/{ushort=37386}"));
        photoInfo.Text = "拍摄日期：" + date
            + "    照相机型号：" + model
            + "    ISO：" + iso
            + "    焦段：" + focalLength
            + "    光圈值：" + aperture
            + "    曝光时间：" + exposure;
    }

    private static object ReadMetadataValue(BitmapMetadata metadata, string query)
    {
        if (metadata == null) return null;
        try { return metadata.GetQuery(query); }
        catch { return null; }
    }

    private static string ToDisplayText(object value)
    {
        if (value == null) return "—";
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return String.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
    }

    private static string FormatExifDate(object value)
    {
        var text = ToDisplayText(value);
        if (text == "—") return text;
        var date = ParseExifDate(value);
        if (date.HasValue) return date.Value.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
        return text;
    }

    private static DateTime? ParseExifDate(object value)
    {
        var text = ToDisplayText(value);
        if (text == "—") return null;
        DateTime date;
        if (DateTime.TryParseExact(text, new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;
        return null;
    }

    private static bool TryGetRational(object value, out double number, out uint numerator, out uint denominator)
    {
        number = 0;
        numerator = 0;
        denominator = 0;
        if (value == null) return false;
        try
        {
            var packed = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            // WIC stores EXIF rationals as denominator in the high 32 bits and numerator in the low 32 bits.
            numerator = (uint)packed;
            denominator = (uint)(packed >> 32);
            if (denominator > 0)
            {
                number = (double)numerator / denominator;
                return true;
            }
        }
        catch { }
        var text = ToDisplayText(value);
        var parts = text.Split('/');
        if (parts.Length == 2 && UInt32.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator) && UInt32.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out denominator) && denominator > 0)
        {
            number = (double)numerator / denominator;
            return true;
        }
        return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string FormatAperture(object value)
    {
        double aperture;
        uint numerator, denominator;
        if (!TryGetRational(value, out aperture, out numerator, out denominator) || aperture <= 0) return "—";
        return "f/" + aperture.ToString(aperture % 1 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture);
    }

    private static string FormatFocalLength(object value)
    {
        double focalLength;
        uint numerator, denominator;
        if (!TryGetRational(value, out focalLength, out numerator, out denominator) || focalLength <= 0) return "—";
        return focalLength.ToString(focalLength % 1 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture) + " mm";
    }

    private static string FormatExposure(object value)
    {
        double seconds;
        uint numerator, denominator;
        if (!TryGetRational(value, out seconds, out numerator, out denominator) || seconds <= 0) return "—";
        if (seconds < 1 && numerator > 0 && denominator > 0) return numerator + "/" + denominator + " 秒";
        if (seconds < 1) return "1/" + Math.Round(1 / seconds).ToString(CultureInfo.InvariantCulture) + " 秒";
        return seconds.ToString(seconds % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " 秒";
    }

    private void SetStatus(string text, bool error)
    {
        status.Text = text;
        status.Foreground = error ? Brush("#B42318") : Brush("#5F6B7A");
    }

    private static Border Card()
    {
        return new Border { Background = Brush("#FFFFFF"), BorderBrush = Brush("#D9E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(20) };
    }

    private static Border Divider()
    {
        return new Border { Height = 1, Background = Brush("#E4EAF0"), Margin = new Thickness(0, 18, 0, 18) };
    }

    private static TextBlock SectionTitle(string text)
    {
        return new TextBlock { Text = text, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2937") };
    }

    private static UIElement Stat(string label, TextBlock value, string color)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brush("#6A7787") });
        value.Text = "—";
        value.Margin = new Thickness(0, 3, 0, 0);
        value.FontSize = 22;
        value.FontWeight = FontWeights.SemiBold;
        value.Foreground = Brush(color);
        panel.Children.Add(value);
        return panel;
    }

    private static Button PrimaryButton(string text, double width) { var button = new Button(); button.Content = text; StyleButton(button, true, width); return button; }
    private static Button SecondaryButton(string text, double width) { var button = new Button(); button.Content = text; StyleButton(button, false, width); return button; }

    private static void StyleButton(Button button, bool primary, double width)
    {
        button.Width = width > 0 ? width : double.NaN;
        button.Height = 40;
        button.FontSize = 14;
        button.FontWeight = FontWeights.SemiBold;
        button.Cursor = Cursors.Hand;
        button.BorderThickness = new Thickness(0);
        // Keep every action in one visual family; availability is communicated by opacity.
        button.Background = Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        ApplyRoundedButtonTemplate(button, 5);
    }

    private static void StyleDestructiveButton(Button button, double width)
    {
        StyleButton(button, true, width);
    }

    private static void ApplyRoundedButtonTemplate(Button button, double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static void StyleViewButton(Button button)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static void StyleFolderHistoryButton(Button button)
    {
        button.Width = 42;
        button.Height = 40;
        button.Padding = new Thickness(0);
        button.FontSize = 11;
        button.Cursor = Cursors.Hand;
        button.BorderBrush = Brush(ButtonBlue);
        button.BorderThickness = new Thickness(1, 1, 1, 1);
        button.Background = Brush(ButtonBlue);
        button.Foreground = Brushes.White;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 6, 6, 0));
        border.SetValue(Border.BackgroundProperty, button.Background);
        border.SetValue(Border.BorderBrushProperty, button.BorderBrush);
        border.SetValue(Border.BorderThicknessProperty, button.BorderThickness);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = border;
        button.Template = template;
    }

    private static ScrollBar CreateMiniVerticalScrollBar()
    {
        var railColor = darkMode ? "#111820" : "#D8E0E8";
        var thumbColor = darkMode ? "#586574" : "#566473";
        var templateXaml =
            "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ScrollBar}\">" +
            "<Grid Width=\"8\" Background=\"Transparent\">" +
            "<Border Width=\"6\" HorizontalAlignment=\"Center\" Background=\"" + railColor + "\" CornerRadius=\"3\"/>" +
            "<Track x:Name=\"PART_Track\" Orientation=\"Vertical\" IsDirectionReversed=\"True\" " +
            "Minimum=\"{TemplateBinding Minimum}\" Maximum=\"{TemplateBinding Maximum}\" " +
            "Value=\"{TemplateBinding Value}\" ViewportSize=\"{TemplateBinding ViewportSize}\">" +
            "<Track.DecreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageUpCommand}\" Opacity=\"0\" Focusable=\"False\"/></Track.DecreaseRepeatButton>" +
            "<Track.Thumb><Thumb MinHeight=\"28\" Focusable=\"False\"><Thumb.Template>" +
            "<ControlTemplate TargetType=\"{x:Type Thumb}\"><Border Margin=\"1,1\" Background=\"" + thumbColor + "\" CornerRadius=\"3\"/></ControlTemplate>" +
            "</Thumb.Template></Thumb></Track.Thumb>" +
            "<Track.IncreaseRepeatButton><RepeatButton Command=\"{x:Static ScrollBar.PageDownCommand}\" Opacity=\"0\" Focusable=\"False\"/></Track.IncreaseRepeatButton>" +
            "</Track></Grid></ControlTemplate>";
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Width = 8,
            Minimum = 0,
            SmallChange = 80,
            LargeChange = 520,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 22, 5, 10),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            Template = (ControlTemplate)XamlReader.Parse(templateXaml)
        };
        Panel.SetZIndex(scrollBar, 5);
        return scrollBar;
    }

    private static void StyleContextMenuItem(MenuItem item)
    {
        item.Height = 32;
        item.Padding = new Thickness(12, 0, 14, 0);
        item.Background = darkMode ? Brush("#242A31") : Brush("#FFFFFF");
        item.Foreground = darkMode ? Brush("#F0F4F8") : Brush("#1F2937");
        item.BorderThickness = new Thickness(0);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, item.Background);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, item.Padding);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(MenuItem));
        template.VisualTree = border;
        item.Template = template;
    }

    private static SolidColorBrush Brush(string hex)
    {
        if (darkMode)
        {
            switch (hex.ToUpperInvariant())
            {
                case "#F4F7FB": hex = "#171B21"; break;
                case "#FFFFFF": hex = "#242A31"; break;
                case "#FBFCFE": hex = "#1E232A"; break;
                case "#EAF1F8": hex = "#303A46"; break;
                case "#EAF0F5": hex = "#2A3038"; break;
                case "#EAF3FF": hex = "#193A58"; break;
                case "#D9E2EC": hex = "#3F4955"; break;
                case "#D6E0EA": hex = "#46515E"; break;
                case "#E4EAF0": hex = "#3A444F"; break;
                case "#B7C3D0": hex = "#5A6674"; break;
                case "#1F2937": hex = "#F0F4F8"; break;
                case "#2D3A4A": hex = "#E2EAF2"; break;
                case "#5F6B7A": hex = "#B5C0CD"; break;
                case "#66778B": hex = "#B7C3D0"; break;
                case "#6A7787": hex = "#B5C0CD"; break;
                case "#75869A": hex = "#B7C3D0"; break;
                case "#245F98": hex = "#B9DDFF"; break;
                case "#2678D4": hex = "#4D9DFF"; break;
                case "#8167C8": hex = "#B69AF4"; break;
                case "#E28825": hex = "#FFAD4E"; break;
                case "#B42318": hex = "#FF9B96"; break;
            }
        }
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
    }

    private static UIElement HeaderBadgeImage()
    {
        try
        {
            var image = new BitmapImage(new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RAWMateHeader.png"), UriKind.Absolute));
            return new Image { Source = image, Stretch = Stretch.Uniform };
        }
        catch
        {
            return new TextBlock { Text = "RAW", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
    }

    // SHFileOperation with FOF_ALLOWUNDO is the native Windows Recycle Bin API.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    private static bool MoveToRecycleBin(string file)
    {
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(file) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        var result = SHFileOperation(ref operation);
        return result == 0 && !operation.fAnyOperationsAborted;
    }
}

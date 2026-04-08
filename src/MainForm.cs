using MHUpkManager.Models;
using MHUpkManager.BackupManager;
using MHUpkManager.MeshExporter;
using MHUpkManager.EnemyConverter;
using MHUpkManager.MeshImporter;
using MHUpkManager.MeshPreview;
using MHUpkManager.MeshSections;
using MHUpkManager.MeshWorkspace;
using MHUpkManager.MaterialInspector;
using MHUpkManager.SectionMapping;
using MHUpkManager.Model;
using MHUpkManager.Model.Import;
using MHUpkManager.Retargeting;
using MHUpkManager.TexturePreview;
using MHUpkManager.TextureManager;
using MHUpkManager.TextureWorkspace;
using MHUpkManager.UI;
using MHUpkManager.UiEditor;
using System.Media;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using UpkManager.Contracts;
using UpkManager.Extensions;
using UpkManager.Indexing;
using UpkManager.Models;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile.Core;
using UpkManager.Models.UpkFile.Engine.Anim;
using UpkManager.Models.UpkFile.Engine;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager
{
    public partial class MainForm : Form
    {
        private const string RetargetBuildMarker = "retarget-build-2026-04-04-packedposfix-v1";
        private const int ToolTabRightPanelWidth = 300;
        private readonly UpkFileRepository repository;
        public const string AppName = "MH UPK Manager v.1.0 by AlexBond - Upgraded by TruskillzzRuns";
        public UnrealUpkFile UpkFile { get; set; }

        private HexViewForm hexViewForm;
        private TextureViewForm textureViewForm;
        private readonly MeshExporterPanel meshExporterPanel;
        private readonly EnemyPlayablePanel enemyPlayablePanel;
        private readonly MeshImporterPanel meshImporterPanel;
        private readonly MeshPreviewUI meshPreviewPanel;
        private readonly MeshSectionToolUI meshSectionToolPanel;
        private readonly MeshWorkspaceUI meshWorkspacePanel;
        private readonly BackupManagerPanel backupManagerPanel;
        private readonly MaterialInspectorUI materialInspectorPanel;
        private readonly SectionMaterialMappingUI sectionMaterialMappingPanel;
        private readonly TexturePreviewUI texturePreviewPanel;
        private readonly MaterialTextureSwapUI materialTextureSwapPanel;
        private readonly CharacterTextureWorkflowUI characterTextureWorkflowPanel;
        private readonly TextureWorkspaceUI textureWorkspacePanel;
        private readonly SkeletalMeshRetargeterPanel skeletalMeshRetargeterPanel;
        private readonly UiEditorPanel uiEditorPanel;
        private readonly EnemyPlayableConverter enemyPlayableConverter;
        private readonly UiEditorTool uiEditorTool;
        private readonly UpkBrowsePreferencesStore upkBrowsePreferencesStore;
        private readonly UpkBrowsePreferences upkBrowsePreferences;
        private readonly Dictionary<string, UnrealExportTableEntry> enemyPlayableExports = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EnemyScanCandidate> enemyPlayableScanCandidates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UnrealHeader> enemyPlayableHeaderCache = new(StringComparer.OrdinalIgnoreCase);
        private UnrealHeader meshExporterHeader;
        private readonly Dictionary<string, UnrealExportTableEntry> meshExporterExports = new(StringComparer.OrdinalIgnoreCase);
        private UnrealHeader meshImporterHeader;
        private readonly Dictionary<string, UnrealExportTableEntry> meshImporterExports = new(StringComparer.OrdinalIgnoreCase);
        private UnrealHeader retargeterHeader;
        private readonly Dictionary<string, UnrealExportTableEntry> retargeterExports = new(StringComparer.OrdinalIgnoreCase);
        private int lastMainSplitterDistance;
        private Panel inspectorHeaderPanel;
        private Label inspectorTitleLabel;
        private Label inspectorSubtitleLabel;
        private Label inspectorHintLabel;
        private RetargetMesh retargetSourceMesh;
        private RetargetMesh retargetProcessedMesh;
        private RetargetMesh retargetReferenceMesh;
        private SkeletonDefinition retargetPlayerSkeleton;
        private BoneMappingResult retargetBoneMapping;
        private UAnimSet retargetAnimSet;
        private readonly List<string> retargetTexturePaths = [];
        private readonly RetargetPosePreviewService retargetPosePreviewService = new();
        private readonly RetargetToPreviewMeshConverter retargetToPreviewMeshConverter = new();
        private string currentRetargetDiagnosticLogPath;
        private bool suppressRetargetWarningForSession;
        private bool showingRetargetWarning;
        private bool darkModeEnabled;
        private ToolStripProfessionalRenderer darkMenuRenderer;

        private List<TreeNode> rootNodes;
        private object currentObject;

        public MainForm()
        {
            InitializeComponent();
            InitializeInspectorHeader();
            TextureManifest.Initialize();
            TextureFileCache.Initialize();
            ApplyShellStyling();

            repository = new UpkFileRepository();
            LoadPackageIndex();
            enemyPlayableConverter = new EnemyPlayableConverter(repository);
            upkBrowsePreferencesStore = new UpkBrowsePreferencesStore();
            upkBrowsePreferences = upkBrowsePreferencesStore.Load();

            rootNodes = [];

            EnableDoubleBuffering(nameGridView);
            EnableDoubleBuffering(importGridView);
            EnableDoubleBuffering(exportGridView);

            RegistryInstances();

            hexViewForm = new HexViewForm();
            textureViewForm = new TextureViewForm();
            textureViewForm.SendToTexturePreviewRequested = ShowTextureInPreviewTab;
            meshExporterPanel = new MeshExporterPanel();
            enemyPlayablePanel = new EnemyPlayablePanel();
            meshImporterPanel = new MeshImporterPanel();
            meshPreviewPanel = new MeshPreviewUI();
            meshSectionToolPanel = new MeshSectionToolUI(meshPreviewPanel, GetCurrentUpkPath, GetCurrentSkeletalMeshExportPath);
            meshWorkspacePanel = new MeshWorkspaceUI(meshPreviewPanel, meshExporterPanel, meshImporterPanel, meshSectionToolPanel);
            backupManagerPanel = new BackupManagerPanel(GetCurrentUpkPath);
            materialInspectorPanel = new MaterialInspectorUI(GetCurrentUpkPath, GetCurrentSkeletalMeshExportPath);
            sectionMaterialMappingPanel = new SectionMaterialMappingUI(GetCurrentUpkPath, GetCurrentSkeletalMeshExportPath);
            texturePreviewPanel = new TexturePreviewUI(meshPreviewPanel, GetCurrentUpkPath, GetCurrentTextureExportPath);
            materialTextureSwapPanel = new MaterialTextureSwapUI(GetCurrentUpkPath, GetCurrentSkeletalMeshExportPath, ShowTextureInPreviewTab);
            characterTextureWorkflowPanel = new CharacterTextureWorkflowUI(
                GetCurrentUpkPath,
                GetCurrentSkeletalMeshExportPath,
                ShowTextureInPreviewTab,
                PreviewCharacterTexturesOnMeshAsync,
                ClearCharacterTexturePreviewOnMesh);
            textureWorkspacePanel = new TextureWorkspaceUI(texturePreviewPanel, materialInspectorPanel, sectionMaterialMappingPanel, materialTextureSwapPanel, characterTextureWorkflowPanel);
            skeletalMeshRetargeterPanel = new SkeletalMeshRetargeterPanel();
            uiEditorPanel = new UiEditorPanel();
            uiEditorTool = new UiEditorTool();
            lastMainSplitterDistance = splitContainer1.SplitterDistance;
            InitializeObjectsWorkspaceUi();
            InitializeMeshPreviewUi();
            InitializeMeshExporterUi();
            InitializeEnemyPlayableUi();
            InitializeMeshImporterUi();
            InitializeMeshWorkspaceUi();
            InitializeBackupManagerUi();
            InitializeTextureWorkspaceUi();
            InitializeSkeletalMeshRetargeterUi();
            InitializeUiEditorUi();
            LogRetargetBuildMarker();
            RefreshRecentUpksMenu();
            tabControl2.SelectedIndexChanged += tabControl2_SelectedIndexChanged;
            UpdateMainLayoutForActiveTab();
            ApplyTheme();
        }

        private void InitializeInspectorHeader()
        {
            inspectorTitleLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 28,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft,
                Text = "Inspector"
            };

            inspectorSubtitleLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 36,
                ForeColor = Color.DimGray,
                Text = "Open a UPK and select an object to inspect its properties, tables, and quick actions."
            };

            inspectorHintLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 36,
                ForeColor = Color.FromArgb(70, 70, 70),
                Text = "Next: browse the package tree on the left, then inspect the selected export or import here."
            };

            inspectorHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 108,
                Padding = new Padding(12, 10, 12, 8)
            };
            inspectorHeaderPanel.Controls.Add(inspectorHintLabel);
            inspectorHeaderPanel.Controls.Add(inspectorSubtitleLabel);
            inspectorHeaderPanel.Controls.Add(inspectorTitleLabel);

            splitContainer1.Panel2.Controls.Add(inspectorHeaderPanel);
            inspectorHeaderPanel.BringToFront();
        }

        private void ApplyShellStyling()
        {
            BackColor = Color.FromArgb(244, 246, 249);

            menuStrip1.Padding = new Padding(8, 4, 8, 4);
            menuStrip1.BackColor = Color.White;
            menuStrip1.RenderMode = ToolStripRenderMode.System;

            statusStrip1.BackColor = Color.White;
            statusStrip1.SizingGrip = false;

            splitContainer1.BackColor = Color.FromArgb(214, 219, 227);
            splitContainer1.SplitterWidth = 6;
            splitContainer1.Panel2.BackColor = Color.FromArgb(248, 249, 251);

            panel4.Padding = new Padding(8, 6, 8, 6);
            panel4.Height = 48;
            panel4.BackColor = Color.WhiteSmoke;
            label1.Text = "Filter";
            label1.Font = new Font(label1.Font, FontStyle.Bold);
            filterClear.BackColor = Color.FromArgb(228, 235, 245);
            LayoutFilterPanel();
            panel4.Resize += (_, _) => LayoutFilterPanel();

            StyleShellTabControl(tabControl2, 170, allowMultiline: false);
            StyleShellTabControl(tabControl1, 152, allowMultiline: false);
            tabControl2.DrawItem += DrawShellTab;
            tabControl1.DrawItem += DrawShellTab;

            StyleTree(objectsTree);
            StyleTree(propertiesView);

            WorkspaceUiStyle.StyleGrid(nameGridView);
            WorkspaceUiStyle.StyleGrid(importGridView);
            WorkspaceUiStyle.StyleGrid(exportGridView);
            inspectorHeaderPanel.BackColor = Color.White;

            propertyGrid.HelpBackColor = Color.White;
            propertyGrid.HelpBorderColor = Color.FromArgb(214, 219, 227);
            propertyGrid.ViewBackColor = Color.White;
            propertyGrid.ViewBorderColor = Color.FromArgb(214, 219, 227);
            propertyGrid.LineColor = Color.FromArgb(228, 232, 238);
            propertyGrid.CategoryForeColor = Color.FromArgb(55, 55, 55);
        }

        private void darkModeMenuItem_Click(object sender, EventArgs e)
        {
            darkModeEnabled = darkModeMenuItem.Checked;
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            Color back = darkModeEnabled ? Color.FromArgb(18, 18, 18) : Color.FromArgb(244, 246, 249);
            Color surface = darkModeEnabled ? Color.FromArgb(28, 28, 28) : Color.White;
            Color surfaceAlt = darkModeEnabled ? Color.FromArgb(36, 36, 36) : Color.WhiteSmoke;
            Color border = darkModeEnabled ? Color.FromArgb(58, 58, 58) : Color.FromArgb(214, 219, 227);
            Color text = darkModeEnabled ? Color.FromArgb(232, 232, 232) : Color.FromArgb(48, 52, 59);
            Color muted = darkModeEnabled ? Color.FromArgb(232, 232, 232) : Color.DimGray;
            Color accent = darkModeEnabled ? Color.FromArgb(52, 95, 160) : Color.FromArgb(228, 235, 245);
            Color gridHeader = darkModeEnabled ? Color.FromArgb(42, 42, 42) : SystemColors.Control;
            Color gridSelection = darkModeEnabled ? Color.FromArgb(56, 78, 110) : SystemColors.GradientInactiveCaption;

            BackColor = back;
            ForeColor = text;
            menuStrip1.BackColor = surface;
            menuStrip1.ForeColor = text;
            menuStrip1.RenderMode = ToolStripRenderMode.Professional;
            menuStrip1.Renderer = darkModeEnabled
                ? darkMenuRenderer ??= new ToolStripProfessionalRenderer(new DarkMenuColorTable())
                : new ToolStripProfessionalRenderer();
            fileToolStripMenuItem.ForeColor = text;
            foreach (ToolStripItem item in fileToolStripMenuItem.DropDownItems)
            {
                item.ForeColor = text;
                item.BackColor = surface;
            }
            statusStrip1.BackColor = surface;
            statusStrip1.ForeColor = text;
            splitContainer1.BackColor = border;
            splitContainer1.Panel1.BackColor = back;
            splitContainer1.Panel2.BackColor = surfaceAlt;
            panel4.BackColor = surfaceAlt;
            label1.ForeColor = text;
            filterBox.BackColor = surface;
            filterBox.ForeColor = text;
            filterClear.BackColor = accent;
            filterClear.ForeColor = text;
            inspectorHeaderPanel.BackColor = surface;
            inspectorTitleLabel.ForeColor = text;
            inspectorSubtitleLabel.ForeColor = muted;
            inspectorHintLabel.ForeColor = muted;
            meshWorkspacePanel.SetDarkMode(darkModeEnabled);
            textureWorkspacePanel.SetDarkMode(darkModeEnabled);

            ApplyThemeToControlTree(this, back, surface, surfaceAlt, border, text, muted, gridHeader, gridSelection);

            propertyGrid.ViewBackColor = surface;
            propertyGrid.ViewForeColor = text;
            propertyGrid.HelpBackColor = surface;
            propertyGrid.HelpForeColor = text;
            propertyGrid.HelpBorderColor = border;
            propertyGrid.ViewBorderColor = border;
            propertyGrid.LineColor = border;
            propertyGrid.CategoryForeColor = text;
            propertyGrid.DisabledItemForeColor = text;

            tabControl2.Invalidate();
            tabControl1.Invalidate();
            Invalidate(true);
        }

        private static void ApplyThemeToControlTree(
            Control root,
            Color back,
            Color surface,
            Color surfaceAlt,
            Color border,
            Color text,
            Color muted,
            Color gridHeader,
            Color gridSelection)
        {
            foreach (Control control in root.Controls)
            {
                switch (control)
                {
                    case TextBox textBox:
                        textBox.BackColor = textBox.ReadOnly ? surfaceAlt : surface;
                        textBox.ForeColor = text;
                        textBox.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case RichTextBox richTextBox:
                        richTextBox.BackColor = surface;
                        richTextBox.ForeColor = text;
                        richTextBox.BorderStyle = BorderStyle.None;
                        WorkspaceUiStyle.RefreshWorkflowDetailsColors(richTextBox);
                        break;
                    case TreeView treeView:
                        treeView.BackColor = surface;
                        treeView.ForeColor = text;
                        treeView.LineColor = muted;
                        ApplyTreeNodeColors(treeView.Nodes, text, surface);
                        break;
                    case ListView listView:
                        listView.BackColor = surface;
                        listView.ForeColor = text;
                        break;
                    case CheckedListBox checkedListBox:
                        checkedListBox.BackColor = surface;
                        checkedListBox.ForeColor = text;
                        break;
                    case ListBox listBox:
                        listBox.BackColor = surface;
                        listBox.ForeColor = text;
                        break;
                    case DataGridView grid:
                        grid.BackgroundColor = surface;
                        grid.GridColor = border;
                        grid.EnableHeadersVisualStyles = false;
                        grid.ColumnHeadersDefaultCellStyle.BackColor = gridHeader;
                        grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                        grid.DefaultCellStyle.BackColor = surface;
                        grid.DefaultCellStyle.ForeColor = text;
                        grid.DefaultCellStyle.SelectionBackColor = gridSelection;
                        grid.DefaultCellStyle.SelectionForeColor = text;
                        break;
                    case Button button:
                        button.UseVisualStyleBackColor = false;
                        button.BackColor = surfaceAlt;
                        button.ForeColor = text;
                        button.FlatStyle = FlatStyle.Standard;
                        break;
                    case CheckBox checkBox:
                        checkBox.BackColor = back;
                        checkBox.ForeColor = text;
                        break;
                    case ComboBox comboBox:
                        comboBox.BackColor = surface;
                        comboBox.ForeColor = text;
                        break;
                    case NumericUpDown numericUpDown:
                        numericUpDown.BackColor = surface;
                        numericUpDown.ForeColor = text;
                        break;
                    case Label label:
                        if (!string.Equals(label.Tag as string, "workflow-step-number", StringComparison.Ordinal))
                            label.ForeColor = text;
                        if (label.Parent is not null && label.Parent is not TabPage)
                            label.BackColor = Color.Transparent;
                        break;
                    case TabPage tabPage:
                        tabPage.BackColor = back;
                        tabPage.ForeColor = text;
                        break;
                    case SplitContainer splitContainer:
                        splitContainer.BackColor = border;
                        splitContainer.Panel1.BackColor = back;
                        splitContainer.Panel2.BackColor = back;
                        break;
                    case UserControl userControl:
                        userControl.BackColor = back;
                        userControl.ForeColor = text;
                        break;
                    case Panel panel:
                        panel.BackColor = back;
                        break;
                    case GroupBox groupBox:
                        groupBox.ForeColor = text;
                        groupBox.BackColor = back;
                        break;
                    case TabControl:
                        break;
                }

                ApplyThemeToControlTree(control, back, surface, surfaceAlt, border, text, muted, gridHeader, gridSelection);
            }
        }

        private static void ApplyTreeNodeColors(TreeNodeCollection nodes, Color text, Color back)
        {
            foreach (TreeNode node in nodes)
            {
                node.ForeColor = text;
                node.BackColor = back;
                if (node.Nodes.Count > 0)
                    ApplyTreeNodeColors(node.Nodes, text, back);
            }
        }

        private void UpdateInspectorSummary()
        {
            if (inspectorTitleLabel == null)
                return;

            if (currentObject is UnrealExportTableEntry export)
            {
                string className = export.ClassReferenceNameIndex?.Name ?? "Export";
                inspectorTitleLabel.Text = export.ObjectNameIndex?.Name ?? export.GetPathName();
                inspectorSubtitleLabel.Text = $"{className}  |  {export.GetPathName()}";

                List<string> hints = ["Review the object tree, properties, and related table rows."];
                if (string.Equals(className, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(className, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    hints.Add("Use Mesh, Texture, or Retarget workspaces for mesh-specific actions.");
                if (string.Equals(className, nameof(UTexture2D), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(className, "Texture2D", StringComparison.OrdinalIgnoreCase))
                    hints.Add("Use Texture workspace to preview, inspect materials, or inject replacements.");

                inspectorHintLabel.Text = string.Join(" ", hints);
                return;
            }

            if (currentObject is UnrealImportTableEntry import)
            {
                inspectorTitleLabel.Text = import.ObjectNameIndex?.Name ?? import.GetPathName();
                inspectorSubtitleLabel.Text = $"Import  |  {import.GetPathName()}";
                inspectorHintLabel.Text = "Review import class, outer, and package info here. Use View Parent or View Object to navigate related entries.";
                return;
            }

            if (UpkFile?.Header != null)
            {
                inspectorTitleLabel.Text = Path.GetFileName(UpkFile.GameFilename);
                inspectorSubtitleLabel.Text = "Package Overview";
                inspectorHintLabel.Text = "Inspect file metadata, package tables, and select objects from the tree to populate this inspector.";
                return;
            }

            inspectorTitleLabel.Text = "Inspector";
            inspectorSubtitleLabel.Text = "Open a UPK and select an object to inspect its properties, tables, and quick actions.";
            inspectorHintLabel.Text = "Next: browse the package tree on the left, then inspect the selected export or import here.";
        }

        private static void StyleShellTabControl(TabControl tabControl, int itemWidth, bool allowMultiline)
        {
            tabControl.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl.ItemSize = new Size(itemWidth, 32);
            tabControl.Padding = new Point(16, 6);
            tabControl.SizeMode = TabSizeMode.Fixed;
            tabControl.Multiline = allowMultiline;
        }

        private static void StyleTree(TreeView treeView)
        {
            treeView.BorderStyle = BorderStyle.None;
            treeView.BackColor = Color.White;
            treeView.HideSelection = false;
            treeView.FullRowSelect = true;
            treeView.ShowLines = false;
        }

        private void DrawShellTab(object sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabControl || e.Index < 0 || e.Index >= tabControl.TabPages.Count)
                return;

            TabPage page = tabControl.TabPages[e.Index];
            bool selected = e.Index == tabControl.SelectedIndex;
            Rectangle bounds = e.Bounds;
            Color backColor = darkModeEnabled
                ? selected ? Color.FromArgb(52, 55, 60) : Color.FromArgb(38, 40, 44)
                : selected ? Color.White : Color.FromArgb(233, 237, 243);
            Color borderColor = darkModeEnabled
                ? Color.FromArgb(70, 73, 79)
                : selected ? Color.FromArgb(185, 196, 212) : Color.FromArgb(214, 219, 227);
            Color textColor = darkModeEnabled ? Color.FromArgb(230, 232, 235) : Color.FromArgb(48, 52, 59);

            using SolidBrush backBrush = new(backColor);
            using Pen borderPen = new(borderColor);
            using SolidBrush textBrush = new(textColor);
            using StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            Rectangle fillRect = Rectangle.Inflate(bounds, -2, -2);
            e.Graphics.FillRectangle(backBrush, fillRect);
            e.Graphics.DrawRectangle(borderPen, fillRect);
            if (selected)
            {
                using Font selectedFont = new(e.Font, FontStyle.Bold);
                e.Graphics.DrawString(page.Text, selectedFont, textBrush, fillRect, format);
            }
            else
            {
                e.Graphics.DrawString(page.Text, e.Font, textBrush, fillRect, format);
            }
        }

        private void LayoutFilterPanel()
        {
            label1.Location = new Point(8, 10);
            filterClear.Size = new Size(26, 26);
            filterClear.Location = new Point(Math.Max(8, panel4.Width - 34), 10);
            filterBox.Location = new Point(58, 10);
            filterBox.Height = 28;
            filterBox.Width = Math.Max(160, panel4.Width - 100);
        }

        private void LogRetargetBuildMarker()
        {
            string exePath = Assembly.GetExecutingAssembly().Location;
            string version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Application.ProductVersion
                ?? "unknown";
            string writeTime = File.Exists(exePath)
                ? File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm:ss")
                : "unknown";

            skeletalMeshRetargeterPanel.AppendLog(
                $"Runtime build marker: {RetargetBuildMarker}, version={version}, exeWriteTime={writeTime}, exePath={exePath}");
        }

        private void LoadPackageIndex()
        {
            try
            {   
                string indexPath = Path.Combine(Application.StartupPath, "Data", "mh152.mpk");
                if (File.Exists(indexPath))
                    repository.LoadPackageIndex(indexPath);
                else
                    WarningBox($"Package index not found at {indexPath}");
            }
            catch (Exception ex)
            {
                WarningBox($"Failed to load package index: {ex.Message}");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            hexViewForm?.Dispose();
            textureViewForm?.Dispose();
        }

        private void RegistryInstances()
        {
            _ = CoreRegistry.Instance;
            _ = EngineRegistry.Instance;
        }

        private void InitializeMeshExporterUi()
        {
            meshExporterPanel.BrowseUpkRequested += async (_, _) => await BrowseMeshExporterUpkAsync().ConfigureAwait(true);
            meshExporterPanel.UseCurrentUpkRequested += async (_, _) => await UseCurrentMeshExporterUpkAsync().ConfigureAwait(true);
            meshExporterPanel.BrowseFbxRequested += BrowseMeshExporterFbx;
            meshExporterPanel.SkeletalMeshChanged += async (_, _) => await RefreshMeshExporterLodsAsync().ConfigureAwait(true);
            meshExporterPanel.ExportRequested += async (_, _) => await ExportMeshFromPanelAsync().ConfigureAwait(true);
        }

        private void InitializeMeshPreviewUi()
        {
            meshPreviewPanel.UseCurrentUpkRequested += async (_, _) => await UseCurrentMeshPreviewUpkAsync().ConfigureAwait(true);
        }

        private void InitializeMeshImporterUi()
        {
            meshImporterPanel.BrowseUpkRequested += async (_, _) => await BrowseMeshImporterUpkAsync().ConfigureAwait(true);
            meshImporterPanel.UseCurrentUpkRequested += async (_, _) => await UseCurrentMeshImporterUpkAsync().ConfigureAwait(true);
            meshImporterPanel.BrowseFbxRequested += BrowseMeshImporterFbx;
            meshImporterPanel.SkeletalMeshChanged += async (_, _) => await RefreshMeshImporterLodsAsync().ConfigureAwait(true);
            meshImporterPanel.ImportRequested += async (_, _) => await ImportMeshFromPanelAsync().ConfigureAwait(true);
        }

        private void InitializeEnemyPlayableUi()
        {
            TabPage enemyConverterPage = new()
            {
                Name = "enemyConverterPage",
                Text = "Enemy Converter",
                UseVisualStyleBackColor = true
            };
            enemyConverterPage.Controls.Add(enemyPlayablePanel);
            tabControl2.Controls.Add(enemyConverterPage);

            enemyPlayablePanel.SelectedMeshChanged += async (_, _) => await RefreshEnemyPlayableDetectionAsync().ConfigureAwait(true);
            enemyPlayablePanel.ScanSelectionChanged += async (_, _) => await RefreshEnemyPlayableDetectionAsync().ConfigureAwait(true);
            enemyPlayablePanel.ScanCurrentFolderRequested += async (_, _) => await ScanEnemyPlayableCurrentFolderAsync().ConfigureAwait(true);
            enemyPlayablePanel.BrowseScanFolderRequested += async (_, _) => await BrowseEnemyPlayableScanFolderAsync().ConfigureAwait(true);
            enemyPlayablePanel.ConvertRequested += async (_, _) => await ConvertEnemyToPlayableAsync().ConfigureAwait(true);

            // TODO(enemy-converter-ui-registration): keep this tab wired into the workspace tab order if the tool navigation shell changes again.
            // TODO(enemy-converter-menu-registration): add a dedicated menu entry if this workflow should be launchable without switching to the workspace tab.
        }

        private void InitializeMeshWorkspaceUi()
        {
            TabPage meshWorkspacePage = new()
            {
                Name = "meshWorkspacePage",
                Text = "Mesh",
                UseVisualStyleBackColor = true
            };
            meshWorkspacePage.Controls.Add(meshWorkspacePanel);
            tabControl2.Controls.Add(meshWorkspacePage);
        }

        private void InitializeTextureWorkspaceUi()
        {
            TabPage textureWorkspacePage = new()
            {
                Name = "textureWorkspacePage",
                Text = "Texture",
                UseVisualStyleBackColor = true
            };
            textureWorkspacePage.Controls.Add(textureWorkspacePanel);
            tabControl2.Controls.Add(textureWorkspacePage);
        }

        private void InitializeBackupManagerUi()
        {
            TabPage backupManagerPage = new()
            {
                Name = "backupManagerPage",
                Text = "Backup",
                UseVisualStyleBackColor = true
            };
            backupManagerPage.Controls.Add(backupManagerPanel);
            tabControl2.Controls.Add(backupManagerPage);
        }

        private void InitializeSkeletalMeshRetargeterUi()
        {
            TabPage retargeterPage = new()
            {
                Name = "retargetWorkspacePage",
                Text = "Retarget",
                UseVisualStyleBackColor = true
            };
            retargeterPage.Controls.Add(skeletalMeshRetargeterPanel);
            tabControl2.Controls.Add(retargeterPage);

            skeletalMeshRetargeterPanel.BrowseUpkRequested += async (_, _) => await BrowseRetargeterUpkAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.UseCurrentUpkRequested += async (_, _) => await UseCurrentRetargeterUpkAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.SkeletalMeshChanged += async (_, _) => await RefreshRetargeterLodsAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.ImportMeshRequested += async (_, _) => await ImportRetargetMeshAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.ImportAnimSetRequested += async (_, _) => await ImportRetargetAnimSetAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.ImportTexturesRequested += (_, _) => ImportRetargetTextures();
            skeletalMeshRetargeterPanel.ImportSkeletonRequested += async (_, _) => await ImportRetargetSkeletonAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.AutoBoneMapRequested += (_, _) => AutoMapRetargetBones();
            skeletalMeshRetargeterPanel.AutoOrientationRequested += (_, _) => ApplyRetargetAutoOrientation();
            skeletalMeshRetargeterPanel.RotateSourceLeftRequested += (_, _) => RotateRetargetSourceMesh(-90.0f);
            skeletalMeshRetargeterPanel.RotateSourceRightRequested += (_, _) => RotateRetargetSourceMesh(90.0f);
            skeletalMeshRetargeterPanel.RotateSourceFlipRequested += (_, _) => RotateRetargetSourceMesh(180.0f);
            skeletalMeshRetargeterPanel.RotateSourcePitchForwardRequested += (_, _) => RotateRetargetSourceMeshAroundAxis("forward", -90.0f);
            skeletalMeshRetargeterPanel.RotateSourcePitchBackwardRequested += (_, _) => RotateRetargetSourceMeshAroundAxis("forward", 90.0f);
            skeletalMeshRetargeterPanel.RotateSourceRollLeftRequested += (_, _) => RotateRetargetSourceMeshAroundAxis("right", -90.0f);
            skeletalMeshRetargeterPanel.RotateSourceRollRightRequested += (_, _) => RotateRetargetSourceMeshAroundAxis("right", 90.0f);
            skeletalMeshRetargeterPanel.AutoScaleRequested += (_, _) => ApplyRetargetAutoScale();
            skeletalMeshRetargeterPanel.WeightTransferRequested += (_, _) => ApplyRetargetWeightTransfer();
            skeletalMeshRetargeterPanel.AutoRigRequested += (_, _) => ApplyRetargetOneClickAutoRig();
            skeletalMeshRetargeterPanel.ApplyPosePreviewRequested += (_, _) => ApplyRetargetPosePreview();
            skeletalMeshRetargeterPanel.ResetPosePreviewRequested += (_, _) => ResetRetargetPosePreview();
            skeletalMeshRetargeterPanel.CompatibilityFixRequested += (_, _) => ApplyRetargetCompatibilityFixes();
            skeletalMeshRetargeterPanel.ExportFbxRequested += async (_, _) => await ExportRetargetFbxAsync().ConfigureAwait(true);
            skeletalMeshRetargeterPanel.ReplaceMeshRequested += async (_, _) => await ReplaceRetargetMeshAsync().ConfigureAwait(true);
        }

        private void InitializeUiEditorUi()
        {
            TabPage uiEditorPage = new()
            {
                Name = "uiEditorPage",
                Text = "UI Editor",
                UseVisualStyleBackColor = true
            };
            uiEditorPage.Controls.Add(uiEditorPanel);
            tabControl2.Controls.Add(uiEditorPage);

            uiEditorPanel.BrowsePackageRequested += (_, _) => BrowseUiEditorPackage();
            uiEditorPanel.UseCurrentPackageRequested += (_, _) => UseCurrentUiEditorPackage();
            uiEditorPanel.ScanRequested += async (_, _) => await ScanUiEditorPackageAsync().ConfigureAwait(true);
            uiEditorPanel.DryRunPatchRequested += async (_, _) => await CreateUiEditorDryRunPatchAsync().ConfigureAwait(true);
            uiEditorPanel.ScanTextureAssetsRequested += async (_, _) => await ScanUiEditorTextureAssetsAsync().ConfigureAwait(true);
            uiEditorPanel.PreviewTextureRequested += async (_, _) => await PreviewUiEditorTextureAssetAsync().ConfigureAwait(true);
            uiEditorPanel.ExportTextureRequested += async (_, _) => await ExportUiEditorTextureAssetAsync().ConfigureAwait(true);
            uiEditorPanel.ChooseReplacementRequested += (_, _) => ChooseUiEditorTextureReplacement();
            uiEditorPanel.ApplyTextureReplacementRequested += async (_, _) => await ApplyUiEditorTextureReplacementAsync().ConfigureAwait(true);
            uiEditorPanel.PreviewSwfRequested += async (_, _) => await PreviewUiEditorSwfAsync().ConfigureAwait(true);
            uiEditorPanel.ExportSwfRawRequested += async (_, _) => await ExportUiEditorSwfAsync(UiEditorSwfTransferMode.RawExport).ConfigureAwait(true);
            uiEditorPanel.ExportSwfEmbeddedRequested += async (_, _) => await ExportUiEditorSwfAsync(UiEditorSwfTransferMode.EmbeddedPayload).ConfigureAwait(true);
            uiEditorPanel.ChooseSwfImportRequested += (_, _) => ChooseUiEditorSwfImport();
            uiEditorPanel.ImportSwfRequested += async (_, _) => await ImportUiEditorSwfAsync().ConfigureAwait(true);
        }

        private void InitializeObjectsWorkspaceUi()
        {
            if (tabControl2.TabPages.Contains(propertyFilePage))
                tabControl2.TabPages.Remove(propertyFilePage);

            TabPage fileInfoPage = new()
            {
                Name = "fileInfoPage",
                Text = "File",
                UseVisualStyleBackColor = true
            };
            propertyGrid.Dock = DockStyle.Fill;
            fileInfoPage.Controls.Add(propertyGrid);
            tabControl1.TabPages.Insert(0, fileInfoPage);
            objectsPage.Text = "Objects";
        }

        private void tabControl2_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateMainLayoutForActiveTab();
            ShowRetargetWarningIfNeeded();
        }

        private void UpdateMainLayoutForActiveTab()
        {
            string activeTabName = tabControl2.SelectedTab?.Name ?? string.Empty;
            bool isObjectsWorkspace =
                string.Equals(activeTabName, "objectsPage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "propertyFilePage", StringComparison.Ordinal);
            bool isToolWorkspace =
                string.Equals(activeTabName, "enemyConverterPage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "meshWorkspacePage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "backupManagerPage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "textureWorkspacePage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "retargetWorkspacePage", StringComparison.Ordinal) ||
                string.Equals(activeTabName, "uiEditorPage", StringComparison.Ordinal);

            if (!isObjectsWorkspace)
            {
                if (!splitContainer1.Panel2Collapsed)
                    lastMainSplitterDistance = splitContainer1.SplitterDistance;

                splitContainer1.Panel2.Enabled = false;
                splitContainer1.Panel2Collapsed = true;
                return;
            }

            if (splitContainer1.Panel2Collapsed)
                splitContainer1.Panel2Collapsed = false;

            splitContainer1.Panel2.Enabled = true;

            int maxSplitterDistance = Math.Max(splitContainer1.Panel1MinSize, splitContainer1.Width - splitContainer1.Panel2MinSize - splitContainer1.SplitterWidth);
            if (maxSplitterDistance <= splitContainer1.Panel1MinSize)
                return;

            splitContainer1.SplitterDistance = Math.Min(lastMainSplitterDistance, maxSplitterDistance);
        }

        private void ShowRetargetWarningIfNeeded()
        {
            if (showingRetargetWarning || suppressRetargetWarningForSession)
                return;

            string activeTabName = tabControl2.SelectedTab?.Name ?? string.Empty;
            if (!string.Equals(activeTabName, "retargetWorkspacePage", StringComparison.Ordinal))
                return;

            showingRetargetWarning = true;
            try
            {
                SystemSounds.Exclamation.Play();
                using RetargetWarningForm warningForm = new();
                if (warningForm.ShowDialog(this) == DialogResult.OK && warningForm.SuppressForSession)
                    suppressRetargetWarningForSession = true;
            }
            finally
            {
                showingRetargetWarning = false;
            }
        }

        private void RefreshEnemyPlayableExports()
        {
            enemyPlayableExports.Clear();

            if (UpkFile?.Header?.ExportTable == null)
            {
                enemyPlayablePanel.SetMeshOptions([]);
                enemyPlayablePanel.ClearDetectedComponents();
                return;
            }

            foreach (UnrealExportTableEntry export in UpkFile.Header.ExportTable)
            {
                if (!string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = export.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                enemyPlayableExports[path] = export;
            }

            enemyPlayablePanel.SetMeshOptions(enemyPlayableExports.Keys);
            enemyPlayablePanel.SetScanFolder(Path.GetDirectoryName(GetCurrentUpkPath()) ?? string.Empty);
        }

        private async Task RefreshEnemyPlayableDetectionAsync()
        {
            EnemyPlayableSelection selection = await GetEnemyPlayableSelectionAsync().ConfigureAwait(true);
            if (selection == null)
            {
                enemyPlayablePanel.ClearDetectedComponents();
                return;
            }

            try
            {
                enemyPlayablePanel.SetBusy(true);
                EnemyComponentDetectionResult detection = await Task.Run(() => enemyPlayableConverter.DetectEnemyComponents(selection)).ConfigureAwait(true);

                enemyPlayablePanel.SetDetectionResult(detection);
                enemyPlayablePanel.SetLog(detection.Messages);
            }
            catch (Exception ex)
            {
                enemyPlayablePanel.ClearDetectedComponents();
                enemyPlayablePanel.SetLog([$"Enemy Converter detection failed: {ex.Message}"]);
                WarningBox($"Enemy Converter detection failed.\n\n{ex}");
            }
            finally
            {
                enemyPlayablePanel.SetBusy(false);
            }
        }

        private async Task ConvertEnemyToPlayableAsync()
        {
            EnemyPlayableSelection selection = await GetEnemyPlayableSelectionAsync().ConfigureAwait(true);
            if (selection == null)
            {
                WarningBox("Select an enemy SkeletalMesh export or choose a compatible scan result first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Converting enemy mesh into a playable hero bundle...";
                enemyPlayablePanel.SetBusy(true);

                EnemyPlayableConversionResult result = await Task.Run(() => enemyPlayableConverter.BuildPlayableHero(selection)).ConfigureAwait(true);

                enemyPlayablePanel.SetDetectionResult(new EnemyComponentDetectionResult
                {
                    DependencyGraph = result.DependencyGraph,
                    SkeletonCompatible = true,
                    Messages = result.Messages
                });
                enemyPlayablePanel.SetLog(result.Messages);
                progressStatus.Text = "Enemy converter completed.";

                MessageBox.Show(
                    $"Enemy converter output written to:\n{result.PackageResult.OutputDirectory}\n\nPatched game tables: {result.PatchPlan.PatchedFiles.Count}",
                    "Enemy Converter Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "Enemy converter failed.";
                WarningBox($"Enemy Converter failed.\n\n{ex}");
            }
            finally
            {
                enemyPlayablePanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task ScanEnemyPlayableCurrentFolderAsync()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first so the Enemy Converter can scan that package folder.");
                return;
            }

            string folderPath = Path.GetDirectoryName(currentUpkPath);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                WarningBox("The current UPK folder could not be determined.");
                return;
            }

            await ScanEnemyPlayableFolderAsync(folderPath).ConfigureAwait(true);
        }

        private async Task BrowseEnemyPlayableScanFolderAsync()
        {
            using FolderBrowserDialog dialog = new()
            {
                Description = "Select the UPK folder to scan for compatible enemy SkeletalMeshes.",
                UseDescriptionForTitle = true
            };

            string currentUpkPath = GetCurrentUpkPath();
            string initialPath = Path.GetDirectoryName(currentUpkPath);
            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                dialog.InitialDirectory = initialPath;

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            await ScanEnemyPlayableFolderAsync(dialog.SelectedPath).ConfigureAwait(true);
        }

        private async Task ScanEnemyPlayableFolderAsync(string folderPath)
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Scanning UPK folder for compatible enemy meshes...";
                enemyPlayablePanel.SetBusy(true);
                enemyPlayablePanel.SetScanFolder(folderPath);
                enemyPlayablePanel.SetLog([$"Enemy Converter: scanning {folderPath}..."]);

                EnemyScanSummary summary = await Task.Run(() => enemyPlayableConverter.ScanFolderForPlayableEnemies(folderPath, _ => { })).ConfigureAwait(true);

                enemyPlayableScanCandidates.Clear();
                foreach (EnemyScanCandidate candidate in summary.Candidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate.SelectionKey))
                        enemyPlayableScanCandidates[candidate.SelectionKey] = candidate;
                }

                enemyPlayablePanel.SetScanResults(summary);
                List<string> logLines =
                [
                    $"Enemy Converter: scanned {summary.Candidates.Count} candidates in {summary.FolderPath}.",
                    $"Ready={summary.ReadyCount}, Warning={summary.WarningCount}, Incompatible={summary.IncompatibleCount}, Error={summary.ErrorCount}"
                ];
                foreach (EnemyScanCandidate candidate in summary.Candidates.Where(static item => item.Status == EnemyScanCompatibilityStatus.Ready || item.Status == EnemyScanCompatibilityStatus.Warning).Take(25))
                    logLines.Add($"{candidate.Status}: {candidate.DisplayName} | {candidate.WarningSummary}");

                enemyPlayablePanel.SetLog(logLines);
                progressStatus.Text = "Enemy converter scan completed.";
                await RefreshEnemyPlayableDetectionAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "Enemy converter scan failed.";
                enemyPlayablePanel.SetLog([$"Enemy Converter scan failed: {ex.Message}"]);
                WarningBox($"Enemy Converter scan failed.\n\n{ex}");
            }
            finally
            {
                enemyPlayablePanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private void BrowseUiEditorPackage()
        {
            string preferredFolder = GetPreferredUpkBrowseFolder();
            List<string> candidatePackages = GetUiPackageCandidates(preferredFolder);
            if (candidatePackages.Count > 0)
            {
                using UiPackageSelectionForm selectionForm = new(candidatePackages, preferredFolder);
                DialogResult result = selectionForm.ShowDialog(this);
                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(selectionForm.SelectedPackagePath))
                {
                    uiEditorPanel.SetPackagePath(selectionForm.SelectedPackagePath);
                    uiEditorPanel.AppendLog($"UI Editor: selected package {selectionForm.SelectedPackagePath}");
                    return;
                }

                if (result != DialogResult.Retry)
                    return;
            }

            using OpenFileDialog dialog = CreateUpkOpenDialog("Select Cooked UI Package", "Cooked UI Packages (*.upk)|*.upk");
            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                return;

            uiEditorPanel.SetPackagePath(dialog.FileName);
            uiEditorPanel.AppendLog($"UI Editor: selected package {dialog.FileName}");
        }

        private void UseCurrentUiEditorPackage()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first, or browse directly to a cooked UI package.");
                return;
            }

            uiEditorPanel.SetPackagePath(currentUpkPath);
            uiEditorPanel.AppendLog($"UI Editor: using current package {currentUpkPath}");
        }

        private async Task ScanUiEditorPackageAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Scanning cooked UI package...";
                uiEditorPanel.SetBusy(true);
                uiEditorPanel.SetLog([$"UI Editor: scanning {packagePath}"]);

                IReadOnlyList<EnemyClientUiTarget> targets = await uiEditorTool
                    .ScanPackageAsync(packagePath, uiEditorPanel.SubjectName)
                    .ConfigureAwait(true);

                uiEditorPanel.SetTargets(targets);
                uiEditorPanel.AppendLog($"UI Editor: found {targets.Count} matching exports in {Path.GetFileName(packagePath)}.");
                progressStatus.Text = "UI package scan completed.";
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI package scan failed.";
                uiEditorPanel.AppendLog($"UI Editor scan failed: {ex.Message}");
                WarningBox($"UI Editor scan failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task CreateUiEditorDryRunPatchAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Creating UI dry-run patched copy...";
                uiEditorPanel.SetBusy(true);

                string outputDirectory = GetUiEditorOutputDirectory(packagePath);
                EnemyClientUiPatchExperimentResult result = await uiEditorTool
                    .CreateDryRunPatchedCopyAsync(packagePath, uiEditorPanel.SubjectName, outputDirectory, uiEditorPanel.AppendLog)
                    .ConfigureAwait(true);

                progressStatus.Text = "UI dry-run patched copy created.";
                MessageBox.Show(
                    $"UI Editor dry-run output written to:\n{result.OutputPackagePath}\n\nReport:\n{result.ReportPath}",
                    "UI Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI dry-run patch failed.";
                uiEditorPanel.AppendLog($"UI Editor dry-run patch failed: {ex.Message}");
                WarningBox($"UI Editor dry-run patch failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task ScanUiEditorTextureAssetsAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Scanning UI image assets...";
                uiEditorPanel.SetBusy(true);

                IReadOnlyList<UiEditorTextureAsset> assets = await uiEditorTool
                    .ScanTextureAssetsAsync(packagePath, uiEditorPanel.SubjectName)
                    .ConfigureAwait(true);

                uiEditorPanel.SetTextureAssets(assets);
                uiEditorPanel.AppendLog($"UI Editor: found {assets.Count} Texture2D asset(s) in {Path.GetFileName(packagePath)}.");
                progressStatus.Text = "UI image asset scan completed.";
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI image asset scan failed.";
                uiEditorPanel.AppendLog($"UI Editor image asset scan failed: {ex.Message}");
                WarningBox($"UI Editor image asset scan failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private void ChooseUiEditorTextureReplacement()
        {
            if (uiEditorPanel.SelectedTextureAsset == null)
            {
                WarningBox("Select a Texture2D asset first.");
                return;
            }

            using OpenFileDialog dialog = new()
            {
                Filter = "Texture Files|*.png;*.jpg;*.jpeg;*.dds;*.tga",
                Title = $"Select Replacement For {uiEditorPanel.SelectedTextureAsset.ExportPath}"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                return;

            uiEditorPanel.SetTextureReplacementFile(dialog.FileName);
            uiEditorPanel.AppendLog($"UI Editor: assigned replacement file {dialog.FileName}.");
        }

        private async Task PreviewUiEditorTextureAssetAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            UiEditorTextureAsset asset = uiEditorPanel.SelectedTextureAsset;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (asset == null)
            {
                WarningBox("Select a Texture2D asset first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Loading UI image preview...";
                uiEditorPanel.SetBusy(true);

                using TexturePreviewTexture texture = await uiEditorTool
                    .LoadTexturePreviewAsync(packagePath, asset, uiEditorPanel.AppendLog)
                    .ConfigureAwait(true);

                using UiImagePreviewForm previewForm = new(texture);
                previewForm.ShowDialog(this);
                uiEditorPanel.AppendLog($"UI Editor: previewed {asset.ExportPath}.");
                progressStatus.Text = "UI image preview loaded.";
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI image preview failed.";
                uiEditorPanel.AppendLog($"UI Editor image preview failed: {ex.Message}");
                WarningBox($"UI Editor image preview failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task ExportUiEditorTextureAssetAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            UiEditorTextureAsset asset = uiEditorPanel.SelectedTextureAsset;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (asset == null)
            {
                WarningBox("Select a Texture2D asset first.");
                return;
            }

            using SaveFileDialog dialog = new()
            {
                Filter = "PNG Image (*.png)|*.png|DDS Container (*.dds)|*.dds|Bitmap (*.bmp)|*.bmp|JPEG Image (*.jpg)|*.jpg",
                Title = $"Export {asset.ExportPath}",
                FileName = $"{SanitizeExportFileName(asset.ExportPath.Replace('.', '_'))}.png"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Exporting UI image asset...";
                uiEditorPanel.SetBusy(true);

                await uiEditorTool.ExportTextureAssetAsync(packagePath, asset, dialog.FileName, uiEditorPanel.AppendLog)
                    .ConfigureAwait(true);

                progressStatus.Text = "UI image asset exported.";
                MessageBox.Show(
                    $"Exported image asset:\n{asset.ExportPath}\n\nOutput:\n{dialog.FileName}",
                    "UI Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI image export failed.";
                uiEditorPanel.AppendLog($"UI Editor image export failed: {ex.Message}");
                WarningBox($"UI Editor image export failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task ApplyUiEditorTextureReplacementAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            UiEditorTextureAsset asset = uiEditorPanel.SelectedTextureAsset;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (asset == null)
            {
                WarningBox("Select a Texture2D asset first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(asset.ReplacementFilePath) || !File.Exists(asset.ReplacementFilePath))
            {
                WarningBox("Choose a replacement image first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Applying UI image replacement...";
                uiEditorPanel.SetBusy(true);
                uiEditorPanel.AppendLog($"UI Editor: injecting {asset.ReplacementFilePath} into {asset.ExportPath}.");

                await uiEditorTool.InjectTextureAssetAsync(packagePath, asset, uiEditorPanel.AppendLog).ConfigureAwait(true);

                progressStatus.Text = "UI image replacement applied.";
                MessageBox.Show(
                    $"Applied replacement image to:\n{asset.ExportPath}\n\nPackage:\n{packagePath}",
                    "UI Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "UI image replacement failed.";
                uiEditorPanel.AppendLog($"UI Editor image replacement failed: {ex.Message}");
                WarningBox($"UI Editor image replacement failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task PreviewUiEditorSwfAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            EnemyClientUiTarget target = uiEditorPanel.SelectedTarget;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (target == null || !string.Equals(target.ClassName, "swfmovie", StringComparison.OrdinalIgnoreCase))
            {
                WarningBox("Select a swfmovie export first.");
                return;
            }

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Inspecting SWF export...";
                uiEditorPanel.SetBusy(true);

                UiEditorSwfPreview preview = await uiEditorTool.PreviewSwfMovieAsync(packagePath, target).ConfigureAwait(true);
                uiEditorPanel.SetSwfPreview(preview.BuildSummaryText());
                uiEditorPanel.AppendLog($"UI Editor: previewed SWF export {target.ExportPath}.");
                progressStatus.Text = "SWF export preview loaded.";
            }
            catch (Exception ex)
            {
                progressStatus.Text = "SWF preview failed.";
                uiEditorPanel.AppendLog($"UI Editor SWF preview failed: {ex.Message}");
                WarningBox($"UI Editor SWF preview failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private async Task ExportUiEditorSwfAsync(UiEditorSwfTransferMode mode)
        {
            string packagePath = uiEditorPanel.PackagePath;
            EnemyClientUiTarget target = uiEditorPanel.SelectedTarget;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (target == null || !string.Equals(target.ClassName, "swfmovie", StringComparison.OrdinalIgnoreCase))
            {
                WarningBox("Select a swfmovie export first.");
                return;
            }

            string defaultExtension = mode == UiEditorSwfTransferMode.RawExport ? "bin" : "swf";
            using SaveFileDialog dialog = new()
            {
                Filter = mode == UiEditorSwfTransferMode.RawExport
                    ? "Raw Export (*.bin)|*.bin|All Files (*.*)|*.*"
                    : "SWF / GFX (*.swf;*.gfx)|*.swf;*.gfx|All Files (*.*)|*.*",
                Title = mode == UiEditorSwfTransferMode.RawExport ? "Export Raw SWF Export" : "Export Embedded SWF/GFX",
                FileName = $"{SanitizeExportFileName(target.ExportPath.Replace('.', '_'))}.{defaultExtension}"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Exporting SWF export...";
                uiEditorPanel.SetBusy(true);

                await uiEditorTool.ExportSwfMovieAsync(packagePath, target, dialog.FileName, mode, uiEditorPanel.AppendLog)
                    .ConfigureAwait(true);

                progressStatus.Text = "SWF export completed.";
                MessageBox.Show(
                    $"Exported {target.ExportPath} to:\n{dialog.FileName}",
                    "UI Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "SWF export failed.";
                uiEditorPanel.AppendLog($"UI Editor SWF export failed: {ex.Message}");
                WarningBox($"UI Editor SWF export failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private void ChooseUiEditorSwfImport()
        {
            EnemyClientUiTarget target = uiEditorPanel.SelectedTarget;
            if (target == null || !string.Equals(target.ClassName, "swfmovie", StringComparison.OrdinalIgnoreCase))
            {
                WarningBox("Select a swfmovie export first.");
                return;
            }

            using OpenFileDialog dialog = new()
            {
                Filter = "SWF / GFX / Raw Export|*.swf;*.gfx;*.bin|All Files (*.*)|*.*",
                Title = $"Choose Import File For {target.ExportPath}"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                return;

            uiEditorPanel.SetSwfImportFile(dialog.FileName);
            uiEditorPanel.AppendLog($"UI Editor: assigned SWF import file {dialog.FileName}.");
        }

        private async Task ImportUiEditorSwfAsync()
        {
            string packagePath = uiEditorPanel.PackagePath;
            EnemyClientUiTarget target = uiEditorPanel.SelectedTarget;
            string importPath = uiEditorPanel.SwfImportFilePath;
            if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
            {
                WarningBox("Pick a cooked UI package first.");
                return;
            }

            if (target == null || !string.Equals(target.ClassName, "swfmovie", StringComparison.OrdinalIgnoreCase))
            {
                WarningBox("Select a swfmovie export first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
            {
                WarningBox("Choose an import file first.");
                return;
            }

            UiEditorSwfTransferMode mode = Path.GetExtension(importPath).Equals(".bin", StringComparison.OrdinalIgnoreCase)
                ? UiEditorSwfTransferMode.RawExport
                : UiEditorSwfTransferMode.EmbeddedPayload;

            DialogResult confirm = MessageBox.Show(
                $"This will replace the selected swfmovie export inside:\n{packagePath}\n\nA backup will be written next to it as:\n{packagePath}.bak\n\nContinue?",
                "Re-Import SWF Export",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Re-importing SWF export...";
                uiEditorPanel.SetBusy(true);

                string backupPath = await uiEditorTool.ImportSwfMovieAsync(packagePath, target, importPath, mode, uiEditorPanel.AppendLog)
                    .ConfigureAwait(true);

                progressStatus.Text = "SWF export re-imported.";
                MessageBox.Show(
                    $"Re-imported {target.ExportPath}.\n\nPackage:\n{packagePath}\n\nBackup:\n{backupPath}",
                    "UI Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "SWF re-import failed.";
                uiEditorPanel.AppendLog($"UI Editor SWF re-import failed: {ex.Message}");
                WarningBox($"UI Editor SWF re-import failed.\n\n{ex}");
            }
            finally
            {
                uiEditorPanel.SetBusy(false);
                Cursor.Current = Cursors.Default;
            }
        }

        private static string GetUiEditorOutputDirectory(string packagePath)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string packageName = Path.GetFileNameWithoutExtension(packagePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(desktopPath, "MHUPKManager Output", "UiEditor", packageName, timestamp);
        }

        private async Task<EnemyPlayableSelection> GetEnemyPlayableSelectionAsync()
        {
            EnemyScanCandidate scanCandidate = enemyPlayablePanel.SelectedScanCandidate;
            if (scanCandidate != null && !string.IsNullOrWhiteSpace(scanCandidate.SourceUpkPath) && !string.IsNullOrWhiteSpace(scanCandidate.MeshExportPath))
            {
                UnrealHeader header = await GetEnemyPlayableHeaderAsync(scanCandidate.SourceUpkPath).ConfigureAwait(true);
                UnrealExportTableEntry meshExport = header?.ExportTable?
                    .FirstOrDefault(export => string.Equals(export.GetPathName(), scanCandidate.MeshExportPath, StringComparison.OrdinalIgnoreCase));

                if (header != null && meshExport != null)
                {
                    return new EnemyPlayableSelection
                    {
                        Header = header,
                        MeshExport = meshExport,
                        SourceUpkPath = scanCandidate.SourceUpkPath
                    };
                }
            }

            if (UpkFile?.Header == null || string.IsNullOrWhiteSpace(enemyPlayablePanel.SelectedMeshName))
                return null;

            if (!enemyPlayableExports.TryGetValue(enemyPlayablePanel.SelectedMeshName, out UnrealExportTableEntry currentMeshExport))
                return null;

            return new EnemyPlayableSelection
            {
                Header = UpkFile.Header,
                MeshExport = currentMeshExport,
                SourceUpkPath = GetCurrentUpkPath()
            };
        }

        private async Task<UnrealHeader> GetEnemyPlayableHeaderAsync(string upkPath)
        {
            if (string.IsNullOrWhiteSpace(upkPath))
                return null;

            string currentUpkPath = GetCurrentUpkPath();
            if (!string.IsNullOrWhiteSpace(currentUpkPath) && string.Equals(currentUpkPath, upkPath, StringComparison.OrdinalIgnoreCase) && UpkFile?.Header != null)
                return UpkFile.Header;

            if (enemyPlayableHeaderCache.TryGetValue(upkPath, out UnrealHeader cachedHeader))
                return cachedHeader;

            UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
            enemyPlayableHeaderCache[upkPath] = header;
            return header;
        }

        private async Task BrowseMeshImporterUpkAsync()
        {
            using OpenFileDialog dialog = CreateUpkOpenDialog("Select UPK for Mesh Importer");

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            meshImporterPanel.UpkPath = dialog.FileName;
            await LoadMeshImporterExportsAsync(dialog.FileName).ConfigureAwait(true);
        }

        private async Task UseCurrentMeshImporterUpkAsync()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first.");
                return;
            }

            meshImporterPanel.UpkPath = currentUpkPath;
            await LoadMeshImporterExportsAsync(currentUpkPath).ConfigureAwait(true);
        }

        private async Task BrowseMeshExporterUpkAsync()
        {
            using OpenFileDialog dialog = CreateUpkOpenDialog("Select UPK for Mesh Exporter");

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            meshExporterPanel.UpkPath = dialog.FileName;
            await LoadMeshExporterExportsAsync(dialog.FileName).ConfigureAwait(true);
        }

        private async Task UseCurrentMeshExporterUpkAsync()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first.");
                return;
            }

            meshExporterPanel.UpkPath = currentUpkPath;
            await LoadMeshExporterExportsAsync(currentUpkPath).ConfigureAwait(true);
        }

        private async Task BrowseRetargeterUpkAsync()
        {
            using OpenFileDialog dialog = CreateUpkOpenDialog("Select UPK for SkeletalMesh Retargeter");

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            skeletalMeshRetargeterPanel.UpkPath = dialog.FileName;
            await LoadRetargeterExportsAsync(dialog.FileName).ConfigureAwait(true);
        }

        private async Task UseCurrentRetargeterUpkAsync()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first.");
                return;
            }

            skeletalMeshRetargeterPanel.UpkPath = currentUpkPath;
            await LoadRetargeterExportsAsync(currentUpkPath).ConfigureAwait(true);
        }

        private async Task UseCurrentMeshPreviewUpkAsync()
        {
            string currentUpkPath = GetCurrentUpkPath();
            if (string.IsNullOrWhiteSpace(currentUpkPath))
            {
                WarningBox("Open a UPK file first.");
                return;
            }

            string exportPath = GetCurrentSkeletalMeshExportPath();
            if (string.IsNullOrWhiteSpace(exportPath))
                exportPath = await meshPreviewPanel.PromptForSkeletalMeshExportAsync(currentUpkPath).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(exportPath))
                return;

            try
            {
                progressStatus.Text = "Loading current UPK SkeletalMesh into Mesh Preview...";
                await meshPreviewPanel.LoadUe3MeshFromUpkAsync(currentUpkPath, exportPath).ConfigureAwait(true);
                progressStatus.Text = "Mesh Preview loaded current UPK SkeletalMesh.";
            }
            catch (Exception ex)
            {
                progressStatus.Text = "Mesh Preview failed to load current UPK SkeletalMesh.";
                WarningBox($"Mesh Preview failed to load the current UPK SkeletalMesh.\n\n{ex}");
            }
        }

        private void BrowseMeshImporterFbx(object sender, EventArgs e)
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "FBX Files (*.fbx)|*.fbx",
                Title = "Select FBX to Import"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                meshImporterPanel.FbxPath = dialog.FileName;
        }

        private void BrowseMeshExporterFbx(object sender, EventArgs e)
        {
            using SaveFileDialog dialog = new()
            {
                Filter = "FBX Files (*.fbx)|*.fbx",
                Title = "Save SkeletalMesh As FBX",
                FileName = SanitizeExportFileName(meshExporterPanel.SelectedMeshName) + ".fbx"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                meshExporterPanel.FbxPath = dialog.FileName;
        }

        private async Task LoadMeshExporterExportsAsync(string upkPath)
        {
            meshExporterPanel.ClearLog();
            meshExporterPanel.AppendLog($"Loading SkeletalMesh exports from {upkPath}");
            meshExporterPanel.SetBusy(true);

            try
            {
                meshExporterHeader = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
                await meshExporterHeader.ReadHeaderAsync(null).ConfigureAwait(true);

                meshExporterExports.Clear();
                List<string> names = [];
                foreach (UnrealExportTableEntry export in meshExporterHeader.ExportTable)
                {
                    if (!string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string path = export.GetPathName();
                    meshExporterExports[path] = export;
                    names.Add(path);
                }

                meshExporterPanel.SetMeshOptions(names);
                meshExporterPanel.AppendLog($"Found {names.Count} SkeletalMesh exports.");
                await RefreshMeshExporterLodsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                meshExporterPanel.AppendLog($"Failed to load SkeletalMesh exports: {ex.Message}");
                WarningBox($"Mesh Exporter failed to load exports.\n\n{ex}");
            }
            finally
            {
                meshExporterPanel.SetBusy(false);
            }
        }

        private async Task RefreshMeshExporterLodsAsync()
        {
            string meshName = meshExporterPanel.SelectedMeshName;
            if (string.IsNullOrWhiteSpace(meshName) || !meshExporterExports.TryGetValue(meshName, out UnrealExportTableEntry export))
                return;

            try
            {
                meshExporterPanel.AppendLog($"Inspecting LODs for {meshName}");
                if (export.UnrealObject == null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                if (export.UnrealObject is UpkManager.Models.UpkFile.Objects.IUnrealObject unrealObject && unrealObject.UObject is USkeletalMesh skeletalMesh)
                {
                    meshExporterPanel.SetLodOptions(skeletalMesh.LODModels.Count);
                    meshExporterPanel.AppendLog($"LOD count: {skeletalMesh.LODModels.Count}");
                }
                else
                {
                    meshExporterPanel.SetLodOptions(1);
                }
            }
            catch (Exception ex)
            {
                meshExporterPanel.AppendLog($"Failed to inspect LODs: {ex.Message}");
            }
        }

        private async Task ExportMeshFromPanelAsync()
        {
            string exportUpkPath = meshExporterPanel.UpkPath;
            string exportFbxPath = meshExporterPanel.FbxPath;
            string exportMeshName = meshExporterPanel.SelectedMeshName;
            int exportLodIndex = meshExporterPanel.SelectedLodIndex;

            if (string.IsNullOrWhiteSpace(exportUpkPath) ||
                string.IsNullOrWhiteSpace(exportFbxPath) ||
                string.IsNullOrWhiteSpace(exportMeshName))
            {
                WarningBox("Select a UPK file, SkeletalMesh export, and output FBX path first.");
                return;
            }

            if (!meshExporterExports.TryGetValue(exportMeshName, out UnrealExportTableEntry export))
            {
                WarningBox("The selected SkeletalMesh export is no longer available.");
                return;
            }

            meshExporterPanel.SetBusy(true);
            meshExporterPanel.ClearLog();

            try
            {
                meshExporterPanel.ReportProgress(5, 100, "Preparing SkeletalMesh export.");
                if (export.UnrealObject == null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                if (export.UnrealObject is not UpkManager.Models.UpkFile.Objects.IUnrealObject unrealObject ||
                    unrealObject.UObject is not USkeletalMesh skeletalMesh)
                {
                    throw new InvalidOperationException("The selected export is not a SkeletalMesh.");
                }

                meshExporterPanel.ReportProgress(20, 100, $"Loaded SkeletalMesh '{exportMeshName}'.");
                meshExporterPanel.ReportProgress(40, 100, $"Exporting LOD {exportLodIndex} to FBX.");

                await Task.Run(() =>
                {
                    SkeletalFbxExporter.Export(
                        exportFbxPath,
                        skeletalMesh,
                        exportMeshName,
                        exportLodIndex,
                        meshExporterPanel.AppendLog);
                }).ConfigureAwait(true);

                meshExporterPanel.ReportProgress(100, 100, "FBX export completed.");
                MessageBox.Show(
                    $"SkeletalMesh export completed.\n\nExported:\n{exportMeshName}\n\nSaved to:\n{exportFbxPath}",
                    "Mesh Exporter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                meshExporterPanel.AppendLog($"Export failed: {ex.Message}");
                MessageBox.Show(
                    $"Mesh export failed.\n\n{ex}",
                    "Mesh Exporter Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                meshExporterPanel.SetBusy(false);
                progressStatus.Text = string.Empty;
            }
        }

        private async Task LoadMeshImporterExportsAsync(string upkPath)
        {
            meshImporterPanel.ClearLog();
            meshImporterPanel.AppendLog($"Loading SkeletalMesh exports from {upkPath}");
            meshImporterPanel.SetBusy(true);

            try
            {
                meshImporterHeader = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
                await meshImporterHeader.ReadHeaderAsync(null).ConfigureAwait(true);

                meshImporterExports.Clear();
                List<string> names = [];
                foreach (UnrealExportTableEntry export in meshImporterHeader.ExportTable)
                {
                    if (!string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string path = export.GetPathName();
                    meshImporterExports[path] = export;
                    names.Add(path);
                }

                meshImporterPanel.SetMeshOptions(names);
                meshImporterPanel.AppendLog($"Found {names.Count} SkeletalMesh exports.");
                await RefreshMeshImporterLodsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                meshImporterPanel.AppendLog($"Failed to load SkeletalMesh exports: {ex.Message}");
                WarningBox($"Mesh Importer failed to load exports.\n\n{ex}");
            }
            finally
            {
                meshImporterPanel.SetBusy(false);
            }
        }

        private async Task RefreshMeshImporterLodsAsync()
        {
            string meshName = meshImporterPanel.SelectedMeshName;
            if (string.IsNullOrWhiteSpace(meshName) || !meshImporterExports.TryGetValue(meshName, out UnrealExportTableEntry export))
                return;

            try
            {
                meshImporterPanel.AppendLog($"Inspecting LODs for {meshName}");
                if (export.UnrealObject == null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                if (export.UnrealObject is UpkManager.Models.UpkFile.Objects.IUnrealObject unrealObject && unrealObject.UObject is USkeletalMesh skeletalMesh)
                {
                    meshImporterPanel.SetLodOptions(skeletalMesh.LODModels.Count);
                    meshImporterPanel.AppendLog($"LOD count: {skeletalMesh.LODModels.Count}");
                }
                else
                {
                    meshImporterPanel.SetLodOptions(1);
                }
            }
            catch (Exception ex)
            {
                meshImporterPanel.AppendLog($"Failed to inspect LODs: {ex.Message}");
            }
        }

        private async Task LoadRetargeterExportsAsync(string upkPath)
        {
            skeletalMeshRetargeterPanel.ClearLog();
            skeletalMeshRetargeterPanel.AppendLog($"Loading SkeletalMesh exports from {upkPath}");
            skeletalMeshRetargeterPanel.SetBusy(true);

            try
            {
                retargeterHeader = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
                await retargeterHeader.ReadHeaderAsync(null).ConfigureAwait(true);

                retargeterExports.Clear();
                List<string> names = [];
                foreach (UnrealExportTableEntry export in retargeterHeader.ExportTable)
                {
                    if (!string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string path = export.GetPathName();
                    retargeterExports[path] = export;
                    names.Add(path);
                }

                skeletalMeshRetargeterPanel.SetMeshOptions(names);
                skeletalMeshRetargeterPanel.AppendLog($"Found {names.Count} SkeletalMesh exports.");
                await RefreshRetargeterLodsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Failed to load SkeletalMesh exports: {ex.Message}");
                WarningBox($"SkeletalMesh Retargeter failed to load exports.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private async Task RefreshRetargeterLodsAsync()
        {
            string meshName = skeletalMeshRetargeterPanel.SelectedMeshName;
            if (string.IsNullOrWhiteSpace(meshName) || !retargeterExports.TryGetValue(meshName, out UnrealExportTableEntry export))
                return;

            try
            {
                skeletalMeshRetargeterPanel.AppendLog($"Inspecting LODs for {meshName}");
                if (export.UnrealObject == null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                if (export.UnrealObject is IUnrealObject unrealObject && unrealObject.UObject is USkeletalMesh skeletalMesh)
                {
                    skeletalMeshRetargeterPanel.SetLodOptions(skeletalMesh.LODModels.Count);
                    skeletalMeshRetargeterPanel.AppendLog($"LOD count: {skeletalMesh.LODModels.Count}");
                    AutoLoadRetargetTargetContext(meshName, skeletalMesh);
                }
                else
                {
                    skeletalMeshRetargeterPanel.SetLodOptions(1);
                }
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Failed to inspect LODs: {ex.Message}");
            }
        }

        private void AutoLoadRetargetTargetContext(string meshExportPath, USkeletalMesh skeletalMesh)
        {
            try
            {
                retargetPlayerSkeleton = BuildSkeletonDefinitionFromSkeletalMesh(meshExportPath, skeletalMesh);
                retargetReferenceMesh = new MhoSkeletalMeshConverter().Convert(
                    skeletalMesh,
                    meshExportPath,
                    skeletalMeshRetargeterPanel.SelectedLodIndex,
                    skeletalMeshRetargeterPanel.AppendLog);
                skeletalMeshRetargeterPanel.PlayerSkeletonPath = $"[Auto] {meshExportPath} skeleton";
                skeletalMeshRetargeterPanel.AppendLog($"Auto-loaded destination skeleton from selected game SkeletalMesh with {retargetPlayerSkeleton.Bones.Count} bones.");
                skeletalMeshRetargeterPanel.AppendLog($"Auto-loaded original MHO weighted source mesh with {retargetReferenceMesh.VertexCount} vertices.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Automatic game skeleton/weighted source load failed: {ex.Message}");
                return;
            }

            try
            {
                UAnimSet animSet = DiscoverAnimSetForSkeletalMesh(meshExportPath, skeletalMesh);
                retargetAnimSet = animSet;
                if (animSet != null)
                {
                    string animSetName = animSet.PreviewSkelMeshName?.Name;
                    string display = string.IsNullOrWhiteSpace(animSetName) ? "[Auto] AnimSet discovered" : $"[Auto] {animSetName}";
                    skeletalMeshRetargeterPanel.AnimSetDisplay = display;
                    skeletalMeshRetargeterPanel.AppendLog($"Auto-loaded AnimSet context with {animSet.TrackBoneNames?.Count ?? 0} track bone(s).");
                }
                else
                {
                    skeletalMeshRetargeterPanel.AnimSetDisplay = "[Auto] No AnimSet found";
                    skeletalMeshRetargeterPanel.AppendLog("No linked AnimSet was discovered automatically for the selected game SkeletalMesh.");
                }
            }
            catch (Exception ex)
            {
                retargetAnimSet = null;
                skeletalMeshRetargeterPanel.AnimSetDisplay = "[Auto] AnimSet load failed";
                skeletalMeshRetargeterPanel.AppendLog($"Automatic AnimSet discovery failed: {ex.Message}");
            }
        }

        private async Task ImportRetargetMeshAsync()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "Mesh Files (*.fbx;*.psk)|*.fbx;*.psk",
                Title = "Import Mesh (.psk/.fbx)"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            skeletalMeshRetargeterPanel.SetBusy(true);
            try
            {
                skeletalMeshRetargeterPanel.ReportProgress(10, 100, "Importing mesh data.");
                MHUpkManager.Retargeting.MeshImporter importer = new();
                retargetSourceMesh = await Task.Run(() => importer.Import(dialog.FileName, skeletalMeshRetargeterPanel.AppendLog)).ConfigureAwait(true);
                retargetProcessedMesh = null;
                retargetBoneMapping = null;
                skeletalMeshRetargeterPanel.MeshPath = dialog.FileName;
                skeletalMeshRetargeterPanel.SetMapping(Array.Empty<KeyValuePair<string, string>>());
                skeletalMeshRetargeterPanel.SetAutoOrientationSummary("No automatic orientation has been applied.");
                skeletalMeshRetargeterPanel.SetAutoScaleSummary($"Current imported mesh scale: {retargetSourceMesh.AppliedScale:0.###}x");
                skeletalMeshRetargeterPanel.SetAutoRigSummary("One-click bind to the original MHO skeleton has not been run.");
                skeletalMeshRetargeterPanel.SetPosePreviewSummary("Pose preview is ready after weight transfer or one-click bind.");
                skeletalMeshRetargeterPanel.ClearPosePreview();
                skeletalMeshRetargeterPanel.AppendLog($"Source mesh bone count: {retargetSourceMesh.Bones.Count}");
                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Imported {retargetSourceMesh.VertexCount} vertices, {retargetSourceMesh.TriangleCount} triangles, {retargetSourceMesh.Sections.Count} sections, {retargetSourceMesh.Bones.Count} source bones.");
                skeletalMeshRetargeterPanel.ReportProgress(100, 100, "Mesh import completed.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Mesh import failed: {ex.Message}");
                WarningBox($"SkeletalMesh Retargeter mesh import failed.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private async Task ImportRetargetSkeletonAsync()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "FBX Files (*.fbx)|*.fbx",
                Title = "Import Player Skeleton (.fbx)"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            skeletalMeshRetargeterPanel.SetBusy(true);
            try
            {
                skeletalMeshRetargeterPanel.ReportProgress(15, 100, "Importing player skeleton.");
                SkeletonImporter importer = new();
                retargetPlayerSkeleton = await Task.Run(() => importer.Import(dialog.FileName, skeletalMeshRetargeterPanel.AppendLog)).ConfigureAwait(true);
                retargetProcessedMesh = null;
                retargetBoneMapping = null;
                skeletalMeshRetargeterPanel.PlayerSkeletonPath = dialog.FileName;
                skeletalMeshRetargeterPanel.SetMapping(Array.Empty<KeyValuePair<string, string>>());
                skeletalMeshRetargeterPanel.SetAutoOrientationSummary("No automatic orientation has been applied.");
                skeletalMeshRetargeterPanel.SetAutoScaleSummary("No automatic scale has been applied.");
                skeletalMeshRetargeterPanel.SetAutoRigSummary("One-click bind to the original MHO skeleton has not been run.");
                skeletalMeshRetargeterPanel.SetPosePreviewSummary("Pose preview is ready after weight transfer or one-click bind.");
                skeletalMeshRetargeterPanel.ClearPosePreview();
                skeletalMeshRetargeterPanel.ReportProgress(100, 100, "Player skeleton import completed.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Player skeleton import failed: {ex.Message}");
                WarningBox($"SkeletalMesh Retargeter skeleton import failed.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private async Task ImportRetargetAnimSetAsync()
        {
            if (string.IsNullOrWhiteSpace(skeletalMeshRetargeterPanel.UpkPath))
            {
                WarningBox("Select a UPK file first.");
                return;
            }

            skeletalMeshRetargeterPanel.SetBusy(true);
            try
            {
                UnrealHeader header = await repository.LoadUpkFile(skeletalMeshRetargeterPanel.UpkPath).ConfigureAwait(true);
                await header.ReadHeaderAsync(null).ConfigureAwait(true);

                List<UnrealExportTableEntry> animSetExports = header.ExportTable
                    .Where(export => string.Equals(export.ClassReferenceNameIndex?.Name, nameof(UAnimSet), StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(export.ClassReferenceNameIndex?.Name, "AnimSet", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static export => export.GetPathName(), StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (animSetExports.Count == 0)
                    throw new InvalidOperationException("The selected UPK did not contain any AnimSet exports.");

                using ExportSelectionForm selectionForm = new(animSetExports.Select(static export => export.GetPathName()), "Select AnimSet Export");
                if (selectionForm.ShowDialog(this) != DialogResult.OK)
                    return;

                UnrealExportTableEntry export = animSetExports.First(entry => string.Equals(entry.GetPathName(), selectionForm.SelectedValue, StringComparison.OrdinalIgnoreCase));
                if (export.UnrealObject == null)
                    await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UAnimSet animSet)
                    throw new InvalidOperationException("The selected export is not an AnimSet.");

                retargetAnimSet = animSet;
                if (retargetSourceMesh != null)
                    retargetSourceMesh.AnimSet = animSet;
                if (retargetProcessedMesh != null)
                    retargetProcessedMesh.AnimSet = animSet;

                skeletalMeshRetargeterPanel.AnimSetDisplay = selectionForm.SelectedValue;
                skeletalMeshRetargeterPanel.AppendLog($"Imported AnimSet '{selectionForm.SelectedValue}' with {animSet.TrackBoneNames?.Count ?? 0} track bone(s).");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"AnimSet import failed: {ex.Message}");
                WarningBox($"SkeletalMesh Retargeter AnimSet import failed.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private SkeletonDefinition BuildSkeletonDefinitionFromSkeletalMesh(string meshExportPath, USkeletalMesh skeletalMesh)
        {
            SkeletonDefinition skeleton = new()
            {
                SourcePath = meshExportPath
            };

            List<Matrix4x4> globals = new(skeletalMesh.RefSkeleton.Count);
            for (int i = 0; i < skeletalMesh.RefSkeleton.Count; i++)
            {
                FMeshBone bone = skeletalMesh.RefSkeleton[i];
                Matrix4x4 rawLocal = bone.BonePos.ToMatrix();
                Matrix4x4 rawGlobal = bone.ParentIndex >= 0 && bone.ParentIndex < globals.Count
                    ? rawLocal * globals[bone.ParentIndex]
                    : rawLocal;
                globals.Add(rawGlobal);

                skeleton.Bones.Add(new RetargetBone
                {
                    Name = bone.Name?.Name ?? $"Bone_{i}",
                    ParentIndex = bone.ParentIndex,
                    LocalTransform = ConvertRetargetTransform(rawLocal),
                    GlobalTransform = ConvertRetargetTransform(rawGlobal)
                });
            }

            skeleton.RebuildBoneLookup();
            return skeleton;
        }

        private static Matrix4x4 ConvertRetargetTransform(Matrix4x4 value)
        {
            return new Matrix4x4(
                value.M11, value.M13, value.M12, value.M14,
                value.M31, value.M33, value.M32, value.M34,
                value.M21, value.M23, value.M22, value.M24,
                value.M41, value.M43, value.M42, value.M44);
        }

        private UAnimSet DiscoverAnimSetForSkeletalMesh(string meshExportPath, USkeletalMesh skeletalMesh)
        {
            if (retargeterHeader?.ExportTable == null)
                return null;

            string meshObjectName = meshExportPath?.Split('.').LastOrDefault();

            foreach (UnrealExportTableEntry export in retargeterHeader.ExportTable)
            {
                string className = TryGetExportClassName(export);
                if (!string.Equals(className, nameof(USkeletalMeshComponent), StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(className, "SkeletalMeshComponent", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (export.UnrealObject == null)
                        export.ParseUnrealObject(false, false).GetAwaiter().GetResult();

                    if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMeshComponent component)
                        continue;

                    USkeletalMesh referencedMesh = component.SkeletalMesh?.LoadObject<USkeletalMesh>();
                    if (!ReferenceEquals(referencedMesh, skeletalMesh))
                        continue;

                    foreach (FObject animSetRef in component.AnimSets ?? [])
                    {
                        UAnimSet animSet = animSetRef?.LoadObject<UAnimSet>();
                        if (animSet != null)
                            return animSet;
                    }
                }
                catch
                {
                    continue;
                }
            }

            foreach (UnrealExportTableEntry export in retargeterHeader.ExportTable)
            {
                string className = TryGetExportClassName(export);
                if (!string.Equals(className, nameof(UAnimSet), StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(className, "AnimSet", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (export.UnrealObject == null)
                        export.ParseUnrealObject(false, false).GetAwaiter().GetResult();

                    if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UAnimSet animSet)
                        continue;

                    string previewName = animSet.PreviewSkelMeshName?.Name;
                    if (!string.IsNullOrWhiteSpace(previewName) &&
                        string.Equals(previewName, meshObjectName, StringComparison.OrdinalIgnoreCase))
                    {
                        return animSet;
                    }

                    if (animSet.TrackBoneNames != null && animSet.TrackBoneNames.Count > 0)
                    {
                        HashSet<string> trackNames = animSet.TrackBoneNames
                            .Where(static name => !string.IsNullOrWhiteSpace(name?.Name))
                            .Select(static name => name.Name)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        int overlap = skeletalMesh.RefSkeleton.Count(bone => trackNames.Contains(bone.Name?.Name));
                        if (overlap >= Math.Max(4, skeletalMesh.RefSkeleton.Count / 4))
                            return animSet;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        private static string TryGetExportClassName(UnrealExportTableEntry export)
        {
            try
            {
                return export?.ClassReferenceNameIndex?.Name;
            }
            catch
            {
                return null;
            }
        }

        private void ImportRetargetTextures()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "Texture Files|*.png;*.jpg;*.jpeg;*.dds;*.tga",
                Title = "Import Textures",
                Multiselect = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            retargetTexturePaths.Clear();
            retargetTexturePaths.AddRange(dialog.FileNames);
            skeletalMeshRetargeterPanel.TexturesDisplay = retargetTexturePaths.Count == 0
                ? string.Empty
                : $"{retargetTexturePaths.Count} texture(s) loaded";
            skeletalMeshRetargeterPanel.AppendLog($"Imported {retargetTexturePaths.Count} texture file(s).");

            if (retargetSourceMesh != null)
            {
                retargetSourceMesh.Textures.Clear();
                foreach (string path in retargetTexturePaths)
                    retargetSourceMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
            }

            if (retargetProcessedMesh != null)
            {
                retargetProcessedMesh.Textures.Clear();
                foreach (string path in retargetTexturePaths)
                    retargetProcessedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));
            }
        }

        private void AutoMapRetargetBones()
        {
            if (retargetSourceMesh == null || retargetPlayerSkeleton == null)
            {
                WarningBox("Import a mesh and a player skeleton first.");
                return;
            }

            if (retargetSourceMesh.Bones.Count == 0)
            {
                skeletalMeshRetargeterPanel.AppendLog("Auto bone mapping skipped because the imported source mesh has 0 bones.");
                WarningBox("The imported source mesh has 0 bones. Import a skinned .psk/.fbx mesh first, then run Auto Bone Mapping again.");
                return;
            }

            try
            {
                BoneMapper mapper = new();
                retargetBoneMapping = mapper.AutoMap(retargetSourceMesh, retargetPlayerSkeleton, skeletalMeshRetargeterPanel.AppendLog);
                skeletalMeshRetargeterPanel.SetMapping(retargetBoneMapping.Mapping);
                skeletalMeshRetargeterPanel.AppendLog($"Auto bone mapping completed with {retargetBoneMapping.Mapping.Count} mapped bones.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Auto bone mapping failed: {ex.Message}");
                WarningBox($"Auto bone mapping failed.\n\n{ex}");
            }
        }

        private void ApplyRetargetWeightTransfer()
        {
            if (retargetSourceMesh == null || retargetPlayerSkeleton == null || retargetBoneMapping == null)
            {
                WarningBox("Import a mesh, import a player skeleton, and run auto bone mapping first.");
                return;
            }

            try
            {
                WeightTransfer transfer = new();
                retargetProcessedMesh = transfer.Apply(retargetSourceMesh, retargetBoneMapping.Mapping, retargetPlayerSkeleton, skeletalMeshRetargeterPanel.AppendLog);
                if (retargetAnimSet != null)
                    retargetProcessedMesh.AnimSet = retargetAnimSet;
                retargetProcessedMesh.Textures.Clear();
                foreach (string path in retargetTexturePaths)
                    retargetProcessedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Transferred weights for {retargetProcessedMesh.VertexCount} vertices against {retargetPlayerSkeleton.Bones.Count} player skeleton bones.");
                skeletalMeshRetargeterPanel.SetPosePreviewSummary("Ready for pose preview. Choose a preset, then apply it to inspect deformation.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Weight transfer failed: {ex.Message}");
                WarningBox($"Weight transfer failed.\n\n{ex}");
            }
        }

        private void ApplyRetargetOneClickAutoRig()
        {
            if (retargetReferenceMesh == null || retargetReferenceMesh.Bones.Count == 0)
            {
                WarningBox("Select an original MHO SkeletalMesh first so the source weights and original skeleton can be loaded.");
                return;
            }

            if (retargetSourceMesh == null)
            {
                WarningBox("Import the new unrigged mesh first.");
                return;
            }

            if (retargetPlayerSkeleton == null || retargetPlayerSkeleton.Bones.Count == 0)
            {
                WarningBox("The original MHO skeleton context is not loaded.");
                return;
            }

            try
            {
                BeginRetargetDiagnosticLog();
                Action<string> diagnosticLog = message =>
                {
                    skeletalMeshRetargeterPanel.AppendLog(message);
                    AppendRetargetDiagnosticLine(message);
                };

                RetargetMesh workingMesh = retargetSourceMesh;
                LogRetargetMeshSpatialSummary("One-click source start", workingMesh, diagnosticLog);
                if (workingMesh.Bones.Count > 0 && retargetBoneMapping != null && retargetBoneMapping.Mapping.Count > 0)
                {
                    OrientationProcessor orientationProcessor = new();
                    Quaternion orientationRotation = orientationProcessor.ComputeAlignmentRotation(
                        workingMesh,
                        retargetPlayerSkeleton,
                        retargetBoneMapping.Mapping,
                        diagnosticLog);
                    workingMesh = orientationProcessor.ApplyRotation(workingMesh, orientationRotation, diagnosticLog);
                    workingMesh.AppliedOrientation = Quaternion.Normalize(orientationRotation * workingMesh.AppliedOrientation);
                    LogRetargetMeshSpatialSummary("One-click after auto orientation", workingMesh, diagnosticLog);
                    Vector3 orientationEuler = ToEulerDegrees(workingMesh.AppliedOrientation);
                    skeletalMeshRetargeterPanel.SetAutoOrientationSummary(
                        $"Applied automatic orientation during one-click bind. Approx Euler: X {orientationEuler.X:0.#}, Y {orientationEuler.Y:0.#}, Z {orientationEuler.Z:0.#} degrees.");
                }
                else if (workingMesh.Bones.Count == 0)
                {
                    OrientationProcessor orientationProcessor = new();
                    Quaternion orientationRotation = orientationProcessor.ComputeGeometryAlignmentRotation(
                        workingMesh,
                        retargetReferenceMesh,
                        retargetPlayerSkeleton,
                        diagnosticLog);
                    workingMesh = orientationProcessor.ApplyRotation(workingMesh, orientationRotation, diagnosticLog);
                    workingMesh.AppliedOrientation = Quaternion.Normalize(orientationRotation * workingMesh.AppliedOrientation);
                    LogRetargetMeshSpatialSummary("One-click after unrigged auto orientation", workingMesh, diagnosticLog);
                    Vector3 orientationEuler = ToEulerDegrees(workingMesh.AppliedOrientation);
                    skeletalMeshRetargeterPanel.SetAutoOrientationSummary(
                        $"Applied automatic orientation during one-click bind for the unrigged mesh path. Approx Euler: X {orientationEuler.X:0.#}, Y {orientationEuler.Y:0.#}, Z {orientationEuler.Z:0.#} degrees.");
                }

                AutoScaleProcessor scaleProcessor = new();
                float scaleFactor = workingMesh.Bones.Count == 0
                    ? scaleProcessor.ComputeScaleFactorToReferenceMesh(
                        workingMesh,
                        retargetReferenceMesh,
                        diagnosticLog)
                    : scaleProcessor.ComputeScaleFactor(
                        workingMesh,
                        retargetPlayerSkeleton,
                        null,
                        diagnosticLog);
                workingMesh = scaleProcessor.ApplyScale(workingMesh, scaleFactor, diagnosticLog);
                LogRetargetMeshSpatialSummary("One-click after scale", workingMesh, diagnosticLog);

                if (workingMesh.Bones.Count == 0)
                {
                    ReferenceAlignmentProcessor alignmentProcessor = new();
                    workingMesh = alignmentProcessor.AlignToReferenceMesh(
                        workingMesh,
                        retargetReferenceMesh,
                        retargetPlayerSkeleton.Bones,
                        diagnosticLog);
                    LogRetargetMeshSpatialSummary("One-click after reference alignment", workingMesh, diagnosticLog);

                    PoseConformProcessor poseConformer = new();
                    workingMesh = poseConformer.ConformToReferencePose(
                        workingMesh,
                        retargetReferenceMesh,
                        retargetPlayerSkeleton.Bones,
                        diagnosticLog);
                    LogRetargetMeshSpatialSummary("One-click after pose conform", workingMesh, diagnosticLog);
                }

                WeightTransferEngine transferEngine = new();
                retargetProcessedMesh = transferEngine.TransferWeights(
                    retargetReferenceMesh,
                    workingMesh,
                    retargetPlayerSkeleton.Bones,
                    diagnosticLog);
                LogRetargetMeshSpatialSummary("One-click after weight transfer", retargetProcessedMesh, diagnosticLog);
                LogRetargetBoneComparisons(
                    retargetReferenceMesh,
                    retargetPlayerSkeleton,
                    retargetProcessedMesh,
                    diagnosticLog);

                if (retargetAnimSet != null)
                    retargetProcessedMesh.AnimSet = retargetAnimSet;
                retargetProcessedMesh.Textures.Clear();
                foreach (string path in retargetTexturePaths)
                    retargetProcessedMesh.Textures.Add(new RetargetTextureReference(path, Path.GetFileNameWithoutExtension(path)));

                skeletalMeshRetargeterPanel.SetAutoScaleSummary($"Applied automatic scale: {scaleFactor:0.###}x (total source scale {workingMesh.AppliedScale:0.###}x)");
                skeletalMeshRetargeterPanel.SetAutoRigSummary(
                    $"Bound imported mesh to original MHO skeleton using nearest-triangle barycentric weight transfer. Vertices: {retargetProcessedMesh.VertexCount}, triangles: {retargetProcessedMesh.TriangleCount}, skeleton bones: {retargetProcessedMesh.Bones.Count}.");
                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Transferred interpolated weights from original MHO mesh onto {retargetProcessedMesh.VertexCount} imported vertices and preserved the original skeleton ordering.");
                skeletalMeshRetargeterPanel.SetPosePreviewSummary("Ready for pose preview. Choose a preset, then apply it to inspect deformation.");
                skeletalMeshRetargeterPanel.AppendLog($"Retarget diagnostic log: {currentRetargetDiagnosticLogPath}");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"One-click auto-rig failed: {ex.Message}");
                WarningBox($"One-click auto-rig failed.\n\n{ex}");
            }
        }

        private void ApplyRetargetPosePreview()
        {
            RetargetMesh previewSource = GetRetargetPosePreviewSourceMesh();
            if (previewSource == null)
            {
                WarningBox("Run Apply Weight Transfer or One-Click Bind To Skeleton first so there is a weighted mesh to pose preview.");
                return;
            }

            try
            {
                RetargetPosePreset preset = skeletalMeshRetargeterPanel.SelectedPosePreset;
                RetargetMesh posedMesh = retargetPosePreviewService.ApplyPose(previewSource, preset, skeletalMeshRetargeterPanel.AppendLog);
                string previewName = $"{(string.IsNullOrWhiteSpace(posedMesh.MeshName) ? "Retarget Mesh" : posedMesh.MeshName)} - {preset}";
                MeshPreviewMesh previewMesh = retargetToPreviewMeshConverter.Convert(posedMesh, previewName, skeletalMeshRetargeterPanel.AppendLog);
                skeletalMeshRetargeterPanel.SetPosePreviewMesh(previewMesh);
                skeletalMeshRetargeterPanel.SetPosePreviewSummary($"Showing {preset} on {ResolveRetargetPoseSourceName(previewSource)}. Use Reset Pose Preview to return to bind pose.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Pose preview failed: {ex.Message}");
                WarningBox($"Pose preview failed.\n\n{ex}");
            }
        }

        private void ResetRetargetPosePreview()
        {
            RetargetMesh previewSource = GetRetargetPosePreviewSourceMesh();
            if (previewSource == null)
            {
                skeletalMeshRetargeterPanel.ClearPosePreview();
                skeletalMeshRetargeterPanel.SetPosePreviewSummary("Pose preview is ready after weight transfer or one-click bind.");
                return;
            }

            try
            {
                string previewName = $"{(string.IsNullOrWhiteSpace(previewSource.MeshName) ? "Retarget Mesh" : previewSource.MeshName)} - BindPose";
                MeshPreviewMesh previewMesh = retargetToPreviewMeshConverter.Convert(previewSource, previewName, skeletalMeshRetargeterPanel.AppendLog);
                skeletalMeshRetargeterPanel.SetPosePreviewMesh(previewMesh);
                skeletalMeshRetargeterPanel.SetPosePreviewSummary($"Showing bind pose on {ResolveRetargetPoseSourceName(previewSource)}.");
                skeletalMeshRetargeterPanel.AppendLog("Pose preview reset to bind pose.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Pose preview reset failed: {ex.Message}");
                WarningBox($"Pose preview reset failed.\n\n{ex}");
            }
        }

        private RetargetMesh GetRetargetPosePreviewSourceMesh()
        {
            if (HasRetargetPosePreviewData(retargetProcessedMesh))
                return retargetProcessedMesh;

            if (HasRetargetPosePreviewData(retargetSourceMesh))
                return retargetSourceMesh;

            if (HasRetargetPosePreviewData(retargetReferenceMesh))
                return retargetReferenceMesh;

            return null;
        }

        private static bool HasRetargetPosePreviewData(RetargetMesh mesh)
        {
            return mesh != null &&
                   mesh.Bones.Count > 0 &&
                   mesh.Sections.Any(static section => section.Vertices.Any(static vertex => vertex.Weights.Count > 0));
        }

        private static string ResolveRetargetPoseSourceName(RetargetMesh mesh)
        {
            if (mesh == null)
                return "retarget mesh";

            if (!string.IsNullOrWhiteSpace(mesh.MeshName))
                return mesh.MeshName;

            return Path.GetFileNameWithoutExtension(mesh.SourcePath);
        }

        private static void LogRetargetBoneComparisons(
            RetargetMesh referenceMesh,
            SkeletonDefinition playerSkeleton,
            RetargetMesh processedMesh,
            Action<string> log)
        {
            if (referenceMesh == null || playerSkeleton == null || processedMesh == null || log == null)
                return;

            referenceMesh.RebuildBoneLookup();
            playerSkeleton.RebuildBoneLookup();
            processedMesh.RebuildBoneLookup();

            string[] boneNames =
            [
                "g_head",
                "g_pelvis",
                "g_l_wrist",
                "g_r_wrist",
                "g_l_ankle",
                "g_r_ankle"
            ];

            foreach (string boneName in boneNames)
            {
                bool hasReference = referenceMesh.BonesByName.TryGetValue(boneName, out RetargetBone referenceBone);
                bool hasPlayer = playerSkeleton.BonesByName.TryGetValue(boneName, out RetargetBone playerBone);
                bool hasProcessed = processedMesh.BonesByName.TryGetValue(boneName, out RetargetBone processedBone);

                if (!hasReference || !hasPlayer || !hasProcessed)
                {
                    log($"Bone compare {boneName}: missing reference={hasReference}, player={hasPlayer}, processed={hasProcessed}.");
                    continue;
                }

                Vector3 referencePosition = referenceBone.GlobalTransform.Translation;
                Vector3 playerPosition = playerBone.GlobalTransform.Translation;
                Vector3 processedPosition = processedBone.GlobalTransform.Translation;

                log(
                    $"Bone compare {boneName}: " +
                    $"reference=({referencePosition.X:0.##},{referencePosition.Y:0.##},{referencePosition.Z:0.##}), " +
                    $"player=({playerPosition.X:0.##},{playerPosition.Y:0.##},{playerPosition.Z:0.##}), " +
                    $"processed=({processedPosition.X:0.##},{processedPosition.Y:0.##},{processedPosition.Z:0.##}), " +
                    $"ref-player={Vector3.Distance(referencePosition, playerPosition):0.###}, " +
                    $"ref-processed={Vector3.Distance(referencePosition, processedPosition):0.###}.");
            }
        }

        private static void VerifyRetargetExportRoundTrip(string filePath, RetargetMesh expectedMesh, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(filePath) || expectedMesh == null || log == null)
                return;

            Retargeting.MeshImporter importer = new();
            RetargetMesh importedMesh = importer.Import(filePath, log);
            log(
                $"Export round-trip summary: expected sections={expectedMesh.Sections.Count}, imported sections={importedMesh.Sections.Count}, " +
                $"expected vertices={expectedMesh.VertexCount}, imported vertices={importedMesh.VertexCount}, " +
                $"expected triangles={expectedMesh.TriangleCount}, imported triangles={importedMesh.TriangleCount}, " +
                $"expected bones={expectedMesh.Bones.Count}, imported bones={importedMesh.Bones.Count}.");

            expectedMesh.RebuildBoneLookup();
            importedMesh.RebuildBoneLookup();

            string[] boneNames =
            [
                "g_head",
                "g_pelvis",
                "g_l_wrist",
                "g_r_wrist",
                "g_l_ankle",
                "g_r_ankle"
            ];

            foreach (string boneName in boneNames)
            {
                bool hasExpected = expectedMesh.BonesByName.TryGetValue(boneName, out RetargetBone expectedBone);
                bool hasImported = importedMesh.BonesByName.TryGetValue(boneName, out RetargetBone importedBone);

                if (!hasExpected || !hasImported)
                {
                    log($"Export round-trip bone {boneName}: missing expected={hasExpected}, imported={hasImported}.");
                    continue;
                }

                Vector3 expectedPosition = expectedBone.GlobalTransform.Translation;
                Vector3 importedPosition = importedBone.GlobalTransform.Translation;
                log(
                    $"Export round-trip bone {boneName}: " +
                    $"expected=({expectedPosition.X:0.##},{expectedPosition.Y:0.##},{expectedPosition.Z:0.##}), " +
                    $"imported=({importedPosition.X:0.##},{importedPosition.Y:0.##},{importedPosition.Z:0.##}), " +
                    $"delta={Vector3.Distance(expectedPosition, importedPosition):0.###}.");
            }
        }

        private async Task VerifyRetargetReplacementRoundTripAsync(
            string upkPath,
            string skeletalMeshExportPath,
            int lodIndex,
            RetargetMesh expectedMesh)
        {
            if (string.IsNullOrWhiteSpace(upkPath) || string.IsNullOrWhiteSpace(skeletalMeshExportPath) || expectedMesh == null)
                return;

            UnrealHeader header = await repository.LoadUpkFile(upkPath).ConfigureAwait(true);
            await header.ReadHeaderAsync(null).ConfigureAwait(true);

            UnrealExportTableEntry export = header.ExportTable
                .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase));
            if (export == null)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Replacement round-trip verification failed: could not find export '{skeletalMeshExportPath}'.");
                return;
            }

            if (export.UnrealObject == null)
                await export.ParseUnrealObject(false, false).ConfigureAwait(true);

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            {
                skeletalMeshRetargeterPanel.AppendLog("Replacement round-trip verification failed: reloaded export was not a SkeletalMesh.");
                return;
            }

            RetargetMesh reloadedMesh = new MhoSkeletalMeshConverter().Convert(
                skeletalMesh,
                skeletalMeshExportPath,
                lodIndex,
                skeletalMeshRetargeterPanel.AppendLog);

            skeletalMeshRetargeterPanel.AppendLog(
                $"Replacement round-trip summary: expected sections={expectedMesh.Sections.Count}, reloaded sections={reloadedMesh.Sections.Count}, " +
                $"expected vertices={expectedMesh.VertexCount}, reloaded vertices={reloadedMesh.VertexCount}, " +
                $"expected triangles={expectedMesh.TriangleCount}, reloaded triangles={reloadedMesh.TriangleCount}, " +
                $"expected bones={expectedMesh.Bones.Count}, reloaded bones={reloadedMesh.Bones.Count}.");

            LogRetargetRegionVertexComparisons("Replacement round-trip", expectedMesh, reloadedMesh, skeletalMeshRetargeterPanel.AppendLog);
        }

        private static void LogRetargetRegionVertexComparisons(string label, RetargetMesh expectedMesh, RetargetMesh reloadedMesh, Action<string> log)
        {
            if (expectedMesh == null || reloadedMesh == null || log == null)
                return;

            RetargetRegion[] regions =
            [
                RetargetRegion.Head,
                RetargetRegion.Chest,
                RetargetRegion.Pelvis,
                RetargetRegion.LeftHand,
                RetargetRegion.RightHand,
                RetargetRegion.LeftFoot,
                RetargetRegion.RightFoot
            ];

            List<RegionAnchor> expectedAnchors = RetargetRegions.BuildAnchors(expectedMesh.Bones);
            foreach (RetargetRegion region in regions)
            {
                Vector3 anchor = RetargetRegions.GetAnchorCenter(RetargetRegions.GetRegionBoneNames(region), expectedAnchors);
                if (!float.IsFinite(anchor.X))
                {
                    log($"{label} region {region}: anchor unavailable.");
                    continue;
                }

                RetargetVertex expectedVertex = FindNearestVertex(expectedMesh, anchor);
                RetargetVertex reloadedVertex = FindNearestVertex(reloadedMesh, anchor);

                log(
                    $"{label} region {region}: " +
                    $"expectedPos=({expectedVertex.Position.X:0.##},{expectedVertex.Position.Y:0.##},{expectedVertex.Position.Z:0.##}), " +
                    $"reloadedPos=({reloadedVertex.Position.X:0.##},{reloadedVertex.Position.Y:0.##},{reloadedVertex.Position.Z:0.##}), " +
                    $"delta={Vector3.Distance(expectedVertex.Position, reloadedVertex.Position):0.###}, " +
                    $"expectedWeights=[{FormatWeights(expectedVertex.Weights)}], " +
                    $"reloadedWeights=[{FormatWeights(reloadedVertex.Weights)}].");
            }
        }

        private static RetargetVertex FindNearestVertex(RetargetMesh mesh, Vector3 target)
        {
            RetargetVertex best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (RetargetSection section in mesh.Sections)
            {
                foreach (RetargetVertex vertex in section.Vertices)
                {
                    float distance = Vector3.DistanceSquared(vertex.Position, target);
                    if (distance >= bestDistance)
                        continue;

                    bestDistance = distance;
                    best = vertex;
                }
            }

            return best ?? new RetargetVertex();
        }

        private static string FormatWeights(IReadOnlyList<RetargetWeight> weights)
        {
            if (weights == null || weights.Count == 0)
                return "<none>";

            return string.Join(", ", weights.Select(weight => $"{weight.BoneName}:{weight.Weight:0.##}"));
        }

        private void BeginRetargetDiagnosticLog()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "MHUpkManager_ImportLogs");
            Directory.CreateDirectory(directory);
            currentRetargetDiagnosticLogPath = Path.Combine(directory, $"retarget-oneclick-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(currentRetargetDiagnosticLogPath, "One-Click Retarget Diagnostics" + Environment.NewLine);
        }

        private void AppendRetargetDiagnosticLine(string message)
        {
            if (string.IsNullOrWhiteSpace(currentRetargetDiagnosticLogPath) || string.IsNullOrWhiteSpace(message))
                return;

            File.AppendAllText(currentRetargetDiagnosticLogPath, message + Environment.NewLine);
        }

        private static void LogRetargetMeshSpatialSummary(string label, RetargetMesh mesh, Action<string> log)
        {
            if (mesh == null || log == null || mesh.Sections.Count == 0)
                return;

            bool hasVertex = false;
            Vector3 min = new(float.PositiveInfinity);
            Vector3 max = new(float.NegativeInfinity);

            foreach (RetargetSection section in mesh.Sections)
            {
                foreach (RetargetVertex vertex in section.Vertices)
                {
                    hasVertex = true;
                    min = Vector3.Min(min, vertex.Position);
                    max = Vector3.Max(max, vertex.Position);
                }
            }

            if (!hasVertex)
                return;

            Vector3 size = max - min;
            string dominantAxis = size.X >= size.Y && size.X >= size.Z
                ? "X"
                : size.Y >= size.Z
                    ? "Y"
                    : "Z";

            log(
                $"{label}: min=({min.X:0.##},{min.Y:0.##},{min.Z:0.##}), " +
                $"max=({max.X:0.##},{max.Y:0.##},{max.Z:0.##}), " +
                $"size=({size.X:0.##},{size.Y:0.##},{size.Z:0.##}), dominantAxis={dominantAxis}.");
        }

        private void ApplyRetargetAutoScale()
        {
            if (retargetSourceMesh == null)
                {
                WarningBox("Import a mesh first.");
                return;
            }

            if (retargetPlayerSkeleton == null)
            {
                WarningBox("Import or auto-load the destination skeleton first.");
                return;
            }

            try
            {
                AutoScaleProcessor processor = new();
                float scaleFactor = processor.ComputeScaleFactor(
                    retargetSourceMesh,
                    retargetPlayerSkeleton,
                    retargetBoneMapping?.Mapping,
                    skeletalMeshRetargeterPanel.AppendLog);

                retargetSourceMesh = processor.ApplyScale(retargetSourceMesh, scaleFactor, skeletalMeshRetargeterPanel.AppendLog);
                retargetProcessedMesh = null;
                skeletalMeshRetargeterPanel.SetAutoScaleSummary($"Applied automatic scale: {scaleFactor:0.###}x (total source scale {retargetSourceMesh.AppliedScale:0.###}x)");
                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Scaled source mesh to target character proportions. Vertices: {retargetSourceMesh.VertexCount}, triangles: {retargetSourceMesh.TriangleCount}, source bones: {retargetSourceMesh.Bones.Count}.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Automatic scale matching failed: {ex.Message}");
                WarningBox($"Automatic scale matching failed.\n\n{ex}");
            }
        }

        private void RotateRetargetSourceMesh(float degrees)
        {
            RotateRetargetSourceMeshAroundAxis("up", degrees);
        }

        private void RotateRetargetSourceMeshAroundAxis(string axisName, float degrees)
        {
            if (retargetSourceMesh == null)
            {
                WarningBox("Import a mesh first.");
                return;
            }

            if (retargetPlayerSkeleton == null)
            {
                WarningBox("Import or auto-load the destination skeleton first.");
                return;
            }

            try
            {
                OrientationProcessor processor = new();
                Vector3 axis = processor.GetTargetAxis(retargetPlayerSkeleton, axisName);
                Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI * degrees / 180.0f);
                retargetSourceMesh = processor.ApplyRotation(retargetSourceMesh, rotation, skeletalMeshRetargeterPanel.AppendLog);
                retargetSourceMesh.AppliedOrientation = Quaternion.Normalize(rotation * retargetSourceMesh.AppliedOrientation);
                retargetProcessedMesh = null;

                Vector3 euler = ToEulerDegrees(retargetSourceMesh.AppliedOrientation);
                skeletalMeshRetargeterPanel.SetAutoOrientationSummary(
                    $"Applied manual orientation offset. Approx Euler: X {euler.X:0.#}, Y {euler.Y:0.#}, Z {euler.Z:0.#} degrees.");
                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Applied a manual {degrees:0.#} degree rotation around the target skeleton {axisName} axis. Re-run one-click bind before export or UPK replacement.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Manual orientation adjustment failed: {ex.Message}");
                WarningBox($"Manual orientation adjustment failed.\n\n{ex}");
            }
        }

        private void ApplyRetargetAutoOrientation()
        {
            if (retargetSourceMesh == null)
            {
                WarningBox("Import a mesh first.");
                return;
            }

            if (retargetPlayerSkeleton == null)
            {
                WarningBox("Import or auto-load the destination skeleton first.");
                return;
            }

            if (retargetBoneMapping == null || retargetBoneMapping.Mapping.Count == 0)
            {
                WarningBox("Run auto bone mapping first so orientation can be aligned against the target skeleton.");
                return;
            }

            try
            {
                OrientationProcessor processor = new();
                Quaternion rotation = processor.ComputeAlignmentRotation(
                    retargetSourceMesh,
                    retargetPlayerSkeleton,
                    retargetBoneMapping.Mapping,
                    skeletalMeshRetargeterPanel.AppendLog);

                retargetSourceMesh = processor.ApplyRotation(retargetSourceMesh, rotation, skeletalMeshRetargeterPanel.AppendLog);
                retargetSourceMesh.AppliedOrientation = Quaternion.Normalize(rotation * retargetSourceMesh.AppliedOrientation);
                retargetProcessedMesh = null;

                Vector3 euler = ToEulerDegrees(retargetSourceMesh.AppliedOrientation);
                skeletalMeshRetargeterPanel.SetAutoOrientationSummary(
                    $"Applied automatic orientation. Approx Euler: X {euler.X:0.#}, Y {euler.Y:0.#}, Z {euler.Z:0.#} degrees.");
                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"Reoriented imported source mesh to match the target skeleton basis. Vertices: {retargetSourceMesh.VertexCount}, triangles: {retargetSourceMesh.TriangleCount}, source bones: {retargetSourceMesh.Bones.Count}.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"Automatic orientation matching failed: {ex.Message}");
                WarningBox($"Automatic orientation matching failed.\n\n{ex}");
            }
        }

        private void ApplyRetargetCompatibilityFixes()
        {
            if (retargetProcessedMesh == null && retargetSourceMesh == null)
            {
                WarningBox("Import a mesh first.");
                return;
            }

            if (retargetPlayerSkeleton == null)
            {
                WarningBox("Import a player skeleton first.");
                return;
            }

            try
            {
                UE3CompatibilityProcessor processor = new();
                RetargetMesh input = retargetProcessedMesh ?? retargetSourceMesh;
                LogRetargetMeshSpatialSummary("UE3 compatibility input", input, skeletalMeshRetargeterPanel.AppendLog);
                retargetProcessedMesh = processor.Process(
                    input,
                    retargetPlayerSkeleton,
                    retargetBoneMapping?.Mapping,
                    skeletalMeshRetargeterPanel.AppendLog);
                LogRetargetMeshSpatialSummary("UE3 compatibility output", retargetProcessedMesh, skeletalMeshRetargeterPanel.AppendLog);

                if (retargetAnimSet != null)
                    retargetProcessedMesh.AnimSet = retargetAnimSet;

                skeletalMeshRetargeterPanel.SetWeightTransferSummary(
                    $"UE3 compatibility fixes applied. LOD0 sections: {retargetProcessedMesh.Sections.Count}, UV sets: {retargetProcessedMesh.MaxUvSets}, bones: {retargetProcessedMesh.Bones.Count}.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"UE3 compatibility processing failed: {ex.Message}");
                WarningBox($"UE3 compatibility processing failed.\n\n{ex}");
            }
        }

        private async Task ExportRetargetFbxAsync()
        {
            if (retargetProcessedMesh == null)
            {
                WarningBox("Run the retargeting and compatibility steps first.");
                return;
            }

            using SaveFileDialog dialog = new()
            {
                Filter = "FBX Files (*.fbx)|*.fbx",
                Title = "Export FBX 2013",
                FileName = Path.GetFileNameWithoutExtension(retargetProcessedMesh.MeshName) + "_retargeted.fbx"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            skeletalMeshRetargeterPanel.SetBusy(true);
            try
            {
                skeletalMeshRetargeterPanel.ReportProgress(20, 100, "Exporting FBX 2013.");
                FBX2013Exporter exporter = new();
                await Task.Run(() => exporter.Export(dialog.FileName, retargetProcessedMesh, skeletalMeshRetargeterPanel.AppendLog)).ConfigureAwait(true);
                skeletalMeshRetargeterPanel.ReportProgress(70, 100, "Verifying exported FBX round-trip.");
                await Task.Run(() => VerifyRetargetExportRoundTrip(dialog.FileName, retargetProcessedMesh, skeletalMeshRetargeterPanel.AppendLog)).ConfigureAwait(true);
                skeletalMeshRetargeterPanel.ReportProgress(100, 100, "FBX 2013 export completed.");
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"FBX 2013 export failed: {ex.Message}");
                WarningBox($"FBX 2013 export failed.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private async Task ReplaceRetargetMeshAsync()
        {
            if (retargetProcessedMesh == null)
            {
                WarningBox("Run the retargeting and compatibility steps first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(skeletalMeshRetargeterPanel.UpkPath) || string.IsNullOrWhiteSpace(skeletalMeshRetargeterPanel.SelectedMeshName))
            {
                WarningBox("Select a UPK file and SkeletalMesh export first.");
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"This will replace the selected SkeletalMesh inside:\n{skeletalMeshRetargeterPanel.UpkPath}\n\nA backup will be written next to it as:\n{skeletalMeshRetargeterPanel.UpkPath}.bak\n\nContinue?",
                "Replace SkeletalMesh In UPK",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.OK)
                return;

            skeletalMeshRetargeterPanel.SetBusy(true);
            try
            {
                skeletalMeshRetargeterPanel.ReportProgress(10, 100, "Verifying rebuilt UE3 LOD before UPK replacement.");
                MeshReplacer replacer = new();
                RetargetMesh rebuiltMesh = await replacer.BuildReplacementRoundTripMeshAsync(
                    skeletalMeshRetargeterPanel.UpkPath,
                    skeletalMeshRetargeterPanel.SelectedMeshName,
                    retargetProcessedMesh,
                    skeletalMeshRetargeterPanel.SelectedLodIndex,
                    skeletalMeshRetargeterPanel.AppendLog).ConfigureAwait(true);
                skeletalMeshRetargeterPanel.AppendLog(
                    $"Replacement build round-trip summary: expected sections={retargetProcessedMesh.Sections.Count}, rebuilt sections={rebuiltMesh.Sections.Count}, " +
                    $"expected vertices={retargetProcessedMesh.VertexCount}, rebuilt vertices={rebuiltMesh.VertexCount}, " +
                    $"expected triangles={retargetProcessedMesh.TriangleCount}, rebuilt triangles={rebuiltMesh.TriangleCount}, " +
                    $"expected bones={retargetProcessedMesh.Bones.Count}, rebuilt bones={rebuiltMesh.Bones.Count}.");
                LogRetargetRegionVertexComparisons("Replacement build round-trip", retargetProcessedMesh, rebuiltMesh, skeletalMeshRetargeterPanel.AppendLog);

                skeletalMeshRetargeterPanel.ReportProgress(25, 100, "Replacing SkeletalMesh in UPK.");
                string backupPath = await replacer.ReplaceMeshInUpkAsync(
                    skeletalMeshRetargeterPanel.UpkPath,
                    skeletalMeshRetargeterPanel.SelectedMeshName,
                    retargetProcessedMesh,
                    skeletalMeshRetargeterPanel.SelectedLodIndex,
                    replaceAllLods: false,
                    skeletalMeshRetargeterPanel.AppendLog).ConfigureAwait(true);
                skeletalMeshRetargeterPanel.ReportProgress(80, 100, "Verifying replaced SkeletalMesh round-trip.");
                await VerifyRetargetReplacementRoundTripAsync(
                    skeletalMeshRetargeterPanel.UpkPath,
                    skeletalMeshRetargeterPanel.SelectedMeshName,
                    skeletalMeshRetargeterPanel.SelectedLodIndex,
                    retargetProcessedMesh).ConfigureAwait(true);

                skeletalMeshRetargeterPanel.ReportProgress(100, 100, "UPK replacement completed.");
                MessageBox.Show(
                    $"Replaced '{skeletalMeshRetargeterPanel.SelectedMeshName}' in:\n{skeletalMeshRetargeterPanel.UpkPath}\n\nBackup created:\n{backupPath}",
                    "SkeletalMesh Retargeter",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                skeletalMeshRetargeterPanel.AppendLog($"UPK replacement failed: {ex.Message}");
                WarningBox($"UPK replacement failed.\n\n{ex}");
            }
            finally
            {
                skeletalMeshRetargeterPanel.SetBusy(false);
            }
        }

        private static Vector3 ToEulerDegrees(Quaternion rotation)
        {
            rotation = Quaternion.Normalize(rotation);

            float sinrCosp = 2.0f * ((rotation.W * rotation.X) + (rotation.Y * rotation.Z));
            float cosrCosp = 1.0f - (2.0f * ((rotation.X * rotation.X) + (rotation.Y * rotation.Y)));
            float roll = MathF.Atan2(sinrCosp, cosrCosp);

            float sinp = 2.0f * ((rotation.W * rotation.Y) - (rotation.Z * rotation.X));
            float pitch = MathF.Abs(sinp) >= 1.0f
                ? MathF.CopySign(MathF.PI / 2.0f, sinp)
                : MathF.Asin(sinp);

            float sinyCosp = 2.0f * ((rotation.W * rotation.Z) + (rotation.X * rotation.Y));
            float cosyCosp = 1.0f - (2.0f * ((rotation.Y * rotation.Y) + (rotation.Z * rotation.Z)));
            float yaw = MathF.Atan2(sinyCosp, cosyCosp);

            const float RadToDeg = 180.0f / MathF.PI;
            return new Vector3(roll * RadToDeg, pitch * RadToDeg, yaw * RadToDeg);
        }

        private async Task ImportMeshFromPanelAsync()
        {
            if (string.IsNullOrWhiteSpace(meshImporterPanel.UpkPath) ||
                string.IsNullOrWhiteSpace(meshImporterPanel.FbxPath) ||
                string.IsNullOrWhiteSpace(meshImporterPanel.SelectedMeshName))
            {
                WarningBox("Select a UPK file, SkeletalMesh export, and FBX file first.");
                return;
            }

            DialogResult confirm = MessageBox.Show(
                $"This will replace the selected UPK:\n{meshImporterPanel.UpkPath}\n\nA backup will be written next to it as:\n{meshImporterPanel.UpkPath}.bak\n\nContinue?",
                "Replace Current UPK",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.OK)
                return;

            meshImporterPanel.SetBusy(true);
            meshImporterPanel.ClearLog();

            try
            {
                meshImporterPanel.AppendLog($"Missing bones / dropped influences / section rebuild / LOD replacement / UPK injection logs will stream here at step boundaries.");
                Progress<MeshImportProgress> progress = new(progressInfo =>
                {
                    meshImporterPanel.ReportProgress(progressInfo.Value, progressInfo.Maximum, progressInfo.Message);
                    progressStatus.Text = progressInfo.Message;
                });

                string backupPath = await MeshPreProcessor.ProcessAndReplaceMesh(
                    meshImporterPanel.UpkPath,
                    meshImporterPanel.SelectedMeshName,
                    meshImporterPanel.FbxPath,
                    meshImporterPanel.SelectedLodIndex,
                    meshImporterPanel.ReplaceAllLods,
                    progress,
                    meshImporterPanel.AppendLog).ConfigureAwait(true);

                string summaryPath = TryGetLatestImportLog("fbx-import-summary-*.log");
                if (!string.IsNullOrWhiteSpace(summaryPath) && File.Exists(summaryPath))
                {
                    meshImporterPanel.AppendLog($"Import summary: {summaryPath}");
                    foreach (string line in File.ReadLines(summaryPath))
                        meshImporterPanel.AppendLog(line);
                }

                MessageBox.Show(
                    $"Mesh import completed.\n\nReplaced:\n{meshImporterPanel.UpkPath}\n\nBackup created:\n{backupPath}",
                    "Mesh Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                string exceptionLog = MeshImportDiagnostics.WriteException(ex);
                meshImporterPanel.AppendLog($"Import failed: {ex.Message}");
                meshImporterPanel.AppendLog($"Exception log: {exceptionLog}");
                MessageBox.Show(
                    $"Mesh import failed.\n\n{ex}\n\nLog written to:\n{exceptionLog}",
                    "Mesh Importer Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                meshImporterPanel.SetBusy(false);
                progressStatus.Text = string.Empty;
            }
        }

        private static string TryGetLatestImportLog(string pattern)
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "MHUpkManager_ImportLogs");

            if (!Directory.Exists(directory))
                return null;

            return new DirectoryInfo(directory)
                .GetFiles(pattern)
                .OrderByDescending(static file => file.LastWriteTimeUtc)
                .Select(static file => file.FullName)
                .FirstOrDefault();
        }

        private static string SanitizeExportFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "skeletal_mesh";

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidChar, '_');

            return value;
        }

        private void WarningBox(string msg)
        {
            MessageBox.Show(msg, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void EnableDoubleBuffering(DataGridView dgv)
        {
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null, dgv, new object[] { true });
        }

        private async void openMenuItem_Click(object sender, EventArgs e)
        {
            using OpenFileDialog openFileDialog = CreateUpkOpenDialog("Open Upk file");

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                await OpenUpkFileFromPath(openFileDialog.FileName, 0);
            }
        }

        private async Task OpenUpkFileFromPath(string filePath, int index)
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                UpkFile = await OpenUpkFile(filePath);
                RememberRecentUpk(filePath);
                Text = $"{AppName} - [{UpkFile.GameFilename}]";
                await LoadUpkFile(UpkFile);

                SelectExportObject(index);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private OpenFileDialog CreateUpkOpenDialog(string title, string filter = "Unreal Package Files (*.upk)|*.upk")
        {
            OpenFileDialog dialog = new()
            {
                Filter = filter,
                Title = title
            };

            string initialDirectory = GetPreferredUpkBrowseFolder();
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            return dialog;
        }

        private string GetPreferredUpkBrowseFolder()
        {
            string currentUpkPath = GetCurrentUpkPath();
            string currentFolder = Path.GetDirectoryName(currentUpkPath);
            if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
                return currentFolder;

            if (!string.IsNullOrWhiteSpace(upkBrowsePreferences.DefaultUpkFolder) && Directory.Exists(upkBrowsePreferences.DefaultUpkFolder))
                return upkBrowsePreferences.DefaultUpkFolder;

            string recentFolder = upkBrowsePreferences.RecentUpkPaths
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder));

            return recentFolder;
        }

        private static List<string> GetUiPackageCandidates(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return [];

            static bool IsUiPackageName(string fileName)
            {
                string name = fileName ?? string.Empty;
                return name.Contains("ui", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("hud", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("frontend", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("icon", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("roster", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("store", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("civilwarpanel", StringComparison.OrdinalIgnoreCase);
            }

            static int GetUiPackagePriority(string fileName)
            {
                return fileName switch
                {
                    var name when name.Contains("ICO__MarvelUIIcons", StringComparison.OrdinalIgnoreCase) => 0,
                    var name when name.Contains("MarvelHUD", StringComparison.OrdinalIgnoreCase) => 1,
                    var name when name.Contains("MarvelFrontEnd", StringComparison.OrdinalIgnoreCase) => 2,
                    var name when name.Contains("Calligraphy", StringComparison.OrdinalIgnoreCase) => 3,
                    _ => 10
                };
            }

            return Directory.GetFiles(folderPath, "*.upk", SearchOption.TopDirectoryOnly)
                .Where(path => IsUiPackageName(Path.GetFileName(path)))
                .OrderBy(path => GetUiPackagePriority(Path.GetFileName(path)))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void RememberRecentUpk(string filePath)
        {
            upkBrowsePreferencesStore.RegisterOpenedUpk(upkBrowsePreferences, filePath);
            upkBrowsePreferencesStore.Save(upkBrowsePreferences);
            RefreshRecentUpksMenu();
        }

        private void RefreshRecentUpksMenu()
        {
            recentUpksMenuItem.DropDownItems.Clear();

            if (upkBrowsePreferences.RecentUpkPaths.Count == 0)
            {
                ToolStripMenuItem emptyItem = new("No recent UPKs")
                {
                    Enabled = false
                };
                recentUpksMenuItem.DropDownItems.Add(emptyItem);
            }
            else
            {
                foreach (string recentPath in upkBrowsePreferences.RecentUpkPaths.Where(File.Exists))
                {
                    string menuText = $"{Path.GetFileName(recentPath)}  |  {recentPath}";
                    ToolStripMenuItem item = new(menuText)
                    {
                        Tag = recentPath
                    };
                    item.Click += recentUpkMenuItem_Click;
                    recentUpksMenuItem.DropDownItems.Add(item);
                }
            }

            recentUpksMenuItem.DropDownItems.Add(new ToolStripSeparator());
            recentUpksMenuItem.DropDownItems.Add(clearRecentUpksMenuItem);
        }

        private async void recentUpkMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem menuItem || menuItem.Tag is not string filePath)
                return;

            if (!File.Exists(filePath))
            {
                WarningBox($"Recent UPK was not found:\n{filePath}");
                upkBrowsePreferences.RecentUpkPaths.RemoveAll(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
                upkBrowsePreferencesStore.Save(upkBrowsePreferences);
                RefreshRecentUpksMenu();
                return;
            }

            await OpenUpkFileFromPath(filePath, 0);
        }

        private void setUpkBrowseFolderMenuItem_Click(object sender, EventArgs e)
        {
            using FolderBrowserDialog dialog = new()
            {
                Description = "Select the default folder to use when browsing for UPK files.",
                UseDescriptionForTitle = true
            };

            string initialFolder = GetPreferredUpkBrowseFolder();
            if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                dialog.InitialDirectory = initialFolder;

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;

            upkBrowsePreferences.DefaultUpkFolder = dialog.SelectedPath;
            upkBrowsePreferencesStore.Save(upkBrowsePreferences);
            RefreshRecentUpksMenu();
            MessageBox.Show(
                $"Default UPK browse folder set to:\n{dialog.SelectedPath}",
                "UPK Browse Folder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void clearRecentUpksMenuItem_Click(object sender, EventArgs e)
        {
            upkBrowsePreferencesStore.ClearRecents(upkBrowsePreferences);
            upkBrowsePreferencesStore.Save(upkBrowsePreferences);
            RefreshRecentUpksMenu();
        }

        private void SelectExportObject(int index)
        {
            if (index <= 0)
            {
                if (objectsTree.Nodes.Count > 0 && objectsTree.Nodes[0].Nodes.Count > 0)
                {
                    var firstNode = objectsTree.Nodes[0].Nodes[0];
                    objectsTree.SelectedNode = firstNode;
                    firstNode.EnsureVisible();
                }
                return;
            }

            var export = UpkFile.Header.ExportTable.FirstOrDefault(e => e.TableIndex == index);
            if (export == null) return;

            foreach (TreeNode root in objectsTree.Nodes)
                if (FindAndSelectNode(root, index)) return;
        }

        private bool FindAndSelectNode(TreeNode node, int index)
        {
            if (node.Tag is UnrealExportTableEntry entry && entry.TableIndex == index)
            {
                objectsTree.SelectedNode = node;
                node.EnsureVisible();
                return true;
            }

            foreach (TreeNode child in node.Nodes)
            {
                if (FindAndSelectNode(child, index))
                    return true;
            }

            return false;
        }

        private async Task LoadUpkFile(UnrealUpkFile upkFile)
        {
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                ClearPropertiesView();

                upkFile.Header = await repository.LoadUpkFile(Path.Combine(upkFile.ContentsRoot, upkFile.GameFilename));
                var header = upkFile.Header;
                await Task.Run(() => header.ReadHeaderAsync(OnLoadProgress));

                nameGridView.DataSource = ViewEntities.GetDataSource(header.NameTable);
                importGridView.DataSource = ViewEntities.GetDataSource(header.ImportTable);
                exportGridView.DataSource = ViewEntities.GetDataSource(header.ExportTable);
                propertyGrid.SelectedObject = new UnrealHeaderViewModel(header);
                UpdateInspectorSummary();

                ViewEntities.BuildObjectTree(rootNodes, header);
                UpdateObjectsTree();
                RefreshEnemyPlayableExports();
                // TODO(enemy-converter-upk-loading-hook): keep the converter mesh list in sync here whenever the active UPK lifecycle changes.
                ApplyTheme();
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void ClearPropertiesView()
        {
            if (propertiesView.Nodes.Count == 0) return;

            propertiesView.BeginUpdate();
            propertiesView.Nodes.Clear();
            propertiesView.EndUpdate();
        }

        private void UpdateObjectsTree()
        {
            filterBox.Text = "";
            if (rootNodes.Count == 0) return;

            objectsTree.Nodes.Clear();
            objectsTree.BeginUpdate();
            objectsTree.Nodes.AddRange([.. rootNodes]);
            foreach (TreeNode node in objectsTree.Nodes) node.Expand();
            objectsTree.EndUpdate();

            int count = objectsTree.Nodes[0]?.Nodes.Count ?? 0;
            totalStatus.Text = $"{count:N0}";
        }

        private void OnLoadProgress(UnrealLoadProgress progress)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnLoadProgress(progress)));
                return;
            }

            totalStatus.Text = $"{progress.Current:N0}";

            if (progress.IsComplete)
            {
                totalStatus.Text = $"{progress.Total:N0}";
                progressStatus.Text = "";
                return;
            }

            if (!string.IsNullOrEmpty(progress.Text))
                progressStatus.Text = progress.Text;
        }

        private async Task<UnrealUpkFile> OpenUpkFile(string filePath)
        {
            var file = new FileInfo(filePath);
            var fileHash = await Task.Run(() => file.OpenRead().GetHash<MD5>((int)file.Length));

            return new UnrealUpkFile
            {
                GameFilename = Path.GetFileName(file.FullName),
                Package = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant(),
                ContentsRoot = Path.GetDirectoryName(file.FullName),
                Filesize = file.Length,
                Filehash = fileHash
            };
        }

        private void saveMenuItem_Click(object sender, EventArgs e)
        {
            using var saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Unreal Package Files (*.upk)|*.upk";
            saveFileDialog.Title = "Save UPK file";

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filename = saveFileDialog.FileName;
                SaveUpkFile(filename);
            }
        }

        private void SaveUpkFile(string filename)
        {
            MessageBox.Show("Save function not ready", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void exportGridView_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex > -1 && exportGridView.Columns[e.ColumnIndex].Name == "buttonColumn")
            {
                var boundItem = exportGridView.Rows[e.RowIndex].DataBoundItem;
                var detailsProp = boundItem.GetType().GetProperty("Details");
                var entry = detailsProp?.GetValue(boundItem);

                if (entry != null)
                {
                    var grid = sender as DataGridView;
                    var parentForm = grid?.FindForm();
                    ViewEntities.ShowPropertyGrid(entry, parentForm);
                }
            }
        }

        private void filterClear_Click(object sender, EventArgs e)
        {
            UpdateObjectsTree();
        }

        private void filterBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (FilterTree(filterBox.Text))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }

                int count = 0;
                if (objectsTree.Nodes.Count > 0)
                    count = objectsTree.Nodes[0].Nodes.Count;

                totalStatus.Text = $"{count:N0}";
            }
        }

        private bool FilterTree(string filterText)
        {
            filterText = filterText.Trim().ToLower();
            if (filterText.Length < 3 || rootNodes.Count == 0) return false;

            objectsTree.BeginUpdate();
            objectsTree.Nodes.Clear();

            foreach (TreeNode rootNode in rootNodes)
            {
                var filteredNode = FilterNode(rootNode, filterText);
                if (filteredNode != null)
                    objectsTree.Nodes.Add(filteredNode);
            }

            objectsTree.ExpandAll();
            objectsTree.EndUpdate();

            return objectsTree.Nodes.Count > 0;
        }

        private static TreeNode FilterNode(TreeNode node, string filterText)
        {
            List<TreeNode> matchingChildren = [];

            foreach (TreeNode child in node.Nodes)
            {
                var filteredChild = FilterNode(child, filterText);
                if (filteredChild != null)
                    matchingChildren.Add(filteredChild);
            }

            if (node.Text.ToLower().Contains(filterText) || matchingChildren.Count > 0)
            {
                var newNode = new TreeNode(node.Text)
                {
                    Tag = node.Tag,
                    ImageIndex = node.ImageIndex,
                    SelectedImageIndex = node.SelectedImageIndex
                };

                newNode.Nodes.AddRange(matchingChildren.ToArray());
                return newNode;
            }

            return null;
        }

        private async void objectsTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            currentObject = e.Node?.Tag;
            UpdateInspectorSummary();
            viewObjectInHEXMenuItem.Enabled = false;

            viewTextureMenuItem.Enabled = false;
            viewModelMenuItem.Enabled = false;
            importFbxToSkeletalMeshMenuItem.Enabled = false;

            if (currentObject is null) return;

            objectNameClassMenuItem.Text = e.Node.Text;

            if (currentObject is UnrealExportTableEntry export)
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    if (export.UnrealObject == null)
                        await export.ParseUnrealObject(false, false);

                    viewObjectInHEXMenuItem.Enabled = true;

                    if (export.UnrealObject is IUnrealObject uObject)
                        BuildPropertyTree(uObject);
                    UpdateInspectorSummary();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error parse object:\n{export.ObjectNameIndex} :: {export.ClassReferenceNameIndex}\n\n{ex.Message}",
                        "Parse error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
            else if (currentObject is UnrealImportTableEntry importEntry)
            {
                propertiesView.Nodes.Clear();
            }
        }

        private async void propertiesView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var node = e.Node;

            if (node.Tag is VirtualNode virtualNode)
            {
                if (node.Nodes.Count == 1 && node.Nodes[0].Tag as string == "lazy-load")
                {
                    try
                    {
                        UseWaitCursor = true;
                        Cursor.Current = Cursors.WaitCursor;
                        node.Tag = virtualNode.Tag;

                        var childNodes = await Task.Run(() => LoadChildNodes(virtualNode));

                        propertiesView.BeginUpdate();
                        node.Nodes.Clear();
                        node.Nodes.AddRange(childNodes.ToArray());
                        propertiesView.EndUpdate();
                        node.Expand();
                    }
                    finally
                    {
                        UseWaitCursor = false;
                    }
                }
            }
        }

        private static List<TreeNode> LoadChildNodes(VirtualNode virtualNode)
        {
            var nodes = new List<TreeNode>();
            foreach (var child in virtualNode.Children)
                nodes.Add(CreateLazyNode(child));
            return nodes;
        }

        private void BuildPropertyTree(IUnrealObject uObject)
        {
            propertiesView.BeginUpdate();
            propertiesView.Nodes.Clear();

            foreach (VirtualNode virtualNode in uObject.FieldNodes)
                propertiesView.Nodes.Add(CreateLazyNode(virtualNode));

            var buffer = uObject.Buffer;
            if (!buffer.IsAbstractClass && (buffer.ResultProperty != ResultProperty.None || buffer.DataSize != 0))
            {
                var dataNode = new TreeNode($"Data [{buffer.ResultProperty}][{buffer.DataSize}]");

                var data = uObject.Buffer.Reader.GetBytes();
                if (uObject.Buffer.DataOffset >= 0 && uObject.Buffer.DataOffset < data.Length)
                {
                    int length = data.Length - uObject.Buffer.DataOffset;
                    byte[] offsetData = new byte[length];
                    Array.Copy(data, uObject.Buffer.DataOffset, offsetData, 0, length);
                    data = offsetData;
                }
                dataNode.Tag = data;

                propertiesView.Nodes.Add(dataNode);
            }

            if (uObject.UObject is UTexture2D) viewTextureMenuItem.Enabled = true;
            if (CheckMeshObject(uObject)) viewModelMenuItem.Enabled = true;
            if (CheckSkeletalMeshObject(uObject)) importFbxToSkeletalMeshMenuItem.Enabled = true;

            ExpandFiltered(propertiesView.Nodes);
            propertiesView.EndUpdate();
        }

        private static bool CheckMeshObject(IUnrealObject uObject)
        {
            return uObject.UObject is USkeletalMesh || uObject.UObject is UStaticMesh;
        }

        private static bool CheckSkeletalMeshObject(IUnrealObject uObject)
        {
            return uObject.UObject is USkeletalMesh;
        }

        private void ExpandFiltered(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                bool isLazy = node.Tag is VirtualNode;
                bool shouldExpand = (node.Nodes.Count > 0 && node.Nodes.Count < 11) || node.Text == "Properties";

                if (shouldExpand && !isLazy)
                {
                    node.Expand();
                    ExpandFiltered(node.Nodes);
                }
            }
        }

        private static TreeNode CreateLazyNode(VirtualNode virtualNode)
        {
            var node = new TreeNode(virtualNode.Text);

            if (virtualNode.Children.Count > 10 && node.Text != "Properties")
            {
                node.Tag = virtualNode;
                node.Nodes.Add(new TreeNode("Loading...") { Tag = "lazy-load" });
            }
            else
            {
                node.Tag = virtualNode.Tag;
                foreach (var child in virtualNode.Children)
                    node.Nodes.Add(CreateLazyNode(child));
            }

            return node;
        }

        private void viewObjectInHEXMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject == null) return;
            if (currentObject is UnrealExportTableEntry export && export.UnrealObject is IUnrealObject uObject)
            {
                var data = uObject.Buffer.Reader.GetBytes();
                openHexView(export.ObjectNameIndex.Name, data);
            }
        }

        private void propertiesMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            viewDataInHEXMenuItem.Enabled = false;
            copySelectedMenuItem.Enabled = false;
            findNameMenuItem.Enabled = false;
            copySelectedMenuItem.Text = "Copy [Selected]";
            findNameMenuItem.Text = "Find [Buffer]";

            if (Clipboard.ContainsText())
            {
                string clipboardText = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(clipboardText) && clipboardText.Length <= 50)
                {
                    findNameMenuItem.Enabled = true;
                    findNameMenuItem.Text = $"Find [{clipboardText}]";
                }
            }

            if (propertiesView.SelectedNode == null) return;

            copySelectedMenuItem.Enabled = true;
            copySelectedMenuItem.Text = $"Copy [{propertiesView.SelectedNode.Text}]";

            if (propertiesView.SelectedNode.Tag is not byte[] data || data.Length == 0) return;
            viewDataInHEXMenuItem.Enabled = true;
        }

        private void viewDataInHEXMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject == null) return;
            if (currentObject is UnrealExportTableEntry export)
            {
                if (propertiesView.SelectedNode.Tag is byte[] data)
                    openHexView(export.ObjectNameIndex.Name, data);
            }
        }

        private void objectNameClassMenuItem_Click(object sender, EventArgs e)
        {
            if (objectNameClassMenuItem.Text != null)
                Clipboard.SetText(objectNameClassMenuItem.Text);
        }

        private void openHexView(string name, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            hexViewForm.SetTitle(name);
            hexViewForm.SetHexData(data);
            hexViewForm.ShowDialog();
        }

        private void objectMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            packageInfoMenuItem.DropDownItems.Clear();

            if (currentObject == null)
            {
                viewObjectMenuItem.Enabled = false;
                viewParentMenuItem.Enabled = false;
                packageInfoMenuItem.Enabled = false;
                packageInfoMenuItem.Text = "Package Info";
                return;
            }

            viewObjectMenuItem.Enabled = true;
            viewParentMenuItem.Enabled = true;
            packageInfoMenuItem.Enabled = true;

            if (currentObject is UnrealImportTableEntry import)
            {
                if (UpkFilePackageSystem.IsPackageOuter(UpkFile.Header, import))
                {
                    var fullPath = import.GetPathName();
                    packageInfoMenuItem.Text = fullPath;

                    var locations = repository.PackageIndex?.GetLocations(fullPath);
                    if (locations == null || locations.Count == 0) return;
                    BuildLocationMenuItems(locations);
                }
            }
            else if (currentObject is UnrealExportTableEntry export)
            {
                packageInfoMenuItem.Text = export.GetPathName();
            }
        }

        private void BuildLocationMenuItems(List<UpkFilePackageSystem.LocationEntry> locations)
        {
            int index = 0;
            foreach (var loc in locations)
            {
                // Create menu item text
                string menuText = $"[{loc.ExportIndex}] {loc.UpkFileName}";

                // Create menu item
                var fileMenuItem = new ToolStripMenuItem(menuText);
                fileMenuItem.Tag = new FileLoadInfo
                {
                    FilePath = loc.UpkFileName,
                    ExportIndex = loc.ExportIndex
                };
                fileMenuItem.Click += PackageFileMenuItem_Click;

                packageInfoMenuItem.DropDownItems.Add(fileMenuItem);

                if (index++ >= 5) break; // Limit to first 5 items
            }

            // If there are more files, add info item
            var totalFiles = locations.Count;
            if (totalFiles > 5)
            {
                packageInfoMenuItem.DropDownItems.Add(new ToolStripSeparator());

                var moreItem = new ToolStripMenuItem($"+{totalFiles - 5} more files");
                moreItem.Enabled = false; // Make it non-clickable
                packageInfoMenuItem.DropDownItems.Add(moreItem);
            }
        }

        private async void PackageFileMenuItem_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag is FileLoadInfo info)
            {
                string fullPath = Path.Combine(UpkFile.ContentsRoot, info.FilePath);

                if (File.Exists(fullPath))
                    await OpenUpkFileFromPath(fullPath, info.ExportIndex);
                else
                    WarningBox($"File not found: {fullPath}");
            }
        }

        private class FileLoadInfo
        {
            public string FilePath { get; set; }
            public int ExportIndex { get; set; }
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            private static readonly Color MenuBack = Color.FromArgb(28, 28, 28);
            private static readonly Color MenuBorderColor = Color.FromArgb(58, 58, 58);
            private static readonly Color MenuSelect = Color.FromArgb(52, 95, 160);

            public override Color ToolStripDropDownBackground => MenuBack;
            public override Color ImageMarginGradientBegin => MenuBack;
            public override Color ImageMarginGradientMiddle => MenuBack;
            public override Color ImageMarginGradientEnd => MenuBack;
            public override Color MenuBorder => MenuBorderColor;
            public override Color MenuItemBorder => MenuBorderColor;
            public override Color MenuItemSelected => MenuSelect;
            public override Color MenuItemSelectedGradientBegin => MenuSelect;
            public override Color MenuItemSelectedGradientEnd => MenuSelect;
            public override Color MenuItemPressedGradientBegin => MenuBack;
            public override Color MenuItemPressedGradientMiddle => MenuBack;
            public override Color MenuItemPressedGradientEnd => MenuBack;
        }

        private sealed class RetargetWarningForm : Form
        {
            private readonly CheckBox _dontShowAgainCheckBox;

            public RetargetWarningForm()
            {
                Text = "Retarget Warning";
                Width = 820;
                Height = 520;
                MinimumSize = new Size(820, 520);
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                Label titleLabel = new()
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 14f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(196, 140, 0),
                    Text = "!!!WARNING!!!",
                    TextAlign = ContentAlignment.MiddleLeft
                };

                Label bodyLabel = new()
                {
                    Dock = DockStyle.Top,
                    Height = 86,
                    Text = "This is an experimental tool. Use at your own risk.",
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(0, 12, 0, 0)
                };

                Label backupLabel = new()
                {
                    Dock = DockStyle.Top,
                    Height = 96,
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11.5f, FontStyle.Bold),
                    ForeColor = Color.Firebrick,
                    Text = "Make sure to create a backup of your files beforehand.",
                    TextAlign = ContentAlignment.TopLeft,
                    Padding = new Padding(0, 10, 0, 0)
                };

                _dontShowAgainCheckBox = new CheckBox
                {
                    AutoSize = true,
                    Text = "Hide until next session",
                    Padding = new Padding(0, 2, 0, 0)
                };

                Button okButton = new()
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Width = 100,
                    Height = 40,
                    Anchor = AnchorStyles.Right | AnchorStyles.Bottom
                };

                FlowLayoutPanel buttonFlow = new()
                {
                    Dock = DockStyle.Right,
                    AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false
                };
                buttonFlow.Controls.Add(okButton);

                FlowLayoutPanel optionFlow = new()
                {
                    Dock = DockStyle.Left,
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    Padding = new Padding(0, 6, 0, 0)
                };
                optionFlow.Controls.Add(_dontShowAgainCheckBox);

                Panel buttonPanel = new()
                {
                    Dock = DockStyle.Bottom,
                    Height = 96,
                    Padding = new Padding(0, 28, 0, 0)
                };
                buttonPanel.Controls.Add(optionFlow);
                buttonPanel.Controls.Add(buttonFlow);

                Panel contentPanel = new()
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(24, 22, 24, 22)
                };
                contentPanel.Controls.Add(buttonPanel);
                contentPanel.Controls.Add(backupLabel);
                contentPanel.Controls.Add(bodyLabel);
                contentPanel.Controls.Add(titleLabel);

                Controls.Add(contentPanel);
                AcceptButton = okButton;
            }

            public bool SuppressForSession => _dontShowAgainCheckBox.Checked;
        }

        private sealed class ExportSelectionForm : Form
        {
            private readonly ListBox _listBox;

            public ExportSelectionForm(IEnumerable<string> values, string title)
            {
                Text = title;
                Width = 640;
                Height = 480;
                StartPosition = FormStartPosition.CenterParent;

                _listBox = new ListBox
                {
                    Dock = DockStyle.Fill
                };
                _listBox.Items.AddRange(values.ToArray());
                if (_listBox.Items.Count > 0)
                    _listBox.SelectedIndex = 0;
                _listBox.DoubleClick += (_, _) => ConfirmSelection();

                Button okButton = new()
                {
                    Text = "Select",
                    Dock = DockStyle.Bottom,
                    Height = 48
                };
                okButton.Click += (_, _) => ConfirmSelection();

                Controls.Add(_listBox);
                Controls.Add(okButton);
            }

            public string SelectedValue => _listBox.SelectedItem as string;

            private void ConfirmSelection()
            {
                if (_listBox.SelectedItem == null)
                    return;

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void viewParentMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject == null) return;
            if (currentObject is UnrealExportTableEntry export)
            {
                int index = export.SuperReference;
                if (index == 0)
                    index = export.ClassReference;

                if (index > 0)
                    selectExportIndex(index);
                else
                    selectImportIndex(index);
            }
            else if (currentObject is UnrealImportTableEntry import)
            {
                int nameIndex = import.ClassNameIndex.Index;
                int index = UpkFile.Header.GetClassNameTableIndex(nameIndex);
                if (index > 0)
                    selectExportIndex(index);
                else
                    selectImportIndex(index);
            }
        }

        private void viewObjectMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject == null) return;
            if (currentObject is UnrealExportTableEntry export)
                selectExportIndex(export.TableIndex);
            else if (currentObject is UnrealImportTableEntry import)
                selectImportIndex(import.TableIndex);
        }

        private void selectExportIndex(int tableIndex)
        {
            tabControl1.SelectTab(exportPage);
            int index = tableIndex - 1;
            if (index >= 0 && index < exportGridView.Rows.Count)
            {
                exportGridView.ClearSelection();
                exportGridView.Rows[index].Selected = true;
                exportGridView.CurrentCell = exportGridView.Rows[index].Cells[0];
                exportGridView.FirstDisplayedScrollingRowIndex = index;
            }
        }

        private void selectImportIndex(int tableIndex)
        {
            tabControl1.SelectTab(importPage);
            int index = -tableIndex - 1;
            if (index >= 0 && index < importGridView.Rows.Count)
            {
                importGridView.ClearSelection();
                importGridView.Rows[index].Selected = true;
                importGridView.CurrentCell = importGridView.Rows[index].Cells[0];
                importGridView.FirstDisplayedScrollingRowIndex = index;
            }
        }

        private void viewTextureMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject == null) return;
            if (currentObject is UnrealExportTableEntry export)
                openTextureView(export.ObjectNameIndex, export.UnrealObject);
        }

        private void openTextureView(FObject textureObject, UnrealObjectBase unrealObject)
        {
            if (unrealObject is IUnrealObject uObject && uObject.UObject is UTexture2D data)
            {
                textureViewForm.SetTitle(textureObject.Name);
                textureViewForm.SetTextureObject(textureObject, data);
                textureViewForm.ShowDialog();
            }
        }

        private string GetCurrentUpkPath()
        {
            if (UpkFile == null || string.IsNullOrWhiteSpace(UpkFile.ContentsRoot) || string.IsNullOrWhiteSpace(UpkFile.GameFilename))
                return null;

            return Path.Combine(UpkFile.ContentsRoot, UpkFile.GameFilename);
        }

        private string GetCurrentTextureExportPath()
        {
            if (currentObject is not UnrealExportTableEntry export)
                return null;

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UTexture2D)
                return null;

            return export.GetPathName();
        }

        private string GetCurrentSkeletalMeshExportPath()
        {
            if (currentObject is not UnrealExportTableEntry export)
                return null;

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh)
                return null;

            return export.GetPathName();
        }

        private void ShowTextureInPreviewTab(TexturePreviewTexture texture)
        {
            if (texture == null)
                return;

            texturePreviewPanel.LoadExternalTexture(texture);
            TabPage texturePreviewPage = tabControl2.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(page => string.Equals(page.Name, "textureWorkspacePage", StringComparison.Ordinal));
            if (texturePreviewPage != null)
                tabControl2.SelectedTab = texturePreviewPage;
        }

        private async Task PreviewCharacterTexturesOnMeshAsync(string upkPath, string meshExportPath, IReadOnlyList<(int SectionIndex, TexturePreviewMaterialSlot Slot, string ReplacementFilePath)> replacements)
        {
            if (string.IsNullOrWhiteSpace(upkPath) || string.IsNullOrWhiteSpace(meshExportPath) || replacements == null || replacements.Count == 0)
                return;

            await meshPreviewPanel.LoadUe3MeshFromUpkAsync(upkPath, meshExportPath).ConfigureAwait(true);
            meshPreviewPanel.ResetPreviewMaterial();
            meshPreviewPanel.SetDisplayMode(MHUpkManager.MeshPreview.MeshPreviewDisplayMode.Ue3Only);
            meshPreviewPanel.SetShadingMode(MHUpkManager.MeshPreview.MeshPreviewShadingMode.Lit);

            TextureLoader loader = new();
            int? focusSection = null;
            foreach ((int sectionIndex, TexturePreviewMaterialSlot slot, string path) in replacements)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                TexturePreviewTexture texture = loader.LoadFromFile(path, slot);
                texture.Slot = slot;
                meshPreviewPanel.SetUe3SectionPreviewTexture(sectionIndex, slot, texture);
                focusSection ??= sectionIndex;
            }

            meshPreviewPanel.SetMaterialPreviewEnabled(true);
            if (focusSection.HasValue)
                meshPreviewPanel.FocusUe3Section(focusSection.Value);
            meshPreviewPanel.RefreshPreview();
            meshWorkspacePanel.ShowPreviewView();

            TabPage meshPage = tabControl2.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(page => string.Equals(page.Name, "meshWorkspacePage", StringComparison.Ordinal));
            if (meshPage != null)
                tabControl2.SelectedTab = meshPage;
        }

        private void ClearCharacterTexturePreviewOnMesh()
        {
            meshPreviewPanel.ResetPreviewMaterial();
            meshPreviewPanel.SetDisplayMode(MHUpkManager.MeshPreview.MeshPreviewDisplayMode.Ue3Only);
            meshPreviewPanel.RefreshPreview();
        }

        private void viewModelMenuItem_Click(object sender, EventArgs e)
        {
            if (currentObject is UnrealExportTableEntry export)
                openModelView(export.ObjectNameIndex.Name, export.UnrealObject);
        }

        private async void importFbxToSkeletalMeshMenuItem_Click(object sender, EventArgs e)
        {
            if (UpkFile?.Header == null || currentObject is not UnrealExportTableEntry export)
                return;

            if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh)
            {
                WarningBox("The selected export is not a SkeletalMesh.");
                return;
            }

            using var openFileDialog = new OpenFileDialog
            {
                Filter = "FBX Files (*.fbx)|*.fbx",
                Title = "Select FBX to Import"
            };

            if (openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            string sourceUpkPath = Path.Combine(UpkFile.ContentsRoot, UpkFile.GameFilename);
            DialogResult confirm = MessageBox.Show(
                $"This will replace the currently opened UPK:\n{sourceUpkPath}\n\nA backup will be written next to it as:\n{sourceUpkPath}.bak\n\nContinue?",
                "Replace Current UPK",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.OK)
                return;

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                progressStatus.Text = "Importing FBX into SkeletalMesh...";

                string backupPath = await SkeletalMeshImportRunner.ImportAndReplaceAsync(
                    sourceUpkPath,
                    export.GetPathName(),
                    openFileDialog.FileName).ConfigureAwait(true);

                progressStatus.Text = "FBX import completed.";
                MessageBox.Show(
                    $"Imported FBX into '{export.GetPathName()}' and replaced:\n{sourceUpkPath}\n\nBackup created:\n{backupPath}",
                    "Import Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                progressStatus.Text = "FBX import failed.";
                string logPath = ImportDiagnostics.WriteException(ex);
                MessageBox.Show(
                    $"FBX import failed for '{export.GetPathName()}'.\n\n{ex}\n\nLog written to:\n{logPath}",
                    "Import Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void openModelView(string name, UnrealObjectBase unrealObject)
        {
            if (unrealObject is IUnrealObject uObject && CheckMeshObject(uObject))
            {
                using var modelViewForm = new ModelViewForm();
                string sourceUpkPath = UpkFile == null ? null : Path.Combine(UpkFile.ContentsRoot, UpkFile.GameFilename);
                string meshExportPath = currentObject is UnrealExportTableEntry export ? export.GetPathName() : null;
                modelViewForm.SetMeshObject(name, uObject.UObject as UObject, sourceUpkPath, meshExportPath);
                modelViewForm.ShowDialog();
            }
        }

        private void loadManifestMenuItem_Click(object sender, EventArgs e)
        {
            string manifestName = TextureManifest.ManifestName;
            using var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Texture Manifest (*.bin)|" + manifestName;
            openFileDialog.Title = "Select " + manifestName;

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string selectedFile = Path.GetFileName(openFileDialog.FileName);

                if (selectedFile != manifestName)
                {
                    MessageBox.Show("Please select the correct file: " + manifestName,
                                    "Invalid File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string filePath = openFileDialog.FileName;
                int totalEntries = 0;
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    totalEntries = TextureManifest.Instance.LoadManifest(filePath);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                    tfcStatus.Text = $"Ready ({totalEntries:N0} textures)";
                }
            }
        }

        private void copySelectedMenuItem_Click(object sender, EventArgs e)
        {
            if (propertiesView.SelectedNode == null) return;
            Clipboard.SetText(propertiesView.SelectedNode.Text);
        }

        private void findNameMenuItem_Click(object sender, EventArgs e)
        {
            if (!Clipboard.ContainsText()) return;

            string searchText = Clipboard.GetText().Trim();
            if (string.IsNullOrEmpty(searchText)) return;

            TreeNode foundNode = FindNodeByText(propertiesView.Nodes, searchText);

            if (foundNode != null)
            {
                foundNode.EnsureVisible();
                propertiesView.SelectedNode = foundNode;
                propertiesView.Focus();
            }
            else
            {
                MessageBox.Show($"Text '{searchText}' not found in tree", "Search Result", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static TreeNode FindNodeByText(TreeNodeCollection nodes, string searchText)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    return node;

                TreeNode foundInChildren = FindNodeByText(node.Nodes, searchText);
                if (foundInChildren != null)
                    return foundInChildren;
            }

            return null;
        }

        private void packageInfoMenuItem_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(packageInfoMenuItem.Text);
        }
    }
}

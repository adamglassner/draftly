using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Draftly.Dialogs;
using Draftly.Services;
using Microsoft.Win32;

namespace Draftly;

public partial class MainWindow
{
    // ================================================================== File menu

    private void FileButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = FileButton, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        MenuItem Item(string header, string glyph, string gesture, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            if (glyph != null) mi.Icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"), FontSize = 15 };
            mi.Click += (_, _) => action();
            return mi;
        }

        menu.Items.Add(Item("New", "", "Ctrl+N", NewDocument));
        menu.Items.Add(Item("Open…", "", "Ctrl+O", OpenDocument));
        menu.Items.Add(Item("Save", "", "Ctrl+S", () => Save()));
        var saveAs = Item("Save as", "", "", () => { });
        saveAs.Items.Add(Item("Rich Text document (.rtf)", null, "Ctrl+Shift+S", () => SaveAs(DocFormat.Rtf)));
        saveAs.Items.Add(Item("Plain text document (.txt)", null, "", () => SaveAs(DocFormat.Txt)));
        saveAs.Items.Add(Item("Other format…", null, "F12", () => SaveAs(null)));
        menu.Items.Add(saveAs);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Print…", "", "Ctrl+P", Print));
        menu.Items.Add(Item("Print preview", "", "Ctrl+F2", PrintPreview));
        menu.Items.Add(Item("Page setup…", "", "", PageSetup));
        menu.Items.Add(new Separator());

        var recent = Item("Recent documents", "", "", () => { });
        var files = S.RecentFiles.Take(S.RecentFilesMax).ToList();
        if (files.Count == 0) recent.Items.Add(Item("No recent documents", null, "", () => { }, false));
        int n = 1;
        foreach (var f in files)
        {
            var path = f;
            var mi = Item($"_{n++}  {Path.GetFileName(f)}", null, "", () => { if (ConfirmDiscard()) OpenFile(path); });
            mi.ToolTip = f;
            recent.Items.Add(mi);
        }
        if (files.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(Item("Clear list", null, "", () => { S.RecentFiles.Clear(); SettingsService.Save(); }));
        }
        menu.Items.Add(recent);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", "", "Ctrl+,", () => OpenSettings()));
        menu.Items.Add(Item("About Draftly", "", "", () => OpenSettings("About")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", "", "Alt+F4", Close));

        menu.Closed += (_, _) => Editor.Focus();
        menu.IsOpen = true;
    }

    // ================================================================== New / Open

    private void NewDocument()
    {
        if (!ConfirmDiscard()) return;
        _path = null;
        _recoveredFrom = null;
        _importedFrom = null;
        _encoding = null;
        _format = S.SaveFormat == DefaultSaveFormat.Txt ? DocFormat.Txt : DocFormat.Rtf;
        _layout = PageLayout.FromSettings(S);
        SetDocument(DocumentService.NewDocument());
        HideInfo();
        Editor.Focus();
    }

    private void OpenDocument()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = DocumentService.OpenFilter,
            InitialDirectory = Directory.Exists(S.LastFolder) ? S.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return;
        OpenFile(dlg.FileName);
    }

    /// <summary>Opens a file. Callers must already have asked about unsaved changes.</summary>
    public void OpenFile(string path)
    {
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var result = DocumentService.Load(path);
            _recoveredFrom = null;
            _layout = PageLayout.FromSettings(S);
            _encoding = result.TextEncoding;
            if (result.Format == DocFormat.Docx)
            {
                // Never overwrite a .docx with RTF. Saving goes through Save As.
                _path = null;
                _importedFrom = path;
                _format = DocFormat.Rtf;
            }
            else
            {
                _path = path;
                _importedFrom = null;
                _format = result.Format;
            }
            SetDocument(result.Document);

            S.LastFolder = Path.GetDirectoryName(path);
            SettingsService.AddRecent(path);

            if (result.Warning != null) ShowInfo(result.Warning);
            else if (_pendingRecovery == null) HideInfo();
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageDialog.Show(this, "Open", $"Draftly couldn't open \"{Path.GetFileName(path)}\".\n\n{ex.Message}", MessageDialog.Kind.Error);
            S.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase) && !File.Exists(p));
            SettingsService.Save();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        Editor.Focus();
    }

    // ================================================================== Save

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    /// <summary>Returns false if the user cancelled or saving failed.</summary>
    private bool Save()
    {
        if (_path == null) return SaveAs(_format);
        if (_format == DocFormat.Txt && DocumentHasFormatting() &&
            !MessageDialog.Ask(this, "Save as plain text",
                $"\"{Path.GetFileName(_path)}\" is a plain text file. Formatting, pictures and tables will be lost when it's saved.\n\nSave as plain text anyway?\n\nTo keep the formatting, choose No and use Save as › Rich Text document."))
            return false;
        return WriteTo(_path, _format, _encoding);
    }

    private bool SaveAs(DocFormat? format)
    {
        var fmt = format ?? _format;
        var baseName = Path.GetFileNameWithoutExtension(_path ?? _importedFrom ?? _recoveredFrom ?? "Document");
        var dlg = new SaveFileDialog
        {
            Filter = DocumentService.SaveFilter,
            FilterIndex = fmt == DocFormat.Txt ? 2 : 1,
            FileName = baseName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Directory.Exists(S.LastFolder) ? S.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return false;

        var path = dlg.FileName;
        Encoding enc = null;
        switch (dlg.FilterIndex)
        {
            case 2: fmt = DocFormat.Txt; enc = _format == DocFormat.Txt && _encoding != null ? _encoding : DocumentService.EncodingFor(S.TextEncoding); break;
            case 3: fmt = DocFormat.Txt; enc = Encoding.Unicode; break;
            default: fmt = DocFormat.Rtf; break;
        }
        if (Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, fmt == DocFormat.Txt ? ".txt" : ".rtf");

        if (fmt == DocFormat.Txt && DocumentHasFormatting() &&
            !MessageDialog.Ask(this, "Save as plain text", "Plain text can't store formatting, pictures or tables. They'll be removed from the saved file.\n\nContinue?"))
            return false;

        if (!WriteTo(path, fmt, enc)) return false;
        _path = path;
        _format = fmt;
        _encoding = fmt == DocFormat.Txt ? enc : null;
        _recoveredFrom = null;
        _importedFrom = null;
        S.LastFolder = Path.GetDirectoryName(path);
        SettingsService.AddRecent(path);
        SetDirty(false);
        UpdateStatus();
        return true;
    }

    private bool WriteTo(string path, DocFormat format, Encoding enc)
    {
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            DocumentService.Save(Editor.Document, path, format, enc);
            SetDirty(false);
            return true;
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageDialog.Show(this, "Save", $"Draftly couldn't save \"{Path.GetFileName(path)}\".\n\n{ex.Message}", MessageDialog.Kind.Error);
            return false;
        }
        finally { Mouse.OverrideCursor = null; }
    }

    /// <summary>True if saving as plain text would lose something.</summary>
    private bool DocumentHasFormatting()
    {
        foreach (var el in DocumentService.Walk(Editor.Document.Blocks))
        {
            if (el is InlineUIContainer or BlockUIContainer or Table or List) return true;
            if (el is Run r && r.Text.Length > 0 &&
                (r.ReadLocalValue(TextElement.FontWeightProperty) != DependencyProperty.UnsetValue ||
                 r.ReadLocalValue(TextElement.FontStyleProperty) != DependencyProperty.UnsetValue ||
                 r.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue ||
                 r.ReadLocalValue(Inline.TextDecorationsProperty) != DependencyProperty.UnsetValue))
                return true;
        }
        return false;
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        switch (SaveChangesDialog.Ask(this, DocName))
        {
            case SaveChoice.Save: return Save();
            case SaveChoice.DontSave: return true;
            default: return false;
        }
    }

    // ================================================================== Print

    private void Print()
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = true };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            var paginator = PrintService.CreatePaginator(Editor.Document, _layout);
            dlg.PrintDocument(paginator, DocName);
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageDialog.Show(this, "Print", "Printing failed.\n\n" + ex.Message, MessageDialog.Kind.Error);
        }
        finally { Mouse.OverrideCursor = null; }
    }

    private void PrintPreview()
    {
        PrintService.Preview preview;
        try
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            preview = new PrintService.Preview(PrintService.CreatePaginator(Editor.Document, _layout));
        }
        catch (Exception ex)
        {
            Mouse.OverrideCursor = null;
            MessageDialog.Show(this, "Print preview", "The preview couldn't be created.\n\n" + ex.Message, MessageDialog.Kind.Error);
            return;
        }
        finally { Mouse.OverrideCursor = null; }

        var win = new PrintPreviewWindow(this, preview, DocName);
        win.PrintRequested += () => { win.Close(); Print(); };
        win.PageSetupRequested += () => { win.Close(); if (PageSetup()) PrintPreview(); };
        win.ShowDialog();
    }

    private bool PageSetup()
    {
        var d = new PageSetupDialog(_layout, S.Units);
        if (d.ShowDialog() != true || d.Result == null) return false;
        _layout = d.Result;
        if (d.MakeDefault)
        {
            S.PaperSize = _layout.PaperName; S.Landscape = _layout.Landscape;
            S.MarginLeftIn = _layout.MarginLeft / 96; S.MarginRightIn = _layout.MarginRight / 96;
            S.MarginTopIn = _layout.MarginTop / 96; S.MarginBottomIn = _layout.MarginBottom / 96;
            S.PrintPageNumbers = _layout.PageNumbers;
            SettingsService.Save();
        }
        UpdatePageLayout();
        UpdateStatus();
        return true;
    }
}

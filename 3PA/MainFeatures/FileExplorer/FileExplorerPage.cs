﻿#region header
// ========================================================================
// Copyright (c) 2015 - Julien Caillon (julien.caillon@gmail.com)
// This file (FileExplorerPage.cs) is part of 3P.
// 
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightIdeasSoftware;
using YamuiFramework.Animations.Transitions;
using YamuiFramework.Controls;
using YamuiFramework.Fonts;
using YamuiFramework.Themes;
using _3PA.Images;
using _3PA.Interop;
using _3PA.Lib;
using _3PA.MainFeatures.AutoCompletion;
using _3PA.MainFeatures.FilesInfoNs;
using _3PA.MainFeatures.ProgressExecutionNs;

namespace _3PA.MainFeatures.FileExplorer {
    public partial class FileExplorerPage : YamuiPage {

        #region Fields
        private const string StrEmptyList = "No files found!";
        private const string StrItems = " items";

        private string[] _explorerDirStr;

        /// <summary>
        /// The filter to apply to the autocompletion form
        /// </summary>
        public string FilterByText {
            get { return _filterByText; }
            set { _filterByText = value.ToLower(); ApplyFilter(); }
        }
        private static string _filterByText = "";

        public bool UseAlternateBackColor {
            set { ovl.UseAlternatingBackColors = value; }
        }

        /// <summary>
        ///  gets or sets the total items currently displayed in the form
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// The value ranging from 0 to 3 and indicating which folder we are exploring
        /// </summary>
        public int DirectoryToExplorer { get; private set; }

        // List of displayed type of file
        private static Dictionary<FileType, SelectorButton<FileType>> _displayedTypes;

        // remember the list that was passed to the autocomplete form when we set the items, we need this
        // because we reorder the list each time the user filters stuff, but we need the original order
        private List<FileObject> _initialObjectsList;

        private int _currentType;

        private static bool _isListing;
        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public FileExplorerPage() {
            InitializeComponent();

            #region object view list

            // Image getter
            FileName.ImageGetter += ImageGetter;

            // Style the control
            StyleOvlTree();

            // Register to events
            ovl.DoubleClick += OvlOnDoubleClick;
            ovl.KeyDown += OvlOnKeyDown;
            ovl.Click += OvlOnClick;
            ovl.CellRightClick += OvlOnCellRightClick;

            // decorate rows
            ovl.UseCellFormatEvents = true;
            ovl.FormatCell += OvlOnFormatCell;

            // problems with the width of the column, set here
            FileName.Width = ovl.Width - 17;
            ovl.SizeChanged += (sender, args) => {
                FileName.Width = ovl.Width - 17;
                ovl.Invalidate();
            };

            #endregion

            #region Current file

            // register to Updated Operation events
            FilesInfo.UpdatedOperation += FilesInfoOnUpdatedOperation;
            FilesInfo.UpdatedErrors += FilesInfoOnUpdatedErrors;

            btGetHelp.BackGrndImage = (Config.Instance.GlobalShowErrorHelp) ? ImageResources.GetHelp : Utils.MakeGrayscale3(ImageResources.GetHelp);
            UpdateErrorButtons(false);

            btGetHelp.ButtonPressed += BtGetHelpOnButtonPressed;
            btPrevError.ButtonPressed += BtPrevErrorOnButtonPressed;
            btNextError.ButtonPressed += BtNextErrorOnButtonPressed;
            btClearAllErrors.ButtonPressed += BtClearAllErrorsOnButtonPressed;

            toolTipHtml.SetToolTip(btGetHelp, "Toggle on/off the <b>detailed help</b> for compilation errors and warnings");
            toolTipHtml.SetToolTip(btPrevError, "<b>Move the caret</b> to the previous error");
            toolTipHtml.SetToolTip(btNextError, "<b>Move the caret</b> to the next error");
            toolTipHtml.SetToolTip(btClearAllErrors, "<b>Clear</b> all the displayed errors");

            #endregion

            #region Actions

            // Builds a list of buttons
            var buttonList = new List<Tuple<Image, Action>> {
                new Tuple<Image, Action>(ImageResources.External, () => { })
            };
            foreach (var buttonSpec in buttonList) {
                var button = new YamuiImageButton {
                    Size = new Size(20, 20),
                    BackGrndImage = buttonSpec.Item1,
                    Margin = new Padding(0)
                };
                var spec = buttonSpec;
                button.ButtonPressed += (sender, args) => { spec.Item2(); };
                flowLayoutPanel.Controls.Add(button);
                //flowLayoutPanel.SetFlowBreak(button, true);
            }

            #endregion

            #region File explorer misc

            // button images
            btErase.BackGrndImage = ImageResources.eraser;
            btRefresh.BackGrndImage = ImageResources.refresh;

            // events
            textFilter.TextChanged += TextFilterOnTextChanged;
            textFilter.KeyDown += TextFilterOnKeyDown;
            btRefresh.ButtonPressed += BtRefreshOnButtonPressed;
            btErase.ButtonPressed += BtEraseOnButtonPressed;

            // button tooltips
            toolTipHtml.SetToolTip(btErase, "<b>Erase</b> the content of the text filter");
            toolTipHtml.SetToolTip(btRefresh, "Click this button to <b>refresh</b> the list of files for the current directory<br>No automatic refreshing is done so you have to use this button when you add/delete a file in said directory");
            toolTipHtml.SetToolTip(textFilter, "Start writing a file name to <b>filter</b> the list below");

            btGotoDir.BackGrndImage = ImageResources.OpenInExplorer;
            btDirectory.BackGrndImage = ImageResources.ExplorerDir0;
            _explorerDirStr = new[] { "Local path ", "Compilation path", "Propath", "Everywhere" };
            lbDirectory.Text = _explorerDirStr[0];
            btDirectory.ButtonPressed += BtDirectoryOnButtonPressed;
            btGotoDir.ButtonPressed += BtGotoDirOnButtonPressed;
            lbDirectory.Click += (sender, args) => BtDirectoryOnButtonPressed(sender, new ButtonPressedEventArgs(args));

            #endregion

        }

        #endregion

        #region File explorer file list

        #region cell formatting and style ovl

        /// <summary>
        /// Return the image that needs to be display on the left of an item
        /// representing its type
        /// </summary>
        /// <param name="typeStr"></param>
        /// <returns></returns>
        private static Image GetImageFromStr(string typeStr) {
            Image tryImg = (Image)ImageResources.ResourceManager.GetObject(typeStr);
            return tryImg ?? ImageResources.Error;
        }

        /// <summary>
        /// Image getter for object rows
        /// </summary>
        /// <param name="rowObject"></param>
        /// <returns></returns>
        private static object ImageGetter(object rowObject) {
            var obj = (FileObject)rowObject;
            if (obj == null) return ImageResources.Error;
            return GetImageFromStr(obj.Type + "Type");
        }

        /// <summary>
        /// Event on format cell
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private static void OvlOnFormatCell(object sender, FormatCellEventArgs args) {
            FileObject obj = (FileObject)args.Model;

            // currently document
            if (obj.FullPath.Equals(Plug.CurrentFilePath)) {
                RowBorderDecoration rbd = new RowBorderDecoration {
                    FillBrush = new SolidBrush(Color.FromArgb(50, ThemeManager.Current.AutoCompletionFocusBackColor)),
                    BorderPen = new Pen(Color.FromArgb(128, ThemeManager.Current.AutoCompletionFocusForeColor), 1),
                    BoundsPadding = new Size(-2, 0),
                    CornerRounding = 6.0f
                };
                args.SubItem.Decoration = rbd;
            }

            // display the flags
            int offset = -5;
            foreach (var name in Enum.GetNames(typeof(FileFlag))) {
                FileFlag flag = (FileFlag)Enum.Parse(typeof(FileFlag), name);
                if (flag == 0) continue;
                if (!obj.Flags.HasFlag(flag)) continue;
                Image tryImg = (Image)ImageResources.ResourceManager.GetObject(name);
                if (tryImg == null) continue;
                ImageDecoration decoration = new ImageDecoration(tryImg, 100, ContentAlignment.MiddleRight) {
                    Offset = new Size(offset, 0)
                };
                if (args.SubItem.Decoration == null)
                    args.SubItem.Decoration = decoration;
                else
                    args.SubItem.Decorations.Add(decoration);
                offset -= 20;
            }

            // display the sub string
            if (offset < -5) offset -= 5;
            if (!string.IsNullOrEmpty(obj.SubString)) {
                TextDecoration decoration = new TextDecoration(obj.SubString, 100) {
                    Alignment = ContentAlignment.MiddleRight,
                    Offset = new Size(offset, 0),
                    Font = FontManager.GetFont(FontStyle.Bold, 11),
                    TextColor = ThemeManager.Current.AutoCompletionNormalSubTypeForeColor,
                    CornerRounding = 1f,
                    Rotation = 0,
                    BorderWidth = 1,
                    BorderColor = ThemeManager.Current.AutoCompletionNormalSubTypeForeColor
                };
                args.SubItem.Decorations.Add(decoration);
            }
        }

        /// <summary>
        /// Apply thememanager theme to the treeview
        /// </summary>
        public void StyleOvlTree() {
            // Style the control
            ovl.OwnerDraw = true;
            ovl.Font = FontManager.GetLabelFont(LabelFunction.AutoCompletion);
            ovl.BackColor = ThemeManager.Current.AutoCompletionNormalBackColor;
            ovl.AlternateRowBackColor = ThemeManager.Current.AutoCompletionNormalAlternateBackColor;
            ovl.ForeColor = ThemeManager.Current.AutoCompletionNormalForeColor;
            ovl.HighlightBackgroundColor = ThemeManager.Current.AutoCompletionFocusBackColor;
            ovl.HighlightForegroundColor = ThemeManager.Current.AutoCompletionFocusForeColor;
            ovl.UnfocusedHighlightBackgroundColor = ovl.HighlightBackgroundColor;
            ovl.UnfocusedHighlightForegroundColor = ovl.HighlightForegroundColor;

            // Decorate and configure hot item
            ovl.UseHotItem = true;
            ovl.HotItemStyle = new HotItemStyle {
                BackColor = ThemeManager.Current.AutoCompletionHoverBackColor,
                ForeColor = ThemeManager.Current.AutoCompletionHoverForeColor
            };

            // overlay of empty list :
            ovl.EmptyListMsg = StrEmptyList;
            TextOverlay textOverlay = ovl.EmptyListMsgOverlay as TextOverlay;
            if (textOverlay != null) {
                textOverlay.TextColor = ThemeManager.Current.AutoCompletionNormalForeColor;
                textOverlay.BackColor = ThemeManager.Current.AutoCompletionNormalAlternateBackColor;
                textOverlay.BorderColor = ThemeManager.Current.AutoCompletionNormalForeColor;
                textOverlay.BorderWidth = 4.0f;
                textOverlay.Font = FontManager.GetFont(FontStyle.Bold, 30f);
                textOverlay.Rotation = -5;
            }
        }

        #endregion

        #region Set items and selector mechanic

        /// <summary>
        /// Call this method to completly refresh the object view list, 
        /// </summary>
        public void RefreshOvl() {
            _isListing = true;
            Task.Factory.StartNew(() => {
                try {
                    // get the list of FileObjects
                    _initialObjectsList = new List<FileObject>();
                    switch (DirectoryToExplorer) {
                        case 0:
                            _initialObjectsList = FileExplorer.ListFileOjectsInDirectory(ProgressEnv.Current.BaseLocalPath);
                            break;
                        case 1:
                            _initialObjectsList = FileExplorer.ListFileOjectsInDirectory(ProgressEnv.Current.BaseCompilationPath);
                            break;
                        case 2:
                            foreach (var dir in ProgressEnv.Current.GetProPathFileList) {
                                _initialObjectsList.AddRange(FileExplorer.ListFileOjectsInDirectory(dir, false));
                            }
                            break;
                        default:
                            // List every folder, must all be unique, then feed to ListFileOjectsInDirectory
                            break;
                    }

                    // apply custom sorting
                    _initialObjectsList.Sort(new FilesSortingClass());

                    // invoke on ui thread
                    BeginInvoke((Action)delegate {
                        try {
                            // delete any existing buttons
                            if (_displayedTypes != null) {
                                foreach (var selectorButton in _displayedTypes) {
                                    selectorButton.Value.ButtonPressed -= HandleTypeClick;
                                    if (yamuiPanel3.Controls.Contains(selectorButton.Value))
                                        yamuiPanel3.Controls.Remove(selectorButton.Value);
                                    selectorButton.Value.Dispose();
                                }
                            }

                            // get distinct types, create a button for each
                            int xPos = nbitems.Left + nbitems.Width + 10;
                            int yPox = yamuiPanel3.Height - 28;
                            _displayedTypes = new Dictionary<FileType, SelectorButton<FileType>>();
                            foreach (var type in _initialObjectsList.Select(x => x.Type).Distinct()) {
                                var but = new SelectorButton<FileType> {
                                    BackGrndImage = GetImageFromStr(type + "Type"),
                                    Activated = true,
                                    Size = new Size(24, 24),
                                    TabStop = false,
                                    Location = new Point(xPos, yPox),
                                    Type = type,
                                    AcceptsRightClick = true
                                };
                                but.ButtonPressed += HandleTypeClick;
                                toolTipHtml.SetToolTip(but, "Type of item : <b>" + type + "</b>:<br><br><b>Left click</b> to toggle on/off this filter<br><b>Right click</b> to filter for this type only");
                                _displayedTypes.Add(type, but);
                                yamuiPanel3.Controls.Add(but);
                                xPos += but.Width;
                            }

                            // label for the number of items
                            TotalItems = _initialObjectsList.Count;
                            nbitems.Text = TotalItems + StrItems;
                            ovl.SetObjects(_initialObjectsList);
                        } catch (Exception e) {
                            ErrorHandler.ShowErrors(e, "Error while showing the list of files");
                        } finally {
                            _isListing = false;
                        }
                    });
                } catch (Exception e) {
                    ErrorHandler.ShowErrors(e, "Error while listing files");
                    _isListing = false;
                }
            });
        }

        /// <summary>
        /// Call this method before showing the list when you don't use SetItems to sort the
        /// items (it is already called by SetItems())
        /// </summary>
        public void SortItems() {
            _initialObjectsList.Sort(new FilesSortingClass());
            ovl.SetObjects(_initialObjectsList);
        }

        /// <summary>
        /// use this to programmatically uncheck any type that is not in the given list
        /// </summary>
        /// <param name="allowedType"></param>
        public void SetActiveType(List<FileType> allowedType) {
            if (_displayedTypes == null) return;
            if (allowedType == null) allowedType = new List<FileType>();
            foreach (var selectorButton in _displayedTypes) {
                selectorButton.Value.Activated = allowedType.IndexOf(selectorButton.Value.Type) >= 0;
                selectorButton.Value.Invalidate();
            }
        }

        /// <summary>
        /// use this to programmatically check any type that is not in the given list
        /// </summary>
        /// <param name="allowedType"></param>
        public void SetUnActiveType(List<FileType> allowedType) {
            if (_displayedTypes == null) return;
            if (allowedType == null) allowedType = new List<FileType>();
            foreach (var selectorButton in _displayedTypes) {
                selectorButton.Value.Activated = allowedType.IndexOf(selectorButton.Value.Type) < 0;
                selectorButton.Value.Invalidate();
            }
        }

        /// <summary>
        /// reset all the button Types to activated
        /// </summary>
        public void ResetActiveType() {
            if (_displayedTypes == null) return;
            foreach (var selectorButton in _displayedTypes) {
                selectorButton.Value.Activated = true;
                selectorButton.Value.Invalidate();
            }
        }

        /// <summary>
        /// allows to programmatically select the first item of the list
        /// </summary>
        public void SelectFirstItem() {
            if (TotalItems > 0) ovl.SelectedIndex = 0;
        }

        #endregion

        #region events

        /// <summary>
        /// Executed when the user double click an item or press enter
        /// </summary>
        public void OnActivateItem() {
            var curItem = GetCurrentFile();
            if (curItem == null)
                return;

            Utils.OpenAnyFullPath(curItem.FullPath);
        }

        /// <summary>
        /// handles click on a type
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HandleTypeClick(object sender, ButtonPressedEventArgs args) {
            var mouseEvent = args.OriginalEventArgs as MouseEventArgs;
            FileType clickedType = ((SelectorButton<FileType>)sender).Type;

            // on right click
            if (mouseEvent != null && mouseEvent.Button == MouseButtons.Right) {
                // everything is unactive but this one
                if (_displayedTypes.Count(b => b.Value.Activated) == 1 && _displayedTypes.First(b => b.Value.Activated).Key == clickedType) {
                    SetUnActiveType(null);
                } else {
                    SetActiveType(new List<FileType> { clickedType });
                }
            } else
                // left click is only a toggle
                _displayedTypes[clickedType].Activated = !_displayedTypes[clickedType].Activated;

            _displayedTypes[clickedType].Invalidate();
            ApplyFilter();
        }

        /// <summary>
        /// handles double click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OvlOnDoubleClick(object sender, EventArgs eventArgs) {
            OnActivateItem();
        }

        /// <summary>
        /// Handles keydown event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="keyEventArgs"></param>
        private void OvlOnKeyDown(object sender, KeyEventArgs keyEventArgs) {
            keyEventArgs.Handled = OnKeyDown(keyEventArgs.KeyCode);
        }

        private void OvlOnClick(object sender, EventArgs eventArgs) {
            if (!KeyInterceptor.GetModifiers().IsAlt)
                return;
            var curItem = GetCurrentFile();
            if (curItem != null) {
                // remove or add favourite flag
                if (curItem.Flags.HasFlag(FileFlag.Favourite))
                    curItem.Flags &= ~FileFlag.Favourite;
                else
                    curItem.Flags |= FileFlag.Favourite;
            }
        }

        private void OvlOnCellRightClick(object sender, CellRightClickEventArgs cellRightClickEventArgs) {
            var fileObj = (FileObject) cellRightClickEventArgs.Model;
            if (fileObj != null) {
                Utils.OpenFileInFolder(fileObj.FullPath);
                cellRightClickEventArgs.Handled = true;
            }
        }

        #endregion

        #region on key events

        public bool OnKeyDown(Keys key) {
            bool handled = true;
            // down and up change the selection
            if (key == Keys.Up) {
                if (ovl.SelectedIndex > 0)
                    ovl.SelectedIndex--;
                else
                    ovl.SelectedIndex = (TotalItems - 1);
                if (ovl.SelectedIndex >= 0)
                    ovl.EnsureVisible(ovl.SelectedIndex);
            } else if (key == Keys.Down) {
                if (ovl.SelectedIndex < (TotalItems - 1))
                    ovl.SelectedIndex++;
                else
                    ovl.SelectedIndex = 0;
                if (ovl.SelectedIndex >= 0)
                    ovl.EnsureVisible(ovl.SelectedIndex);

                // escape close
            } else if (key == Keys.Escape) {
                Npp.GrabFocus();

                // left and right keys
            } else if (key == Keys.Left) {
                handled = LeftRight(true);

            } else if (key == Keys.Right) {
                handled = LeftRight(false);

                // enter and tab accept the current selection
            } else if (key == Keys.Enter) {
                OnActivateItem();

            } else if (key == Keys.Tab) {
                OnActivateItem();
                GiveFocustoTextBox();

                // else, any other key is unhandled
            } else {
                handled = false;
            }

            // down and up activate the display of tooltip
            if (key == Keys.Up || key == Keys.Down) {
                // TODO
                //InfoToolTip.InfoToolTip.ShowToolTipFromAutocomplete(GetCurrentSuggestion(), new Rectangle(new Point(Location.X, Location.Y), new Size(Width, Height)), _isReversed);
            }
            return handled;
        }

        private bool LeftRight(bool isLeft) {
            // Alt must be pressed
            if (!KeyInterceptor.GetModifiers().IsAlt)
                return false;

            // only 1 type is active
            if (_displayedTypes.Count(b => b.Value.Activated) == 1)
                _currentType = _displayedTypes.FindIndex(pair => pair.Value.Activated) + (isLeft ? -1 : 1);
            if (_currentType > _displayedTypes.Count - 1) _currentType = 0;
            if (_currentType < 0) _currentType = _displayedTypes.Count - 1;
            SetActiveType(new List<FileType> { _displayedTypes.ElementAt(_currentType).Key });
            ApplyFilter();
            return true;
        }

        #endregion

        #region Filter

        /// <summary>
        /// this methods sorts the items to put the best match on top and then filter it with modelFilter
        /// </summary>
        private void ApplyFilter() {
            // order the list, first the ones that are equals to the filter, then the
            // ones that start with the filter, then the rest
            if (string.IsNullOrEmpty(_filterByText)) {
                ovl.SetObjects(_initialObjectsList);
            } else {
                char firstChar = char.ToUpperInvariant(_filterByText[0]);
                ovl.SetObjects(_initialObjectsList.OrderBy(
                    x => {
                        if (x.FileName.Length < 1 || char.ToUpperInvariant(x.FileName[0]) != firstChar) return 2;
                        return x.FileName.Equals(_filterByText, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1;
                    }).ToList());
            }

            // apply the filter, need to match the filter + need to be an active type (Selector button activated)
            ovl.ModelFilter = new ModelFilter(FilterPredicate);

            ovl.DefaultRenderer = new CustomHighlightTextRenderer(_filterByText);

            // update total items
            TotalItems = ((ArrayList)ovl.FilteredObjects).Count;
            nbitems.Text = TotalItems + StrItems;

            // if the selected row is > to number of items, then there will be a unselect
            if (ovl.SelectedIndex == -1) ovl.SelectedIndex = 0;
            if (ovl.SelectedIndex >= 0)
                ovl.EnsureVisible(ovl.SelectedIndex);
        }

        /// <summary>
        /// if true, the item isn't filtered
        /// </summary>
        /// <param name="o"></param>
        /// <returns></returns>
        private static bool FilterPredicate(object o) {
            var compData = (FileObject)o;
            // check for the filter match, the activated category,
            bool output = compData.FileName.ToLower().FullyMatchFilter(_filterByText);
            if (_displayedTypes.ContainsKey(compData.Type))
                output = output && _displayedTypes[compData.Type].Activated;
            return output;
        }

        #endregion

        #region Misc

        /// <summary>
        /// Get the current selected item
        /// </summary>
        /// <returns></returns>
        public FileObject GetCurrentFile() {
            try {
                return (FileObject)ovl.SelectedItem.RowObject;
            } catch (Exception) {
                //ignored
            }
            return null;
        }

        internal void Redraw() {
            ovl.Invalidate();
        }

        /// <summary>
        /// Explicit
        /// </summary>
        public void GiveFocustoTextBox() {
            textFilter.Focus();
        }

        /// <summary>
        /// Explicit
        /// </summary>
        public void ClearFilter() {
            textFilter.Text = "";
            FilterByText = "";
        }

        #endregion

        #endregion

        #region File Explorer buttons events

        private void BtGotoDirOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            if (DirectoryToExplorer == 0)
                Utils.OpenFolder(ProgressEnv.Current.BaseLocalPath);
            else if (DirectoryToExplorer == 1)
                Utils.OpenFolder(ProgressEnv.Current.BaseCompilationPath);
        }

        private void BtDirectoryOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            if (_isListing) return;
            DirectoryToExplorer++;
            if (DirectoryToExplorer > 3) DirectoryToExplorer = 0;
            Image tryImg = (Image)ImageResources.ResourceManager.GetObject("ExplorerDir" + DirectoryToExplorer);
            btDirectory.BackGrndImage = tryImg ?? ImageResources.Error;
            btDirectory.Invalidate();
            lbDirectory.Text = _explorerDirStr[DirectoryToExplorer];
            if (DirectoryToExplorer > 1)
                btGotoDir.Hide();
            else
                btGotoDir.Show();
            RefreshOvl();
        }

        private void TextFilterOnTextChanged(object sender, EventArgs eventArgs) {
            FilterByText = textFilter.Text;
        }

        private void BtEraseOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            textFilter.Text = "";
            FilterByText = "";
        }

        private void BtRefreshOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            RefreshOvl();
        }

        private void TextFilterOnKeyDown(object sender, KeyEventArgs keyEventArgs) {
            keyEventArgs.Handled = OnKeyDown(keyEventArgs.KeyCode);
        }

        #endregion

        #region Current file

        private CurrentOperation _currentOperation;

        private void FilesInfoOnUpdatedOperation(object sender, UpdatedOperationEventArgs updatedOperationEventArgs) {

            Color endingColor;

            if (updatedOperationEventArgs.CurrentOperation == 0) {
                endingColor = ThemeManager.Current.FormColorBackColor;
            } else {
                endingColor = ThemeManager.AccentColor;
            }

            // blink back color
            if (_currentOperation != updatedOperationEventArgs.CurrentOperation) {
                lbStatus.UseCustomBackColor = true;
                Transition.run(lbStatus, "BackColor", lbStatus.BackColor, (lbStatus.BackColor == ThemeManager.Current.FormColorBackColor) ? ThemeManager.AccentColor : ThemeManager.Current.FormColorBackColor, new TransitionType_Flash(3, 300), (o, args) => {
                    lbStatus.BackColor = endingColor;
                });
            }

            // text
            foreach (var name in Enum.GetNames(typeof(CurrentOperation))) {
                CurrentOperation flag = (CurrentOperation)Enum.Parse(typeof(CurrentOperation), name);
                if (!updatedOperationEventArgs.CurrentOperation.HasFlag(flag)) continue;
                lbStatus.Text = ((CurrentOperationAttr)flag.GetAttributes()).DisplayText;
            }

            _currentOperation = updatedOperationEventArgs.CurrentOperation;

        }

        /// <summary>
        /// This method is called each time the user switches document or start a compile or ends a compile...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="updatedErrorsEventArgs"></param>
        private void FilesInfoOnUpdatedErrors(object sender, UpdatedErrorsEventArgs updatedErrorsEventArgs) {

            lbNbErrors.UseCustomBackColor = true;
            lbNbErrors.UseCustomForeColor = true;
            var t = new Transition(new TransitionType_Linear(500));

            // disable/enable buttons
            UpdateErrorButtons(updatedErrorsEventArgs.NbErrors > 0);

            // colors
            t.add(lbNbErrors, "BackColor", Style.BgErrorLevelColors[(int)updatedErrorsEventArgs.ErrorLevel]);
            t.add(lbNbErrors, "ForeColor", Style.FgErrorLevelColors[(int)updatedErrorsEventArgs.ErrorLevel]);

            // text
            t.add(lbNbErrors, "Text", updatedErrorsEventArgs.NbErrors.ToString());
            t.add(lbErrorText, "Text", ((ErrorLevelAttr)updatedErrorsEventArgs.ErrorLevel.GetAttributes()).DisplayText);

            t.run();
        }

        private void UpdateErrorButtons(bool activate) {
            btPrevError.Enabled = activate;
            btNextError.Enabled = activate;
            btClearAllErrors.Enabled = activate;
            btPrevError.BackGrndImage = activate ? ImageResources.Previous : Utils.MakeGrayscale3(ImageResources.Previous);
            btNextError.BackGrndImage = activate ? ImageResources.Next : Utils.MakeGrayscale3(ImageResources.Next);
            btClearAllErrors.BackGrndImage = activate ? ImageResources.ClearAll : Utils.MakeGrayscale3(ImageResources.ClearAll);
            btPrevError.Invalidate();
            btNextError.Invalidate();
            btClearAllErrors.Invalidate();
        }

        private void BtClearAllErrorsOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            FilesInfo.ClearAllErrors();
        }

        private void BtNextErrorOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            FilesInfo.GoToNextError(Npp.Line.CurrentLine + 1);
        }

        private void BtPrevErrorOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            FilesInfo.GoToPrevError(Npp.Line.CurrentLine - 1);
        }

        private void BtGetHelpOnButtonPressed(object sender, ButtonPressedEventArgs buttonPressedEventArgs) {
            Config.Instance.GlobalShowErrorHelp = !Config.Instance.GlobalShowErrorHelp;
            btGetHelp.BackGrndImage = (Config.Instance.GlobalShowErrorHelp) ? ImageResources.GetHelp : Utils.MakeGrayscale3(ImageResources.GetHelp);
            FilesInfo.DisplayCurrentFileInfo();
        }

        #endregion

    }

    #region sorting
    /// <summary>
    /// Class used in objectlist.Sort method
    /// </summary>
    public class FilesSortingClass : IComparer<FileObject> {
        public int Compare(FileObject x, FileObject y) {
            // first, the favourite
            int compare = x.Flags.HasFlag(FileFlag.Favourite).CompareTo(y.Flags.HasFlag(FileFlag.Favourite));
            if (compare != 0) return compare;

            // then the folders
            compare = y.Type.Equals(FileType.Folder).CompareTo(x.Type.Equals(FileType.Folder));
            if (compare != 0) return compare;

            // then the non read only
            compare = y.Flags.HasFlag(FileFlag.ReadOnly).CompareTo(x.Flags.HasFlag(FileFlag.ReadOnly));
            if (compare != 0) return compare;

            // sort by FileName
            return string.Compare(x.FileName, y.FileName, StringComparison.CurrentCultureIgnoreCase);
        }
    }
    #endregion
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Rat9Battery
{
    internal sealed class V102LightingForm : Form
    {
        private sealed class EffectChoice
        {
            public int Value { get; set; }
            public string Text { get; set; }
            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class LogoChoice
        {
            public int Value { get; set; }
            public string Text { get; set; }
            public override string ToString()
            {
                return Text;
            }
        }

        private readonly KeyboardProfileStore _store;
        private readonly V102ProfilesDocument _document;
        private readonly Func<V102KeyboardConfig> _readConfig;
        private readonly Action<V102LightingProfile> _applyProfile;
        private readonly Action _profilesChanged;

        private readonly ComboBox _profileBox =
            new ComboBox();
        private readonly TextBox _nameBox =
            new TextBox();
        private readonly CheckBox _enabledBox =
            new CheckBox();
        private readonly ComboBox _effectBox =
            new ComboBox();
        private readonly TrackBar _brightness =
            new TrackBar();
        private readonly TrackBar _speed =
            new TrackBar();
        private readonly Label _brightnessValue =
            new Label();
        private readonly Label _speedValue =
            new Label();
        private readonly ComboBox _logoBox =
            new ComboBox();
        private readonly ComboBox _breathColor =
            new ComboBox();
        private readonly ComboBox _waveColor =
            new ComboBox();
        private readonly ListBox _executables =
            new ListBox();
        private readonly Label _status =
            new Label();

        private V102LightingProfile _selected;
        private bool _loading;

        public V102LightingForm(
            KeyboardProfileStore store,
            V102ProfilesDocument document,
            Func<V102KeyboardConfig> readConfig,
            Action<V102LightingProfile> applyProfile,
            Action profilesChanged)
        {
            _store = store;
            _document = document;
            _readConfig = readConfig;
            _applyProfile = applyProfile;
            _profilesChanged = profilesChanged;

            Text = "V102 Pro · RGB och spelprofiler";
            ClientSize = new Size(660, 610);
            MinimumSize = new Size(680, 640);
            StartPosition = FormStartPosition.CenterScreen;
            ShowIcon = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(24, 29, 37);
            ForeColor = Color.FromArgb(232, 237, 244);
            Font = new Font(
                "Segoe UI",
                9f,
                FontStyle.Regular,
                GraphicsUnit.Point);

            BuildLayout();
            PopulateChoices();
            ReloadProfiles(null);
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 2,
                RowCount = 5
            };
            root.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(
                new RowStyle(SizeType.Absolute, 42));

            FlowLayoutPanel profileRow =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    WrapContents = false,
                    Padding = new Padding(0, 5, 0, 0)
                };
            _profileBox.Width = 190;
            _profileBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _profileBox.SelectedIndexChanged +=
                delegate { LoadSelectedProfile(); };
            _nameBox.Width = 190;
            profileRow.Controls.Add(_profileBox);
            profileRow.Controls.Add(_nameBox);

            Button newButton = CreateButton("Ny");
            newButton.Click += delegate { CreateProfile(); };
            Button deleteButton = CreateButton("Ta bort");
            deleteButton.Click += delegate { DeleteProfile(); };
            Button baseButton = CreateButton("Gör till bas");
            baseButton.Click += delegate { MakeBaseProfile(); };
            profileRow.Controls.Add(newButton);
            profileRow.Controls.Add(deleteButton);
            profileRow.Controls.Add(baseButton);

            GroupBox lighting = CreateGroup("Belysning");
            TableLayoutPanel lightingGrid =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    ColumnCount = 2,
                    RowCount = 7
                };
            lightingGrid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 128));
            lightingGrid.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            AddRow(lightingGrid, 0, "Aktiv", _enabledBox);
            AddRow(lightingGrid, 1, "Effekt", _effectBox);
            AddRow(
                lightingGrid,
                2,
                "Ljusstyrka",
                CreateSliderRow(
                    _brightness,
                    1,
                    5,
                    _brightnessValue));
            AddRow(
                lightingGrid,
                3,
                "Hastighet",
                CreateSliderRow(
                    _speed,
                    0,
                    4,
                    _speedValue));
            AddRow(lightingGrid, 4, "Logolampa", _logoBox);
            AddRow(lightingGrid, 5, "Andningsfärg", _breathColor);
            AddRow(lightingGrid, 6, "Flödesfärg", _waveColor);
            lighting.Controls.Add(lightingGrid);

            GroupBox games = CreateGroup("Starta med spel");
            TableLayoutPanel gameGrid =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    Padding = new Padding(10),
                    ColumnCount = 1,
                    RowCount = 2
                };
            _executables.Dock = DockStyle.Fill;
            _executables.DrawMode = DrawMode.OwnerDrawFixed;
            _executables.BackColor =
                Color.FromArgb(31, 37, 47);
            _executables.ForeColor =
                Color.FromArgb(225, 232, 241);
            _executables.DrawItem += DrawExecutableItem;
            gameGrid.Controls.Add(_executables, 0, 0);

            FlowLayoutPanel gameButtons =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    WrapContents = false
                };
            Button addButton = CreateButton("Lägg till .exe");
            addButton.Click += delegate { AddExecutable(); };
            Button removeButton = CreateButton("Ta bort vald");
            removeButton.Click += delegate
            {
                if (_executables.SelectedIndex >= 0)
                {
                    _executables.Items.RemoveAt(
                        _executables.SelectedIndex);
                }
            };
            gameButtons.Controls.Add(addButton);
            gameButtons.Controls.Add(removeButton);
            gameGrid.Controls.Add(gameButtons, 0, 1);
            games.Controls.Add(gameGrid);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 8, 0, 0)
            };
            Button applyButton = CreateButton("Spara och applicera");
            applyButton.Click += delegate { SaveAndApply(); };
            Button readButton = CreateButton("Läs från tangentbord");
            readButton.Click += delegate { ReadCurrent(); };
            actions.Controls.Add(applyButton);
            actions.Controls.Add(readButton);

            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Color.FromArgb(154, 167, 184);
            _status.Text =
                "Profiler sparas lokalt. Ingen EWEADN- eller molnkontoserver används.";

            root.Controls.Add(profileRow, 0, 0);
            root.SetColumnSpan(profileRow, 2);
            root.Controls.Add(lighting, 0, 1);
            root.SetRowSpan(lighting, 2);
            root.Controls.Add(games, 1, 1);
            root.SetRowSpan(games, 2);
            root.Controls.Add(actions, 0, 3);
            root.SetColumnSpan(actions, 2);
            root.Controls.Add(_status, 0, 4);
            root.SetColumnSpan(_status, 2);
            Controls.Add(root);
        }

        private void PopulateChoices()
        {
            ApplyInputTheme(_profileBox);
            ApplyInputTheme(_nameBox);
            ApplyInputTheme(_effectBox);
            ApplyInputTheme(_logoBox);
            ApplyInputTheme(_breathColor);
            ApplyInputTheme(_waveColor);
            _enabledBox.ForeColor =
                Color.FromArgb(225, 232, 241);

            _effectBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _effectBox.Items.AddRange(new object[]
            {
                new EffectChoice { Value = 0, Text = "0 · Andning" },
                new EffectChoice { Value = 1, Text = "1 · Regnbåge" },
                new EffectChoice { Value = 2, Text = "2 · Löpande ljus" },
                new EffectChoice { Value = 3, Text = "3 · Tidvatten" },
                new EffectChoice { Value = 4, Text = "4 · Färgflöde" },
                new EffectChoice { Value = 5, Text = "5 · Krusning" },
                new EffectChoice { Value = 6, Text = "6 · Trefärg" },
                new EffectChoice { Value = 7, Text = "7 · Kollision" },
                new EffectChoice { Value = 16, Text = "Fast röd" },
                new EffectChoice { Value = 17, Text = "Fast orange" },
                new EffectChoice { Value = 18, Text = "Fast gul" },
                new EffectChoice { Value = 19, Text = "Fast grön" },
                new EffectChoice { Value = 20, Text = "Fast cyan" },
                new EffectChoice { Value = 21, Text = "Fast blå" },
                new EffectChoice { Value = 22, Text = "Fast lila" },
                new EffectChoice { Value = 23, Text = "Fast vit" },
                new EffectChoice { Value = 24, Text = "Fast flerfärg" }
            });

            LogoChoice[] logos =
            {
                new LogoChoice { Value = 0, Text = "Synkad med belysningen" },
                new LogoChoice { Value = 1, Text = "Röd" },
                new LogoChoice { Value = 2, Text = "Grön" },
                new LogoChoice { Value = 3, Text = "Blå" },
                new LogoChoice { Value = 4, Text = "Gul" },
                new LogoChoice { Value = 5, Text = "Cyan" },
                new LogoChoice { Value = 6, Text = "Lila" },
                new LogoChoice { Value = 7, Text = "Vit" },
                new LogoChoice { Value = 9, Text = "Av" }
            };
            _logoBox.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _logoBox.Items.AddRange(logos);

            string[] colors =
            {
                "Automatisk",
                "Röd",
                "Grön",
                "Blå",
                "Gul",
                "Cyan",
                "Lila",
                "Vit"
            };
            _breathColor.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _waveColor.DropDownStyle =
                ComboBoxStyle.DropDownList;
            _breathColor.Items.AddRange(colors);
            _waveColor.Items.AddRange(colors);
            _brightness.Scroll += delegate
            {
                _brightnessValue.Text =
                    _brightness.Value.ToString();
            };
            _speed.Scroll += delegate
            {
                _speedValue.Text = _speed.Value.ToString();
            };
        }

        private static void ApplyInputTheme(Control control)
        {
            control.BackColor = Color.FromArgb(31, 37, 47);
            control.ForeColor = Color.FromArgb(232, 237, 244);
            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                comboBox.FlatStyle = FlatStyle.Flat;
            }
        }

        private void ReloadProfiles(string selectedId)
        {
            _loading = true;
            _profileBox.Items.Clear();
            foreach (V102LightingProfile profile in
                _document.Profiles)
            {
                _profileBox.Items.Add(profile);
            }

            int index = 0;
            if (!string.IsNullOrEmpty(selectedId))
            {
                for (int candidate = 0;
                     candidate < _document.Profiles.Count;
                     candidate++)
                {
                    if (string.Equals(
                        _document.Profiles[candidate].Id,
                        selectedId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        index = candidate;
                        break;
                    }
                }
            }

            if (_profileBox.Items.Count > 0)
            {
                _profileBox.SelectedIndex = index;
            }
            _loading = false;
            LoadSelectedProfile();
        }

        private void LoadSelectedProfile()
        {
            if (_loading)
            {
                return;
            }

            _selected =
                _profileBox.SelectedItem as
                    V102LightingProfile;
            if (_selected == null)
            {
                return;
            }

            _loading = true;
            try
            {
                _nameBox.Text = _selected.Name;
                _enabledBox.Checked = _selected.Enabled;
                SelectEffect(_selected.EffectMode);
                _brightness.Value = Math.Max(
                    _brightness.Minimum,
                    Math.Min(
                        _brightness.Maximum,
                        _selected.Brightness));
                _speed.Value = Math.Max(
                    _speed.Minimum,
                    Math.Min(
                        _speed.Maximum,
                        _selected.Speed));
                _brightnessValue.Text =
                    _brightness.Value.ToString();
                _speedValue.Text = _speed.Value.ToString();
                SelectLogo(_selected.LogoMode);
                _breathColor.SelectedIndex = Math.Max(
                    0,
                    Math.Min(
                        _breathColor.Items.Count - 1,
                        _selected.BreathColor));
                _waveColor.SelectedIndex = Math.Max(
                    0,
                    Math.Min(
                        _waveColor.Items.Count - 1,
                        _selected.WaveColor));
                _executables.Items.Clear();
                foreach (string executable in
                    _selected.Executables)
                {
                    _executables.Items.Add(executable);
                }

                int missing = 0;
                foreach (object executable in
                    _executables.Items)
                {
                    string path = Convert.ToString(executable);
                    if (!File.Exists(path))
                    {
                        missing++;
                    }
                }

                SetStatus(missing == 0
                    ? "Alla spelvägar finns."
                    : missing + " spelväg(ar) saknas och visas rött.");
            }
            finally
            {
                _loading = false;
            }
        }

        private void DrawExecutableItem(
            object sender,
            DrawItemEventArgs args)
        {
            if (args.Index < 0 ||
                args.Index >= _executables.Items.Count)
            {
                return;
            }

            args.DrawBackground();
            string path = Convert.ToString(
                _executables.Items[args.Index]);
            Color color = File.Exists(path)
                ? Color.FromArgb(225, 232, 241)
                : Color.FromArgb(238, 105, 105);
            using (SolidBrush brush = new SolidBrush(color))
            {
                args.Graphics.DrawString(
                    path,
                    args.Font,
                    brush,
                    args.Bounds);
            }
            args.DrawFocusRectangle();
        }

        private void SaveAndApply()
        {
            V102LightingProfile profile =
                CaptureSelectedProfile();
            if (profile == null)
            {
                return;
            }

            _store.Save(_document);
            _profilesChanged();
            SetStatus("Sparar och skickar belysningen...");
            V102LightingProfile copy = profile.Clone();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _applyProfile(copy);
                    SetStatus("Belysningen är skickad till V102.");
                }
                catch (Exception exception)
                {
                    SetStatus(
                        "Kunde inte skicka belysningen: " +
                        exception.Message);
                }
            });
        }

        private V102LightingProfile CaptureSelectedProfile()
        {
            if (_selected == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(
                    this,
                    "Profilen behöver ett namn.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            _selected.Name = _nameBox.Text.Trim();
            _selected.Enabled = _enabledBox.Checked;
            EffectChoice effect =
                _effectBox.SelectedItem as EffectChoice;
            _selected.EffectMode =
                effect == null ? 4 : effect.Value;
            _selected.Brightness = _brightness.Value;
            _selected.Speed = _speed.Value;
            LogoChoice logo =
                _logoBox.SelectedItem as LogoChoice;
            _selected.LogoMode =
                logo == null ? 0 : logo.Value;
            _selected.BreathColor = Math.Max(
                0,
                _breathColor.SelectedIndex);
            _selected.WaveColor = Math.Max(
                0,
                _waveColor.SelectedIndex);
            _selected.Executables =
                new List<string>();
            foreach (object item in _executables.Items)
            {
                _selected.Executables.Add(
                    Convert.ToString(item));
            }

            return _selected;
        }

        private void CreateProfile()
        {
            V102LightingProfile profile =
                KeyboardProfileStore.CreateDefaultProfile();
            profile.Id = Guid.NewGuid().ToString("N");
            profile.Name = "Ny profil";
            _document.Profiles.Add(profile);
            _store.Save(_document);
            _profilesChanged();
            ReloadProfiles(profile.Id);
        }

        private void DeleteProfile()
        {
            if (_selected == null)
            {
                return;
            }

            if (string.Equals(
                _selected.Id,
                _document.BaseProfileId,
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    "Välj en annan basprofil först.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    this,
                    "Ta bort profilen \"" +
                    _selected.Name + "\"?",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _document.Profiles.Remove(_selected);
            _store.Save(_document);
            _profilesChanged();
            ReloadProfiles(_document.BaseProfileId);
        }

        private void MakeBaseProfile()
        {
            if (_selected == null)
            {
                return;
            }

            _document.BaseProfileId = _selected.Id;
            _store.Save(_document);
            _profilesChanged();
            SetStatus("Basprofil: " + _selected.Name);
        }

        private void AddExecutable()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Välj spelprogram";
                dialog.Filter =
                    "Program (*.exe)|*.exe|Alla filer (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) !=
                    DialogResult.OK)
                {
                    return;
                }

                string path = Path.GetFullPath(
                    dialog.FileName);
                if (!_executables.Items.Contains(path))
                {
                    _executables.Items.Add(path);
                }
            }
        }

        private void ReadCurrent()
        {
            SetStatus("Läser belysningen från V102...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    V102KeyboardConfig config = _readConfig();
                    V102LightingProfile current =
                        V102LightingService.FromCurrent(config);
                    BeginInvoke(new Action(delegate
                    {
                        if (_selected == null)
                        {
                            return;
                        }

                        _selected.Enabled = current.Enabled;
                        _selected.EffectMode = current.EffectMode;
                        _selected.Brightness = current.Brightness;
                        _selected.Speed = current.Speed;
                        _selected.LogoMode = current.LogoMode;
                        _selected.BreathColor =
                            current.BreathColor;
                        _selected.WaveColor = current.WaveColor;
                        LoadSelectedProfile();
                        SetStatus(
                            "Nuvarande belysning lästes från V102.");
                    }));
                }
                catch (Exception exception)
                {
                    SetStatus(
                        "Kunde inte läsa belysningen: " +
                        exception.Message);
                }
            });
        }

        private void SelectEffect(int value)
        {
            for (int index = 0;
                 index < _effectBox.Items.Count;
                 index++)
            {
                EffectChoice choice =
                    _effectBox.Items[index] as EffectChoice;
                if (choice != null && choice.Value == value)
                {
                    _effectBox.SelectedIndex = index;
                    return;
                }
            }
        }

        private void SelectLogo(int value)
        {
            for (int index = 0;
                 index < _logoBox.Items.Count;
                 index++)
            {
                LogoChoice choice =
                    _logoBox.Items[index] as LogoChoice;
                if (choice != null && choice.Value == value)
                {
                    _logoBox.SelectedIndex = index;
                    return;
                }
            }
        }

        private void SetStatus(string message)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(SetStatus), message);
                }
                catch
                {
                    // The form is closing.
                }
                return;
            }

            _status.Text = message;
        }

        private static void AddRow(
            TableLayoutPanel table,
            int row,
            string label,
            Control control)
        {
            table.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100));
            table.Controls.Add(
                new Label
                {
                    Text = label,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    ForeColor = Color.FromArgb(182, 193, 208)
                },
                0,
                row);
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 1, row);
        }

        private static Control CreateSliderRow(
            TrackBar slider,
            int minimum,
            int maximum,
            Label valueLabel)
        {
            TableLayoutPanel panel =
                new TableLayoutPanel
                {
                    ColumnCount = 2,
                    RowCount = 1,
                    Dock = DockStyle.Fill
                };
            panel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 30));
            slider.Minimum = minimum;
            slider.Maximum = maximum;
            slider.TickStyle = TickStyle.None;
            slider.Dock = DockStyle.Fill;
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.TextAlign = ContentAlignment.MiddleCenter;
            panel.Controls.Add(slider, 0, 0);
            panel.Controls.Add(valueLabel, 1, 0);
            return panel;
        }

        private static GroupBox CreateGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(201, 210, 222),
                Padding = new Padding(8)
            };
        }

        private static Button CreateButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(42, 49, 61),
                ForeColor = Color.White,
                Margin = new Padding(4)
            };
        }
    }
}

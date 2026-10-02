using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.GameContent;
using DynamicWorlds;

namespace DynamicWorlds.UI
{
    internal class BiomeDowserSettingsUI : UIState
    {
        private UIPanel _panel;
        private UIPanel _listContainer;
        private UIList _pylonList;
        private UIPanel _detailPanel;
        private UIText _title;
        private TeleportPylonType? _selected;

        public override void OnInitialize()
        {
            _panel = new UIPanel
            {
                HAlign = 0.5f,
                VAlign = 0.5f,
                PaddingTop = 16f,
                PaddingBottom = 16f,
                PaddingLeft = 16f,
                PaddingRight = 16f,
                BackgroundColor = new Color(33, 40, 60) * 0.95f,
            };
            _panel.Width.Set(1140f, 0f);
            _panel.Height.Set(780f, 0f);

            _title = new UIText("Biome Dowser Settings", 0.9f, true)
            {
                HAlign = 0.5f,
            };
            _title.Top.Set(-4f, 0f);
            _panel.Append(_title);

            var closeButton = new UITextPanel<string>("Close", 0.85f, true)
            {
                HAlign = 1f,
                BackgroundColor = new Color(70, 90, 130) * 0.9f,
            };
            closeButton.Width.Set(96f, 0f);
            closeButton.Height.Set(36f, 0f);
            closeButton.Top.Set(-6f, 0f);
            closeButton.OnLeftClick += (_, __) => BiomeDowserSettingsSystem.ToggleUI();
            _panel.Append(closeButton);

            _listContainer = new UIPanel
            {
                PaddingTop = 6f,
                PaddingBottom = 6f,
                PaddingLeft = 6f,
                PaddingRight = 6f,
                BackgroundColor = new Color(28, 34, 52) * 0.95f,
                BorderColor = new Color(70, 90, 130) * 0.85f,
            };
            _listContainer.Width.Set(520f, 0f);
            _listContainer.Height.Set(-70f, 1f);
            _listContainer.Top.Set(44f, 0f);
            _listContainer.Left.Set(0f, 0f);

            _pylonList = new UIList
            {
            };
            _pylonList.Width.Set(0f, 1f);
            _pylonList.Height.Set(0f, 1f);
            _listContainer.Append(_pylonList);

            var scroll = new UIScrollbar
            {
                HAlign = 1f,
                VAlign = 0f,
            };
            scroll.Height.Set(0f, 1f);
            scroll.SetView(100f, 1000f);
            _pylonList.SetScrollbar(scroll);
            _listContainer.Append(scroll);
            _panel.Append(_listContainer);

            _detailPanel = new UIPanel
            {
                PaddingTop = 10f,
                PaddingBottom = 10f,
                PaddingLeft = 10f,
                PaddingRight = 10f,
                BackgroundColor = new Color(25, 30, 50) * 0.92f,
                BorderColor = new Color(70, 90, 130) * 0.8f,
            };
            // Fill remaining width after the left list with a small gap.
            float detailLeft = _listContainer.Width.Pixels + 20f;
            _detailPanel.Left.Set(detailLeft, 0f);
            _detailPanel.Width.Set(-(detailLeft + 24f), 1f); // leave a bit of breathing room at the right edge
            _detailPanel.Height.Set(-70f, 1f);
            _detailPanel.Top.Set(44f, 0f);
            // Only append when a selection is made.

            Append(_panel);
        }

        public void Rebuild()
        {
            _pylonList.Clear();
            var player = Main.LocalPlayer?.GetModPlayer<BiomeDowserPlayer>();
            if (player == null)
                return;

            var pylonTypes = Enum.GetValues(typeof(TeleportPylonType)).Cast<TeleportPylonType>()
                .Where(t => t != TeleportPylonType.Count)
                .ToArray();

            foreach (var pylonType in pylonTypes)
            {
                _pylonList.Add(new PylonListRow(pylonType, () => ShowDetail(pylonType)));
            }

            HideDetail();
        }

        public void ResetView()
        {
            _pylonList?.Clear();
            HideDetail();
        }

        private void ShowDetail(TeleportPylonType pylonType)
        {
            _selected = pylonType;
            _detailPanel.RemoveAllChildren();
            if (_detailPanel.Parent == null)
                _panel.Append(_detailPanel);

            var player = Main.LocalPlayer?.GetModPlayer<BiomeDowserPlayer>();
            if (player == null)
                return;

            var header = new UIText(pylonType.ToString(), 0.9f, true)
            {
                HAlign = 0.5f,
            };
            header.Top.Set(-2f, 0f);
            _detailPanel.Append(header);


            var content = new DetailContent(pylonType, player)
            {
            };
            content.Width.Set(-16f, 1f);
            content.Height.Set(-60f, 1f);
            content.Top.Set(44f, 0f);
            content.Left.Set(8f, 0f);
            _detailPanel.Append(content);
        }

        private void HideDetail()
        {
            _selected = null;
            _detailPanel?.RemoveAllChildren();
            if (_detailPanel?.Parent != null)
                _detailPanel.Remove();
        }

        private class PylonListRow : UITextPanel<string>
        {
            private readonly Action _onSelect;

            public PylonListRow(TeleportPylonType type, Action onSelect) : base(type.ToString(), 0.9f, true)
            {
                _onSelect = onSelect;

                Width.Set(0f, 1f);
                Height.Set(44f, 0f);
                BackgroundColor = new Color(60, 75, 110) * 0.9f;
                BorderColor = new Color(90, 120, 170) * 0.9f;
                PaddingTop = 10f;
                PaddingBottom = 10f;
                PaddingLeft = 12f;
                PaddingRight = 12f;

                OnLeftClick += (_, __) => _onSelect?.Invoke();
                OnMouseOver += (_, __) => BackgroundColor = new Color(80, 105, 155) * 0.95f;
                OnMouseOut += (_, __) => BackgroundColor = new Color(60, 75, 110) * 0.9f;
            }
        }

        private class DetailContent : UIPanel
        {
            private enum PlacementOption
            {
                Surface,
                Underground,
                Floating,
                SkyIsland,
                Boat,
                Submarine,
                OceanFloor,
                DryBeach,
                Aether,
            }

            private readonly TeleportPylonType _pylonType;
            private readonly BiomeDowserPlayer _player;
            private readonly UIList _stack;
            private readonly UIScrollbar _scrollbar;
            private readonly System.Collections.Generic.List<UITextPanel<string>> _placementButtons = new();
            private PlacementOption _selectedOption;
            private PlacementOption[] _options;

            public DetailContent(TeleportPylonType type, BiomeDowserPlayer player)
            {
                _pylonType = type;
                _player = player;

                BackgroundColor = new Color(45, 55, 80) * 0.9f;
                PaddingLeft = 16f;
                PaddingRight = 16f;
                PaddingTop = 16f;
                PaddingBottom = 16f;

                _stack = new UIList
                {
                    PaddingTop = 4f,
                    PaddingBottom = 4f,
                    ListPadding = 10f,
                };
                _stack.Width.Set(-22f, 1f); // make room for scrollbar
                _stack.Height.Set(0f, 1f);
                Append(_stack);

                _scrollbar = new UIScrollbar
                {
                    HAlign = 1f,
                    VAlign = 0f,
                };
                _scrollbar.Width.Set(18f, 0f);
                _scrollbar.Height.Set(0f, 1f);
                _stack.SetScrollbar(_scrollbar);
                Append(_scrollbar);

                BuildPlacementOptions();
                UpdateDisplay();
            }

            private void UpdateDisplay()
            {
                BiomeDowserPylonPreferences prefs = _player.GetPreferredPreferences(_pylonType);
                _selectedOption = DetermineSelectedOption(prefs);
                if (_options.Length > 0 && !_options.Contains(_selectedOption))
                    _selectedOption = _options[0];
                RefreshPlacementButtons();
                _stack.Recalculate();
            }

            private void BuildPlacementOptions()
            {
                var strategy = BiomeDowserPlacementHelper.GetRegenStrategy(_pylonType);
                var options = new System.Collections.Generic.List<PlacementOption>();

                if (strategy.Modes.Contains(BiomeDowserPlacementMode.Surface))
                    options.Add(PlacementOption.Surface);
                if (strategy.Modes.Contains(BiomeDowserPlacementMode.Underground))
                    options.Add(PlacementOption.Underground);
                if (strategy.Modes.Contains(BiomeDowserPlacementMode.Floating))
                    options.Add(PlacementOption.Floating);

                if (strategy.SupportsSkyIsland)
                    options.Add(PlacementOption.SkyIsland);
                if (strategy.SupportsAether)
                    options.Add(PlacementOption.Aether);
                if (strategy.OceanPlacements != null && strategy.OceanPlacements.Length > 0)
                {
                    if (strategy.OceanPlacements.Contains(BiomeDowserOceanPlacement.OceanFloor))
                        options.Add(PlacementOption.OceanFloor);
                    if (strategy.OceanPlacements.Contains(BiomeDowserOceanPlacement.DryBeach))
                        options.Add(PlacementOption.DryBeach);
                    if (strategy.OceanPlacements.Contains(BiomeDowserOceanPlacement.Boat))
                        options.Add(PlacementOption.Boat);
                    if (strategy.OceanPlacements.Contains(BiomeDowserOceanPlacement.Submarine))
                        options.Add(PlacementOption.Submarine);
                }

                _options = options.ToArray();
                _stack.Clear();
                _placementButtons.Clear();

                foreach (var opt in _options)
                {
                    string label = GetOptionLabel(opt);
                    var btn = new UITextPanel<string>(label, 0.9f, true)
                    {
                        BackgroundColor = new Color(60, 75, 110) * 0.9f,
                        BorderColor = new Color(90, 120, 170) * 0.9f,
                    };
                    btn.Width.Set(0f, 1f);
                    btn.Height.Set(32f, 0f);
                    btn.OnLeftClick += (_, __) => SelectOption(opt);
                    btn.OnMouseOver += (_, __) => btn.BackgroundColor = new Color(80, 105, 155) * 0.95f;
                    btn.OnMouseOut += (_, __) => btn.BackgroundColor = (_selectedOption == opt ? new Color(110, 140, 190) * 0.95f : new Color(60, 75, 110) * 0.9f);
                    _stack.Add(btn);
                    _placementButtons.Add(btn);
                }
                _stack.Recalculate();
            }

            private void RefreshPlacementButtons()
            {
                for (int i = 0; i < _placementButtons.Count; i++)
                {
                    var btn = _placementButtons[i];
                    var opt = _options[i];
                    btn.BackgroundColor = opt == _selectedOption ? new Color(110, 140, 190) * 0.95f : new Color(60, 75, 110) * 0.9f;
                }

                _stack.Recalculate();
            }

            private void SelectOption(PlacementOption option)
            {
                var prefs = _player.GetPreferredPreferences(_pylonType);
                switch (option)
                {
                    case PlacementOption.Surface:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.PreferSkyIslandSurface = false;
                        prefs.PreferAetherCavern = false;
                        break;
                    case PlacementOption.Underground:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Underground;
                        prefs.PreferAetherCavern = false;
                        prefs.PreferSkyIslandSurface = false;
                        break;
                    case PlacementOption.Floating:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Floating;
                        prefs.PreferSkyIslandSurface = false;
                        prefs.PreferAetherCavern = false;
                        break;
                    case PlacementOption.SkyIsland:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.PreferSkyIslandSurface = true;
                        prefs.PreferAetherCavern = false;
                        break;
                    case PlacementOption.Aether:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Underground;
                        prefs.PreferAetherCavern = true;
                        prefs.PreferSkyIslandSurface = false;
                        break;
                    case PlacementOption.OceanFloor:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.OceanPlacement = BiomeDowserOceanPlacement.OceanFloor;
                        break;
                    case PlacementOption.DryBeach:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.OceanPlacement = BiomeDowserOceanPlacement.DryBeach;
                        break;
                    case PlacementOption.Boat:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.OceanPlacement = BiomeDowserOceanPlacement.Boat;
                        break;
                    case PlacementOption.Submarine:
                        prefs.PlacementMode = BiomeDowserPlacementMode.Surface;
                        prefs.OceanPlacement = BiomeDowserOceanPlacement.Submarine;
                        break;
                }

                _player.SetPreferredPreferences(_pylonType, prefs);
                _selectedOption = option;
                RefreshPlacementButtons();
                UpdateDisplay();
            }

            private PlacementOption DetermineSelectedOption(BiomeDowserPylonPreferences prefs)
            {
                if (_pylonType == TeleportPylonType.Beach)
                {
                    return prefs.OceanPlacement switch
                    {
                        BiomeDowserOceanPlacement.DryBeach => PlacementOption.DryBeach,
                        BiomeDowserOceanPlacement.Boat => PlacementOption.Boat,
                        BiomeDowserOceanPlacement.Submarine => PlacementOption.Submarine,
                        _ => PlacementOption.OceanFloor,
                    };
                }

                if (prefs.PreferSkyIslandSurface)
                    return PlacementOption.SkyIsland;
                if (prefs.PreferAetherCavern)
                    return PlacementOption.Aether;

                return prefs.PlacementMode switch
                {
                    BiomeDowserPlacementMode.Underground => PlacementOption.Underground,
                    BiomeDowserPlacementMode.Floating => PlacementOption.Floating,
                    _ => PlacementOption.Surface,
                };
            }

            private static string GetOptionLabel(PlacementOption option) => option switch
            {
                PlacementOption.Surface => "Surface",
                PlacementOption.Underground => "Underground",
                PlacementOption.Floating => "Floating",
                PlacementOption.SkyIsland => "Sky island",
                PlacementOption.Boat => "Boat",
                PlacementOption.Submarine => "Submarine",
                PlacementOption.OceanFloor => "Ocean floor",
                PlacementOption.DryBeach => "Dry beach",
                PlacementOption.Aether => "Aether",
                _ => option.ToString(),
            };
        }
    }

    internal class BiomeDowserSettingsSystem : ModSystem
    {
        private UserInterface _ui;
        private BiomeDowserSettingsUI _state;
        private bool _visible;

        public static bool IsVisible =>
            !Main.dedServ && ModContent.GetInstance<BiomeDowserSettingsSystem>()._visible;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _ui = new UserInterface();
            _state = new BiomeDowserSettingsUI();
            _state.Activate();
        }

        public override void Unload()
        {
            _ui = null;
            _state = null;
        }

        public static void ToggleUI()
        {
            if (Main.dedServ)
                return;

            var system = ModContent.GetInstance<BiomeDowserSettingsSystem>();
            system._visible = !system._visible;
            if (system._visible)
            {
                system._state.Rebuild();
                system._ui?.SetState(system._state);
            }
            else
            {
                system._state?.ResetView();
                system._ui?.SetState(null);
            }
        }

        public override void UpdateUI(GameTime gameTime)
        {
            if (_visible)
                _ui?.Update(gameTime);
        }

        public override void ModifyInterfaceLayers(System.Collections.Generic.List<GameInterfaceLayer> layers)
        {
            if (!_visible)
                return;

            int mouseTextIndex = layers.FindIndex(layer => layer.Name.Equals("Vanilla: Mouse Text"));
            if (mouseTextIndex != -1)
            {
                layers.Insert(mouseTextIndex, new LegacyGameInterfaceLayer(
                    "DynamicWorlds: BiomeDowserSettings",
                    () =>
                    {
                        _ui?.Draw(Main.spriteBatch, new GameTime());
                        return true;
                    },
                    InterfaceScaleType.UI));
            }
        }
    }
}

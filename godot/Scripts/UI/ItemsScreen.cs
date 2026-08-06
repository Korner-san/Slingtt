using System;
using System.Collections.Generic;
using Godot;
using Slingtt.Game;

namespace Slingtt.Render;

// Prompt 9 — the three missing screens (Gacha, Upgrade, Evolution) as one
// screen with internal navigation, since they're really one "manage your
// gear" flow sharing the same weapon/armor tab. Rebuilds its content tree on
// every state change (tab switch, pull, enhance) rather than doing granular
// updates — this is a menu, not a per-frame battle view, so the simplicity is
// worth far more than the (imperceptible) rebuild cost.
public sealed partial class ItemsScreen : Control
{
    private enum Page { Gacha, Upgrade, Evolution }

    private GameRoot _game = null!;
    private Page _page = Page.Gacha;
    private GachaTab _tab = GachaTab.Weapon;
    private GachaTier _tier = GachaTier.Tier1;

    private Control _mainLayer = null!;
    private Control _overlayLayer = null!;

    // --- staged reveal state --------------------------------------------------
    private sealed class RevealRow
    {
        public PullResult Result = null!;
        public double RevealAtSeconds;
        public bool Revealed;
        public Label MysteryLabel = null!;
        public Control RevealedContent = null!;
    }

    private readonly List<RevealRow> _revealRows = new();
    private double _revealClock;
    private bool _revealAllShown;
    private const double RevealStaggerSeconds = 0.5;
    private const double RevealHoldAfterLastSeconds = 0.4;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        _game = GameRoot.Instance;
        if (_game is null || _game.FatalError is not null)
        {
            GD.PushError("[Slingtt] items screen entered without loaded content; returning to menu");
            GameRoot.Instance?.GoToMenu();
            return;
        }

        _page = DevCapture.ItemsPage switch
        {
            "upgrade" => Page.Upgrade,
            "evolution" => Page.Evolution,
            _ => Page.Gacha,
        };

        AddChild(UiKit.Backdrop(Palette.UiBg));
        _mainLayer = new Control();
        _mainLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainLayer);

        _overlayLayer = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlayLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_overlayLayer);

        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (_revealRows.Count == 0)
        {
            return;
        }
        _revealClock += delta;
        foreach (RevealRow row in _revealRows)
        {
            if (!row.Revealed && _revealClock >= row.RevealAtSeconds)
            {
                row.Revealed = true;
                row.MysteryLabel.Visible = false;
                row.RevealedContent.Visible = true;
                bool legendary = row.Result.Item?.Rarity == "Legendary";
                _game.Sfx.Play(legendary ? SfxId.Ultimate : (row.Result.WasDuplicate ? SfxId.Heal : SfxId.UiTap));
            }
        }
        if (!_revealAllShown && _revealClock >= LastRevealAt() + RevealHoldAfterLastSeconds)
        {
            _revealAllShown = true;
            ShowRevealContinueButton();
        }
    }

    private double LastRevealAt()
    {
        double max = 0;
        foreach (RevealRow row in _revealRows)
        {
            max = Math.Max(max, row.RevealAtSeconds);
        }
        return max;
    }

    // --- top-level layout ------------------------------------------------------

    private void Rebuild()
    {
        foreach (Node child in _mainLayer.GetChildren())
        {
            _mainLayer.RemoveChild(child);
            child.QueueFree();
        }

        MarginContainer safe = UiKit.SafeArea(16);
        _mainLayer.AddChild(safe);

        VBoxContainer root = UiKit.VBox(10);
        safe.AddChild(root);

        root.AddChild(BuildHeader());
        root.AddChild(BuildPageTabs());
        root.AddChild(BuildSubTabs());

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        root.AddChild(scroll);

        VBoxContainer content = UiKit.VBox(10);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);

        switch (_page)
        {
            case Page.Gacha:
                BuildGachaPage(content);
                break;
            case Page.Upgrade:
                BuildUpgradePage(content);
                break;
            case Page.Evolution:
                BuildEvolutionPage(content);
                break;
        }
    }

    private Control BuildHeader()
    {
        HBoxContainer header = UiKit.HBox(10);
        Label title = UiKit.MakeLabel("ARMORY", 30, Palette.UiText);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);

        Button back = UiKit.MakeButton("BACK", Palette.UiSurfaceRaised, primary: false, fontSize: 18);
        back.CustomMinimumSize = new Vector2(96, UiKit.TouchMin);
        back.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            _game.GoToMenu();
        };
        header.AddChild(back);
        return header;
    }

    private Control BuildPageTabs()
    {
        HBoxContainer row = UiKit.HBox(6);
        row.AddChild(PageTabButton("GACHA", Page.Gacha));
        row.AddChild(PageTabButton("UPGRADE", Page.Upgrade));
        row.AddChild(PageTabButton("EVOLUTION", Page.Evolution));
        return row;
    }

    private Button PageTabButton(string text, Page page)
    {
        bool active = _page == page;
        Button b = UiKit.MakeButton(text, active ? Palette.TeamHero : Palette.UiSurfaceRaised, primary: active, fontSize: 17);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            _page = page;
            Rebuild();
        };
        return b;
    }

    private Control BuildSubTabs()
    {
        HBoxContainer row = UiKit.HBox(6);
        row.AddChild(TabButton("WEAPON", GachaTab.Weapon));
        row.AddChild(TabButton("ARMOR", GachaTab.Armor));
        return row;
    }

    private Button TabButton(string text, GachaTab tab)
    {
        bool active = _tab == tab;
        Button b = UiKit.MakeButton(text, active ? Palette.TeamHero : Palette.UiSurfaceRaised, primary: active, fontSize: 16);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.CustomMinimumSize = new Vector2(0, 44);
        b.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            _tab = tab;
            Rebuild();
        };
        return b;
    }

    /// <summary>NameKey/Rarity for an owned item's template, regardless of tab.</summary>
    private (string NameKey, string Rarity) TemplateInfo(string templateId)
    {
        if (_tab == GachaTab.Weapon && _game.Content.Weapons.TryGetValue(templateId, out WeaponDef? w))
        {
            return (w.NameKey, w.Rarity);
        }
        if (_tab == GachaTab.Armor && _game.Content.Armor.TryGetValue(templateId, out ArmorDef? a))
        {
            return (a.NameKey, a.Rarity);
        }
        return (templateId, "Common");
    }

    // --- Gacha page --------------------------------------------------------

    private void BuildGachaPage(VBoxContainer content)
    {
        GachaTabState state = _game.Run.GachaTabStateOf(_tab);
        GachaBalance gacha = _game.Content.Balance.Gacha;

        PanelContainer summary = UiKit.MakePanel();
        VBoxContainer sv = UiKit.VBox(6);
        summary.AddChild(sv);
        sv.AddChild(UiKit.MakeLabel($"{_tab.ToString().ToUpperInvariant()} ESSENCE: {UiKit.FormatNumber(_game.Run.EssenceBalanceOf(_tab))}", 16, Palette.UiText));
        sv.AddChild(UiKit.MakeLabel($"PITY: {state.PullsSinceLegendary} / {gacha.PityPullsForLegendary}", 15, Palette.RarityLegendary));
        content.AddChild(summary);

        // Prompt 9 — piece meters are permanently visible, regardless of which
        // tier is currently selected below.
        PanelContainer pieces = UiKit.MakePanel();
        VBoxContainer pv = UiKit.VBox(4);
        pieces.AddChild(pv);
        pv.AddChild(UiKit.MakeLabel("PIECES", 13, Palette.UiTextDim));
        pv.AddChild(UiKit.MakeLabel($"Tier 2 Pieces: {state.Tier2Pieces}/{gacha.PiecesPerToken}   (Tokens: {state.Tier2Tokens})", 15, Palette.UiText));
        pv.AddChild(UiKit.MakeLabel($"Tier 3 Pieces: {state.Tier3Pieces}/{gacha.PiecesPerToken}   (Tokens: {state.Tier3Tokens})", 15, Palette.UiText));
        content.AddChild(pieces);

        HBoxContainer tierRow = UiKit.HBox(6);
        tierRow.AddChild(TierButton(GachaTier.Tier1));
        tierRow.AddChild(TierButton(GachaTier.Tier2));
        tierRow.AddChild(TierButton(GachaTier.Tier3));
        content.AddChild(tierRow);

        content.AddChild(BuildRateTable(gacha.TierRates[(int)_tier]));
        content.AddChild(BuildPullButton(state, gacha));
        content.AddChild(BuildAdSection());
    }

    private Button TierButton(GachaTier tier)
    {
        bool active = _tier == tier;
        string label = tier switch { GachaTier.Tier1 => "TIER 1", GachaTier.Tier2 => "TIER 2", _ => "TIER 3" };
        Button b = UiKit.MakeButton(label, active ? Palette.RarityEpic : Palette.UiSurfaceRaised, primary: active, fontSize: 15);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            _tier = tier;
            Rebuild();
        };
        return b;
    }

    /// <summary>Prompt 9 — "full rate tables visible": every rarity in the
    /// currently selected tier's table, no odds hidden.</summary>
    private static Control BuildRateTable(RarityRateTable table)
    {
        PanelContainer panel = UiKit.MakePanel();
        VBoxContainer v = UiKit.VBox(4);
        panel.AddChild(v);
        v.AddChild(UiKit.MakeLabel("DROP RATES", 13, Palette.UiTextDim));
        AddRateRow(v, "Common", table.Common, Palette.RarityCommon);
        AddRateRow(v, "Uncommon", table.Uncommon, Palette.RarityUncommon);
        AddRateRow(v, "Rare", table.Rare, Palette.RarityRare);
        AddRateRow(v, "Epic", table.Epic, Palette.RarityEpic);
        AddRateRow(v, "Legendary", table.Legendary, Palette.RarityLegendary);
        return panel;
    }

    private static void AddRateRow(VBoxContainer v, string name, double pct, Color color)
    {
        HBoxContainer row = UiKit.HBox(8);
        Label n = UiKit.MakeLabel(name, 15, color);
        n.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(n);
        row.AddChild(UiKit.MakeLabel($"{pct * 100:0.###}%", 15, Palette.UiText, HorizontalAlignment.Right));
        v.AddChild(row);
    }

    private Button BuildPullButton(GachaTabState state, GachaBalance gacha)
    {
        string label;
        bool enabled;
        if (_tier == GachaTier.Tier1)
        {
            int cost = gacha.Tier1PullCost;
            label = $"PULL — {cost} SLING CORES";
            enabled = _game.Run.BalanceOf(ResourceKind.SlingCores) >= cost;
        }
        else
        {
            int tokens = _tier == GachaTier.Tier2 ? state.Tier2Tokens : state.Tier3Tokens;
            label = $"PULL — {tokens} TOKEN{(tokens == 1 ? "" : "S")} AVAILABLE";
            enabled = tokens > 0;
        }

        Button b = UiKit.MakeButton(label, Palette.UiSuccess, primary: true, fontSize: 19);
        b.Disabled = !enabled;
        GachaTier tier = _tier;
        b.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            PullResult r = _game.Run.Pull(_tab, tier);
            if (r.Success)
            {
                StartReveal(new List<PullResult> { r });
            }
            else
            {
                Rebuild();
            }
        };
        return b;
    }

    /// <summary>Prompt 9 — rewarded ads: unlocked by floor clears (see
    /// AdRewardEconomy.OnFloorCleared), not a timer, capped per real day.</summary>
    private Control BuildAdSection()
    {
        PanelContainer panel = UiKit.MakePanel();
        VBoxContainer v = UiKit.VBox(6);
        panel.AddChild(v);

        AdRewardSave ad = _game.Run.AdRewardState;
        AdRewardBalance adBal = _game.Content.Balance.AdReward;
        bool canWatch = _game.Run.CanWatchAd(DateTime.UtcNow);

        v.AddChild(UiKit.MakeLabel(
            $"Ad pulls banked: {ad.AdUnlocksAvailable}   ·   Watched today: {ad.AdsWatchedToday}/{adBal.MaxAdsPerDay}",
            14, Palette.UiTextDim));

        Button watch = UiKit.MakeButton($"WATCH AD — {adBal.PullsPerAd} FREE TIER 1 PULLS", Palette.RarityRare, primary: true, fontSize: 17);
        watch.Disabled = !canWatch;
        GachaTab tab = _tab;
        watch.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            watch.Disabled = true;
            _game.AdProvider.ShowRewardedAd(
                onCompleted: () =>
                {
                    AdWatchResult result = _game.Run.WatchAd(tab, DateTime.UtcNow);
                    if (result.Success)
                    {
                        StartReveal(result.Pulls);
                    }
                    else
                    {
                        Rebuild();
                    }
                },
                onFailedOrCancelled: Rebuild);
        };
        v.AddChild(watch);

        return panel;
    }

    // --- Upgrade page --------------------------------------------------------

    private void BuildUpgradePage(VBoxContainer content)
    {
        GachaTabState state = _game.Run.GachaTabStateOf(_tab);
        content.AddChild(UiKit.MakeLabel(
            $"{_tab.ToString().ToUpperInvariant()} ESSENCE: {UiKit.FormatNumber(_game.Run.EssenceBalanceOf(_tab))}",
            18, Palette.UiText, HorizontalAlignment.Center));

        if (state.Items.Count == 0)
        {
            content.AddChild(EmptyInventoryLabel());
            return;
        }

        int maxLevel = _game.Content.Balance.Progression.MaxLevel;
        foreach (OwnedItem item in state.Items)
        {
            (string nameKey, string rarity) = TemplateInfo(item.TemplateId);
            PanelContainer panel = UiKit.MakePanel();
            HBoxContainer row = UiKit.HBox(10);
            panel.AddChild(row);

            VBoxContainer text = UiKit.VBox(2);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(UiKit.MakeLabel(_game.Content.Name(nameKey), 17, Palette.Rarity(rarity)));
            text.AddChild(UiKit.MakeLabel($"Level {item.Level} / {maxLevel}", 13, Palette.UiTextDim));
            row.AddChild(text);

            if (item.Level >= maxLevel)
            {
                row.AddChild(UiKit.MakeLabel("MAX", 16, Palette.UiSuccess));
            }
            else
            {
                int cost = GachaEconomy.EnhanceCost(_game.Content.Balance.Gacha, item.Rarity, item.Level);
                Button enhance = UiKit.MakeButton($"+1 LVL ({cost})", Palette.UiSurfaceRaised, primary: false, fontSize: 15);
                enhance.Disabled = _game.Run.EssenceBalanceOf(_tab) < cost;
                string instanceId = item.InstanceId;
                GachaTab tab = _tab;
                enhance.Pressed += () =>
                {
                    _game.Sfx.Play(SfxId.UiTap);
                    _game.Run.Enhance(tab, instanceId);
                    Rebuild();
                };
                row.AddChild(enhance);
            }

            content.AddChild(panel);
        }
    }

    // --- Evolution page --------------------------------------------------------

    private void BuildEvolutionPage(VBoxContainer content)
    {
        GachaTabState state = _game.Run.GachaTabStateOf(_tab);
        if (state.Items.Count == 0)
        {
            content.AddChild(EmptyInventoryLabel());
            return;
        }

        foreach (OwnedItem item in state.Items)
        {
            (string nameKey, string rarity) = TemplateInfo(item.TemplateId);
            int tier = Formulas.EvolutionTier(item.Level);

            PanelContainer panel = UiKit.MakePanel();
            VBoxContainer v = UiKit.VBox(4);
            panel.AddChild(v);

            HBoxContainer head = UiKit.HBox(8);
            Label name = UiKit.MakeLabel(_game.Content.Name(nameKey), 17, Palette.Rarity(rarity));
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(UiKit.MakeLabel($"TIER {tier}", 15, Palette.RarityEpic));
            v.AddChild(head);

            v.AddChild(UiKit.MakeLabel(EvolutionGateText(item.Level, tier), 13, Palette.UiTextDim, wrap: true));

            content.AddChild(panel);
        }
    }

    private static string EvolutionGateText(int level, int tier) => tier switch
    {
        0 => $"Level {level}/10 — reaching level 10 unlocks this item's ultimate.",
        1 => $"Level {level}/20 — reaching level 20 unlocks bidirectional sweep.",
        2 => $"Level {level}/30 — reaching level 30 reaches this item's final tier.",
        _ => "Final tier reached.",
    };

    private static Label EmptyInventoryLabel()
        => UiKit.MakeLabel("No items yet — pull some in the Gacha tab.", 15, Palette.UiTextDim, HorizontalAlignment.Center, wrap: true);

    // --- staged reveal overlay -------------------------------------------------

    /// <summary>Prompt 9 — "staged rarity reveal" + "duplicates showing what
    /// they became": every row starts hidden behind a "? ? ?" mystery label,
    /// then flips to its real rarity/name/duplicate-conversion outcome in
    /// sequence (RevealStaggerSeconds apart, driven from _Process) rather than
    /// dumping the whole result at once. The same overlay serves a single pull
    /// and an ad-reward's whole batch — "an overlay reading from real
    /// inventory rather than a fixed 3-step counter" means this always shows
    /// exactly results.Count rows, built from the real PullResults, never a
    /// hardcoded step count.</summary>
    private void StartReveal(List<PullResult> results)
    {
        foreach (Node child in _overlayLayer.GetChildren())
        {
            _overlayLayer.RemoveChild(child);
            child.QueueFree();
        }
        _revealRows.Clear();
        _revealClock = 0;
        _revealAllShown = false;
        _overlayLayer.MouseFilter = Control.MouseFilterEnum.Stop;

        var backdrop = new ColorRect { Color = Palette.UiBg with { A = 0.92f }, MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        _overlayLayer.AddChild(backdrop);

        MarginContainer safe = UiKit.SafeArea(24);
        backdrop.AddChild(safe);
        VBoxContainer v = UiKit.VBox(10);
        safe.AddChild(v);

        v.AddChild(UiKit.MakeLabel(
            results.Count > 1 ? "REWARD PULLS" : "PULL RESULT", 24, Palette.UiText, HorizontalAlignment.Center));

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        v.AddChild(scroll);
        VBoxContainer list = UiKit.VBox(8);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);

        for (int i = 0; i < results.Count; i++)
        {
            PullResult r = results[i];
            PanelContainer rowPanel = UiKit.MakePanel();
            var stack = new VBoxContainer();
            rowPanel.AddChild(stack);

            Label mystery = UiKit.MakeLabel("? ? ?", 22, Palette.UiTextDim, HorizontalAlignment.Center);
            stack.AddChild(mystery);

            Control revealed = BuildRevealedRow(r);
            revealed.Visible = false;
            stack.AddChild(revealed);

            list.AddChild(rowPanel);

            _revealRows.Add(new RevealRow
            {
                Result = r,
                RevealAtSeconds = i * RevealStaggerSeconds,
                MysteryLabel = mystery,
                RevealedContent = revealed,
            });
        }

        var continueSlot = new VBoxContainer { Name = "ContinueSlot" };
        v.AddChild(continueSlot);
    }

    private Control BuildRevealedRow(PullResult r)
    {
        VBoxContainer v = UiKit.VBox(3);
        if (r.Item is not { } item)
        {
            return v;
        }

        (string nameKey, string rarity) = TemplateInfo(item.TemplateId);
        Color color = Palette.Rarity(rarity);
        v.AddChild(UiKit.MakeLabel(rarity.ToUpperInvariant(), 15, color, HorizontalAlignment.Center));
        v.AddChild(UiKit.MakeLabel(_game.Content.Name(nameKey), 19, Palette.UiText, HorizontalAlignment.Center));
        if (r.WasDuplicate)
        {
            v.AddChild(UiKit.MakeLabel(
                $"Duplicate — became {r.DuplicateEssenceGranted} {_tab} Essence", 14, Palette.UiSuccess, HorizontalAlignment.Center));
        }
        if (r.PityTriggered)
        {
            v.AddChild(UiKit.MakeLabel("PITY GUARANTEE", 12, Palette.RarityLegendary, HorizontalAlignment.Center));
        }
        if (r.TrickleTriggered)
        {
            v.AddChild(UiKit.MakeLabel("TIER 3 PIECE TRICKLE!", 12, Palette.RarityEpic, HorizontalAlignment.Center));
        }
        return v;
    }

    private void ShowRevealContinueButton()
    {
        Node? slot = _overlayLayer.FindChild("ContinueSlot", recursive: true, owned: false);
        if (slot is not VBoxContainer container)
        {
            return;
        }
        Button b = UiKit.MakeButton("CONTINUE", Palette.TeamHero, primary: true, fontSize: 20);
        b.Pressed += () =>
        {
            _game.Sfx.Play(SfxId.UiTap);
            CloseReveal();
        };
        container.AddChild(b);
    }

    private void CloseReveal()
    {
        _revealRows.Clear();
        foreach (Node child in _overlayLayer.GetChildren())
        {
            _overlayLayer.RemoveChild(child);
            child.QueueFree();
        }
        _overlayLayer.MouseFilter = Control.MouseFilterEnum.Ignore;
        Rebuild();
    }
}

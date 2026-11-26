using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DMCommandTable;

public sealed partial class MainWindow
{
    private sealed record RecordField(string Key, string Label, string Kind = "text");

    private static RecordField[] RecordFields(string resource) => resource switch
    {
        "players" => [new("name", "Name"), new("kind", "Type (player or npc)"), new("race", "Race"),
            new("className", "Class"), new("level", "Level", "number"), new("hitPoints", "HP", "number"),
            new("armorClass", "AC", "number"), new("notes", "Notes (HTML)", "markup")],
        "sessions" => [new("title", "Title"), new("date", "Date"), new("status", "Status (planned, active or happened)"),
            new("notes", "Notes (HTML)", "markup")],
        "story" => [new("title", "Title"), new("chapter", "Chapter"), new("status", "Status (planned, active or happened)"),
            new("details", "Details (HTML)", "markup")],
        _ => [new("name", "Name"), new("source", "Source"), new("type", "Type"), new("challengeRating", "Challenge rating"),
            new("armorClass", "AC", "number"), new("hitPoints", "HP", "number"), new("speed", "Speed"), new("stats", "Ability scores"),
            new("token", "Token (HTML)", "markup"), new("abilities", "Actions and traits (HTML)", "markup"),
            new("spells", "Spells (HTML)", "markup"), new("notes", "Notes (HTML)", "markup"),
            new("spellSlots", "Spell slots (JSON array)", "array"), new("favorite", "Favorite (true or false)", "boolean")]
    };

    private static JsonObject Defaults(string resource) => resource switch
    {
        "players" => new() { ["kind"] = "player" },
        "story" => new() { ["status"] = "planned", ["sortOrder"] = 0, ["sessionIds"] = new JsonArray() },
        "sessions" => new() { ["status"] = "planned", ["sortOrder"] = 0 },
        _ => new() { ["armorClass"] = 10, ["hitPoints"] = 1, ["spellSlots"] = new JsonArray(), ["favorite"] = false }
    };

    private async Task ShowRecords(string resource, string title, JsonArray? summaries = null)
    {
        if (summaries is null) Heading(section);
        var path = CampaignRoute + "/" + resource;
        var records = summaries ?? Items(await engine.JsonAsync("GET", path), resource);
        Button(Row(), "New record", () => EditRecord(resource, Defaults(resource)));
        foreach (var record in records.OrderBy(item => Text(item, title), StringComparer.CurrentCultureIgnoreCase))
        {
            Button(Page, Text(record, title), async () =>
            {
                var item = resource == "monsters"
                    ? Obj(await engine.JsonAsync("GET", path + "/" + Text(record, "id")), "monster")
                    : record!.AsObject();
                await EditRecord(resource, item);
            });
        }
    }

    private Task EditRecord(string resource, JsonObject record)
    {
        var id = Text(record, "id");
        Heading(id.Length == 0 ? "New record" : Text(record, resource is "sessions" or "story" ? "title" : "name"));
        var fields = RecordFields(resource).Select(field =>
            (Definition: field, Input: Field(field.Label, Text(record, field.Key), field.Kind == "markup"))).ToArray();
        var actions = Row();
        Button(actions, "Save", async () =>
        {
            var payload = (JsonObject)record.DeepClone();
            foreach (var (definition, input) in fields)
                payload[definition.Key] = definition.Kind switch
                {
                    "number" => input.Text.Length == 0 ? null : JsonValue.Create(int.Parse(input.Text)),
                    "boolean" => JsonValue.Create(bool.Parse(input.Text)),
                    "array" => JsonNode.Parse(input.Text)?.AsArray() ?? new JsonArray(),
                    _ => JsonValue.Create(input.Text)
                };
            if (resource == "monsters")
                payload["tagIds"] = new JsonArray(Items(record, "tags").Select(tag => JsonValue.Create(Text(tag, "id"))).ToArray());
            var path = $"{CampaignRoute}/{resource}";
            await engine.JsonAsync(id.Length == 0 ? "POST" : "PATCH", id.Length == 0 ? path : $"{path}/{id}", payload);
            await ShowSection();
            Message("Record saved.");
        });
        if (resource == "sessions" && id.Length != 0)
            Button(actions, "Prepared encounters", () => ShowEncounters(id));
        if (id.Length != 0)
            Button(actions, "Delete", async () =>
            {
                if (!await Confirm("Delete this record?")) return;
                await engine.JsonAsync("DELETE", $"{CampaignRoute}/{resource}/{id}");
                await ShowSection();
            });
        Button(actions, "Back", ShowSection);
        return Task.CompletedTask;
    }

    private async Task<bool> Confirm(string title) =>
        await new ContentDialog { XamlRoot = Root.XamlRoot, Title = title, PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Primary;

    private async Task ShowEncounters(string sessionId)
    {
        Heading("Prepared encounters");
        var path = $"{CampaignRoute}/sessions/{sessionId}/encounters";
        var records = Items(await engine.JsonAsync("GET", path), "encounters");
        Button(Row(), "New encounter", () => EditEncounter(sessionId, new JsonObject
        {
            ["name"] = "", ["notes"] = "", ["sortOrder"] = records.Count,
            ["monsters"] = new JsonArray(), ["combatants"] = new JsonArray()
        }));
        foreach (var item in records)
        {
            var encounter = item!.AsObject();
            var row = Row();
            row.Children.Add(new TextBlock { Text = Text(encounter, "name"), VerticalAlignment = VerticalAlignment.Center });
            Button(row, "Edit", () => EditEncounter(sessionId, encounter));
            async Task Load(bool preserveRoster)
            {
                if (!await Confirm(preserveRoster ? "Replace the active monsters?" : "Replace the active encounter?")) return;
                var combat = Obj(await engine.JsonAsync("GET", CampaignRoute + "/combat"), "combat");
                await engine.JsonAsync("POST", CampaignRoute + "/combat/load-prepared", new()
                {
                    ["prepared"] = encounter.DeepClone(), ["revision"] = combat["revision"]?.DeepClone(),
                    ["preservePlayersAndNpcs"] = preserveRoster
                });
                Message("Prepared encounter loaded.");
            }
            Button(row, "Load into combat", () => Load(false));
            Button(row, "Load and keep players / NPCs", () => Load(true));
        }
        Button(Row(), "Back to sessions", ShowSection);
    }

    private Task EditEncounter(string sessionId, JsonObject encounter)
    {
        var id = Text(encounter, "id");
        var path = $"{CampaignRoute}/sessions/{sessionId}/encounters";
        var draft = (JsonObject)encounter.DeepClone();
        Heading(id.Length == 0 ? "New encounter" : Text(encounter, "name"));
        var name = Field("Encounter name", Text(encounter, "name"));
        var notes = Field("Encounter notes (HTML)", Text(encounter, "notes"), true);
        foreach (var combatant in Items(draft, "combatants"))
            Page.Children.Add(new TextBlock { Text = Text(combatant, "name") });
        var additions = new StackPanel { Spacing = 6 };
        Page.Children.Add(additions);
        var combatantName = Field("New monster name");
        var count = Field("HP", "10");
        Button(Row(), "Add monster", () =>
        {
            var hitPoints = int.Parse(count.Text);
            if (string.IsNullOrWhiteSpace(combatantName.Text) || hitPoints < 0)
                throw new InvalidOperationException("Enter a monster name and a non-negative HP value.");
            var combatants = Items(draft, "combatants");
            combatants.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(), ["name"] = combatantName.Text, ["kind"] = "monster",
                ["displayNumber"] = null, ["initiative"] = 0, ["hitPoints"] = hitPoints,
                ["maximumHitPoints"] = hitPoints, ["armorClass"] = 10, ["sortOrder"] = combatants.Count
            });
            additions.Children.Add(new TextBlock { Text = combatantName.Text });
            combatantName.Text = "";
            return Task.CompletedTask;
        });
        var actions = Row();
        Button(actions, "Save encounter", async () =>
        {
            draft["name"] = name.Text;
            draft["notes"] = notes.Text;
            await engine.JsonAsync(id.Length == 0 ? "POST" : "PATCH", id.Length == 0 ? path : path + "/" + id, draft);
            await ShowEncounters(sessionId);
            Message("Encounter saved.");
        });
        if (id.Length != 0)
            Button(actions, "Delete encounter", async () =>
            {
                if (!await Confirm("Delete this encounter?")) return;
                await engine.JsonAsync("DELETE", path + "/" + id);
                await ShowEncounters(sessionId);
            });
        Button(actions, "Back", () => ShowEncounters(sessionId));
        return Task.CompletedTask;
    }

    private static Border Card(UIElement content) => new()
    {
        Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = content
    };

    private static TextBlock CombatLabel(string text, double size = 14, bool accent = false) => new()
    {
        Text = text, FontSize = size, Foreground = accent ? Amber : Muted,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    private static Grid InitiativeRow()
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        return row;
    }

    private static void Place(Grid grid, FrameworkElement control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private async Task ShowCombat()
    {
        var path = CampaignRoute + "/combat";
        var combat = Obj(await engine.JsonAsync("GET", path), "combat");
        Heading("Combat");
        Page.Children.Insert(0, CombatLabel(Text(campaign, "name"), 13, accent: true));

        var workspace = new Grid
        {
            ColumnSpacing = 16, RowSpacing = 16, HorizontalAlignment = HorizontalAlignment.Left
        };
        var mainColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var sideColumn = new ColumnDefinition { Width = new GridLength(0) };
        workspace.ColumnDefinitions.Add(mainColumn);
        workspace.ColumnDefinitions.Add(sideColumn);
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var layout = new Grid();
        layout.Children.Add(workspace);
        layout.SizeChanged += (_, args) => workspace.Width = Math.Min(args.NewSize.Width, 1200);
        Page.Children.Add(layout);

        var tracker = new StackPanel { Spacing = 14 };
        var sidebar = new StackPanel { Spacing = 12 };
        var trackerCard = Card(tracker);
        var addCard = Card(sidebar);
        workspace.Children.Add(trackerCard);
        workspace.Children.Add(addCard);
        Grid.SetRow(addCard, 1);
        addCard.VerticalAlignment = VerticalAlignment.Top;
        void ArrangeCombat(double width)
        {
            var wide = width >= 720;
            sideColumn.Width = new GridLength(wide ? 280 : 0);
            workspace.ColumnSpacing = wide ? 16 : 0;
            workspace.RowSpacing = wide ? 0 : 16;
            Grid.SetColumn(addCard, wide ? 1 : 0);
            Grid.SetRow(addCard, wide ? 0 : 1);
        }
        workspace.SizeChanged += (_, args) => ArrangeCombat(args.NewSize.Width);

        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(status, new TextBlock { Text = "Initiative", Foreground = Ink, FontSize = 20 }, 0);
        Place(status, CombatLabel($"Round {Text(combat, "round")}", accent: true), 1);
        tracker.Children.Add(status);
        var name = Field("Encounter name", Text(combat, "name"), parent: tracker);

        var combatants = Items(combat, "combatants");
        if (combatants.Count > 0)
        {
            var columns = InitiativeRow();
            Place(columns, CombatLabel("Combatant"), 0);
            Place(columns, CombatLabel("Initiative"), 1);
            Place(columns, CombatLabel("HP"), 2);
            tracker.Children.Add(columns);
        }
        else
        {
            tracker.Children.Add(CombatLabel("Add a combatant to begin tracking initiative."));
        }

        var edits = new List<(TextBox Initiative, TextBox Hp)>();
        var turn = combat["turn"]?.GetValue<int>() ?? -1;
        foreach (var item in combatants)
        {
            var active = edits.Count == turn;
            var row = InitiativeRow();
            var identity = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(new TextBlock
            {
                Text = Text(item, "name"), Foreground = Ink, TextWrapping = TextWrapping.Wrap
            });
            if (active) identity.Children.Add(CombatLabel("Current turn", 12, accent: true));
            Place(row, identity, 0);
            var initiative = new TextBox { Text = Text(item, "initiative"), MinWidth = 0, CornerRadius = new CornerRadius(7) };
            var hp = new TextBox { Text = Text(item, "hitPoints"), MinWidth = 0, CornerRadius = new CornerRadius(7) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(initiative, $"Initiative for {Text(item, "name")}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hp, $"HP for {Text(item, "name")}");
            Place(row, initiative, 1);
            Place(row, hp, 2);
            tracker.Children.Add(new Border
            {
                BorderBrush = active ? Amber : Line, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 4), Child = row
            });
            edits.Add((initiative, hp));
        }
        JsonObject Draft()
        {
            var draft = (JsonObject)combat.DeepClone();
            draft["name"] = name.Text;
            var values = Items(draft, "combatants");
            for (int index = 0; index < edits.Count; index++)
            {
                values[index]!["initiative"] = double.Parse(edits[index].Initiative.Text);
                values[index]!["hitPoints"] = double.Parse(edits[index].Hp.Text);
            }
            return draft;
        }
        var actions = Row(tracker);
        Button(actions, "Save", async () => { await engine.JsonAsync("PUT", path, Draft()); await ShowCombat(); });
        Button(actions, "Next turn", async () =>
        {
            await engine.JsonAsync("POST", path + "/command", new() { ["combat"] = Draft(), ["command"] = "next-turn" });
            await ShowCombat();
        }, primary: true);
        Button(actions, "Undo", async () =>
        {
            await engine.JsonAsync("POST", path + "/undo", new() { ["revision"] = combat["revision"]?.DeepClone() });
            await ShowCombat();
        });
        sidebar.Children.Add(new TextBlock { Text = "Add combatant", Foreground = Ink, FontSize = 20 });
        var add = Field("Combatant name", parent: sidebar);
        Button(Row(sidebar), "Add combatant", async () =>
        {
            var draft = Draft();
            Items(draft, "combatants").Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(), ["name"] = add.Text, ["kind"] = "monster", ["playerId"] = null,
                ["monsterId"] = null, ["displayNumber"] = null, ["initiative"] = 0, ["hitPoints"] = 10,
                ["maximumHitPoints"] = 10, ["armorClass"] = 10, ["sortOrder"] = combatants.Count,
                ["conditions"] = new JsonArray(), ["revision"] = 1
            });
            await engine.JsonAsync("PUT", path, draft);
            await ShowCombat();
        });
    }

    private void ShowSearch()
    {
        Heading("Search campaign");
        var query = Field("Search terms");
        var results = new StackPanel { Spacing = 8 };
        Button(Row(), "Search", async () =>
        {
            var response = await engine.JsonAsync("GET", $"{CampaignRoute}/search?q={Uri.EscapeDataString(query.Text)}");
            results.Children.Clear();
            foreach (var item in Items(response, "results"))
                results.Children.Add(new TextBlock { Text = Text(item, "title"), TextWrapping = TextWrapping.Wrap });
        });
        Page.Children.Add(results);
    }

    private async Task<(byte[] Bytes, string MediaType)?> FetchToken(Uri uri)
    {
        if (uri.Scheme != "https" || uri.Host != "dnd5e.lamagra.link" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith("/img/", StringComparison.Ordinal))
            return null;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var mime = response.Content.Headers.ContentType?.MediaType;
        const int limit = 2 * 1024 * 1024;
        if (!response.IsSuccessStatusCode || mime is not ("image/webp" or "image/png" or "image/jpeg")
            || response.Content.Headers.ContentLength > limit) return null;
        using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var body = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (body.Length + count > limit) return null;
            body.Write(buffer, 0, count);
        }
        return (body.ToArray(), mime!);
    }

    private async Task ShowBestiary()
    {
        Heading("Bestiary");
        var path = CampaignRoute + "/monsters";
        var query = Field("Catalogue name or source");
        var matches = new StackPanel { Spacing = 6 };
        async Task Search(bool local)
        {
            var request = new JsonObject { ["campaignId"] = Text(campaign, "id"), ["query"] = query.Text };
            if (local) request["local"] = localCatalogue;
            var response = await engine.JsonAsync("POST", "/api/desktop-catalogue", request);
            matches.Children.Clear();
            var results = Items(response, "monsters");
            if (results.Count == 0) matches.Children.Add(new TextBlock { Text = "No matching monsters." });
            foreach (var entry in results)
            {
                var candidate = Obj(entry, "monster");
                Button(matches, $"Import {Text(candidate, "name")} ({Text(candidate, "source")})", async () =>
                {
                    var monster = (JsonObject)candidate.DeepClone();
                    var duplicates = await engine.JsonAsync("GET", path + "?view=summary&q=" + Uri.EscapeDataString(Text(monster, "name")));
                    var duplicate = Items(duplicates, "monsters").FirstOrDefault(item =>
                        string.Equals(Text(item, "name"), Text(monster, "name"), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Text(item, "source"), Text(monster, "source"), StringComparison.OrdinalIgnoreCase));
                    JsonObject? existing = null;
                    if (duplicate is not null)
                    {
                        if (!await Confirm("Replace the existing Bestiary entry?")) return;
                        existing = Obj(await engine.JsonAsync("GET", path + "/" + Text(duplicate, "id")), "monster");
                        foreach (var key in new[] { "revision", "notes", "token", "favorite" })
                            monster[key] = existing[key]?.DeepClone();
                        monster["tagIds"] = new JsonArray(Items(existing, "tags").Select(tag => JsonValue.Create(Text(tag, "id"))).ToArray());
                    }
                    var tokenMissing = false;
                    if (Uri.TryCreate(Text(entry, "tokenUrl"), UriKind.Absolute, out var uri))
                    {
                        try
                        {
                            var token = await FetchToken(uri);
                            if (token is not null)
                            {
                                using var form = new MultipartFormDataContent();
                                var bytes = new ByteArrayContent(token.Value.Bytes);
                                bytes.Headers.ContentType = new MediaTypeHeaderValue(token.Value.MediaType);
                                form.Add(bytes, "file", "monster-token");
                                form.Add(new StringContent("true"), "deduplicateToken");
                                using var uploaded = await engine.SendAsync("POST", "/api/screenshots", form);
                                var image = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())?["screenshot"];
                                if (Guid.TryParse(Text(image, "id"), out var imageId))
                                    monster["token"] = $"<img src=\"/api/screenshots/{imageId}\" alt=\"Monster token\">";
                                else tokenMissing = true;
                            }
                            else tokenMissing = true;
                        }
                        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException)
                        {
                            tokenMissing = true;
                        }
                    }
                    await engine.JsonAsync(existing is null ? "POST" : "PATCH",
                        existing is null ? path : path + "/" + Text(existing, "id"), monster);
                    await ShowBestiary();
                    Message(tokenMissing ? "Monster imported. Its token was unavailable." : "Monster imported.",
                        tokenMissing ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
                });
            }
        }
        var actions = Row();
        Button(actions, "Search online", () => Search(false));
        Button(actions, "Open local JSON", async () =>
        {
            var file = await PickJson();
            if (file is null) return;
            if ((await file.GetBasicPropertiesAsync()).Size > 10 * 1024 * 1024)
                throw new InvalidOperationException("Catalogue files must be no larger than 10 MiB.");
            localCatalogue = await Windows.Storage.FileIO.ReadTextAsync(file);
            Message("Catalogue loaded. Enter a search term and choose Search local file.");
        });
        Button(actions, "Search local file", () => localCatalogue is null
            ? throw new InvalidOperationException("Open a local catalogue file first.") : Search(true));
        Page.Children.Add(matches);
        var list = new JsonArray();
        string? after = null;
        do
        {
            var result = await engine.JsonAsync("GET", path + "?view=summary" + (after is null ? "" : "&after=" + after));
            foreach (var item in Items(result, "monsters")) list.Add(item?.DeepClone());
            after = result["next"]?.GetValue<string>();
        } while (after is not null);
        await ShowRecords("monsters", "name", list);
    }
}

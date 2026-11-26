using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DMCommandTable;

public sealed partial class MainWindow : Window
{
    private readonly Engine engine = new();
    private JsonArray campaigns = [];
    private JsonObject? campaign;
    private string section = "Campaign";
    private bool busy;
    private bool initialized;
    private string? localCatalogue;
    private string CampaignRoute => $"/api/campaigns/{Text(campaign, "id")}";

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => engine.Dispose();
        Root.Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            await Run(async () =>
            {
                Heading("Starting the local engine…");
                await engine.StartAsync();
                await ShowAccount();
                if (Environment.GetEnvironmentVariable("DM_COMMAND_TABLE_TEST_READY_FILE") is { } readyFile)
                {
                    if (WindowNative.GetWindowHandle(this) == IntPtr.Zero ||
                        !Page.Children.OfType<StackPanel>().SelectMany(row => row.Children)
                            .OfType<Button>().Any(button => Equals(button.Content, "Register")))
                        throw new InvalidOperationException("The native first-run UI was not created.");
                    File.WriteAllText(readyFile, "registration-ready");
                    DispatcherQueue.TryEnqueue(Close);
                }
            });
        };
    }

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        Root.IsHitTestVisible = false;
        Notice.IsOpen = false;
        try { await action(); }
        catch (Exception error)
        {
            Message(error.Message, InfoBarSeverity.Error);
            if (Environment.GetEnvironmentVariable("DM_COMMAND_TABLE_TEST_READY_FILE") is { } readyFile)
            {
                File.WriteAllText(readyFile, error.ToString());
                DispatcherQueue.TryEnqueue(Close);
            }
        }
        finally
        {
            Root.IsHitTestVisible = true;
            busy = false;
        }
    }

    private void Message(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    private async void Navigate(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string key }) return;
        section = key;
        await Run(ShowSection);
    }

    private static string Text(JsonNode? node, string key) => node?[key]?.ToString() ?? "";
    private static JsonObject Obj(JsonNode? node, string key) => node?[key]?.AsObject()
        ?? throw new InvalidOperationException($"The local engine omitted {key}.");
    private static JsonArray Items(JsonNode? node, string key) => node?[key]?.AsArray() ?? [];
    private static JsonArray CopyItems(JsonNode? node, string key) => (JsonArray)Items(node, key).DeepClone();

    private void Heading(string text)
    {
        Page.Children.Clear();
        Page.Children.Add(new TextBlock { Text = text, FontSize = 28, TextWrapping = TextWrapping.Wrap });
    }

    private StackPanel Row()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Page.Children.Add(row);
        return row;
    }

    private void Button(StackPanel panel, string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await Run(action);
        panel.Children.Add(button);
    }

    private TextBox Field(string label, string value = "", bool multiline = false)
    {
        var input = new TextBox { Header = label, Text = value, AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        if (multiline) input.MinHeight = 110;
        Page.Children.Add(input);
        return input;
    }

    private async Task ShowAccount()
    {
        var auth = await engine.JsonAsync("GET", "/api/auth");
        if (auth["user"] is not null)
        {
            await OpenWorkspace();
            return;
        }
        Navigation.MenuItems.Clear();
        Heading("Local account");
        var username = Field("Username");
        var password = new PasswordBox { Header = "Password" };
        Page.Children.Add(password);
        Button(Row(), "Sign in", async () =>
        {
            await engine.JsonAsync("POST", "/api/auth", new()
            {
                ["action"] = "login", ["username"] = username.Text, ["password"] = password.Password
            });
            await OpenWorkspace();
        });
        if (auth["registrationEnabled"]?.GetValue<bool>() == true)
        {
            var displayName = Field("Display name");
            Button(Row(), "Register", async () =>
            {
                await engine.JsonAsync("POST", "/api/auth", new()
                {
                    ["action"] = "register", ["username"] = username.Text, ["password"] = password.Password,
                    ["displayName"] = displayName.Text, ["bootstrapToken"] = engine.BootstrapToken
                });
                await OpenWorkspace();
            });
        }
    }

    private async Task OpenWorkspace(string? selected = null)
    {
        campaigns = Items(await engine.JsonAsync("GET", "/api/campaigns"), "campaigns");
        campaign = (selected is null ? campaigns.FirstOrDefault() :
            campaigns.FirstOrDefault(item => Text(item, "id") == selected))?.AsObject();
        if (Navigation.MenuItems.Count == 0)
        {
            foreach (var (label, icon) in new[] {
                ("Campaign", Symbol.Home), ("Players / NPCs", Symbol.People), ("Bestiary", Symbol.Library),
                ("Sessions", Symbol.Calendar), ("Story", Symbol.Document), ("Combat", Symbol.Flag),
                ("Search", Symbol.Find), ("Account", Symbol.Contact) })
                Navigation.MenuItems.Add(new NavigationViewItem { Content = label, Tag = label, Icon = new SymbolIcon(icon) });
            Navigation.SelectedItem = Navigation.MenuItems[0];
        }
        await ShowSection();
    }

    private async Task ShowSection()
    {
        if (section == "Account")
        {
            Heading("Local account");
            Button(Row(), "Sign out", async () =>
            {
                await engine.JsonAsync("POST", "/api/auth", new() { ["action"] = "logout" });
                section = "Campaign";
                await ShowAccount();
            });
            return;
        }
        if (section == "Campaign" || campaign is null) { await ShowCampaigns(); return; }
        switch (section)
        {
            case "Players / NPCs": await ShowRecords("players", "name"); break;
            case "Sessions": await ShowRecords("sessions", "title"); break;
            case "Story": await ShowRecords("story", "title"); break;
            case "Bestiary": await ShowBestiary(); break;
            case "Combat": await ShowCombat(); break;
            case "Search": ShowSearch(); break;
        }
    }

    private async Task ShowCampaigns()
    {
        Heading("Campaigns");
        var list = new ComboBox { Header = "Active campaign", MinWidth = 260 };
        foreach (var item in campaigns)
            list.Items.Add(new ComboBoxItem { Content = Text(item, "name"), Tag = Text(item, "id") });
        list.SelectedItem = list.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, Text(campaign, "id")));
        list.SelectionChanged += async (_, _) =>
        {
            if (list.SelectedItem is ComboBoxItem item) await Run(() => OpenWorkspace((string)item.Tag));
        };
        Page.Children.Add(list);
        var label = Field("New campaign name");
        var actions = Row();
        Button(actions, "Create campaign", async () =>
        {
            var result = await engine.JsonAsync("POST", "/api/campaigns", new() { ["name"] = label.Text });
            await OpenWorkspace(Text(result["campaign"], "id"));
        });
        Button(actions, "Import campaign", ImportCampaign);
        if (campaign is null) return;
        var export = Row();
        Button(export, "Export with images", () => ExportCampaign(true));
        Button(export, "Export JSON", () => ExportCampaign(false));
        var record = Obj(await engine.JsonAsync("GET", CampaignRoute), "campaign");
        var name = Field("Campaign name", Text(record, "name"));
        var notes = Field("Campaign notes (HTML)", Text(record, "notes"), true);
        Button(Row(), "Save campaign", async () =>
        {
            await engine.JsonAsync("PATCH", CampaignRoute, new()
            {
                ["name"] = name.Text, ["notes"] = notes.Text, ["revision"] = record["revision"]?.DeepClone()
            });
            await OpenWorkspace(Text(record, "id"));
            Message("Campaign saved.");
        });
    }

    private async Task<StorageFile?> PickJson()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".json");
        return await picker.PickSingleFileAsync();
    }

    private async Task ImportCampaign()
    {
        var file = await PickJson();
        if (file is null) return;
        if ((await file.GetBasicPropertiesAsync()).Size > Engine.CampaignLimit)
            throw new InvalidOperationException("Campaign imports must be no larger than 50 MiB.");
        using var stream = await file.OpenStreamForReadAsync();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await engine.SendAsync("POST", "/api/campaigns/import", content);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        await OpenWorkspace(Text(result?["campaign"], "id"));
        Message("Campaign imported.");
    }

    private async Task ExportCampaign(bool withImages)
    {
        using var response = await engine.SendAsync("GET", CampaignRoute + (withImages ? "/package" : "/export"));
        var picker = new FileSavePicker { SuggestedFileName = withImages ? "campaign.dmct" : "campaign" };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeChoices.Add("Campaign JSON", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        using var stream = await file.OpenStreamForWriteAsync();
        stream.SetLength(0);
        await response.Content.CopyToAsync(stream);
        Message("Campaign exported.");
    }
}

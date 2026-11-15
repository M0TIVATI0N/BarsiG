using FinanceFlow.Domain.Analytics;
using FinanceFlow.Domain.Common;
using FinanceFlow.Domain.Enums;
using FinanceFlow.Web.Components.Shared;
using FinanceFlow.Web.State;

using Microsoft.Extensions.DependencyInjection;

namespace FinanceFlow.ComponentTests;

public sealed class SharedComponentTests : BunitContext
{
    [Fact]
    public void StatCard_ShowsTheLabelAndTheValue()
    {
        IRenderedComponent<StatCard> component = Render<StatCard>(parameters => parameters
            .Add(p => p.Label, "Current balance")
            .Add(p => p.Value, "1 234,00 ₸")
            .Add(p => p.Icon, "wallet"));

        Assert.Contains("Current balance", component.Markup);
        Assert.Contains("1 234,00 ₸", component.Markup);
    }

    [Fact]
    public void ProgressBar_ClampsTheValueToOneHundredPercent()
    {
        IRenderedComponent<ProgressBar> component = Render<ProgressBar>(parameters => parameters
            .Add(p => p.Value, 1.8));

        Assert.Contains("width:100%", component.Markup);
    }

    [Fact]
    public void ProgressBar_HidesTheLabelWhenItIsEmpty()
    {
        IRenderedComponent<ProgressBar> component = Render<ProgressBar>();

        Assert.DoesNotContain("progress-label", component.Markup);
    }

    [Fact]
    public void MoneyText_ShowsAPlusSignForIncome()
    {
        IRenderedComponent<MoneyText> component = Render<MoneyText>(parameters => parameters
            .Add(p => p.Amount, Money.FromDecimal(250m))
            .Add(p => p.Kind, TransactionKind.Income));

        Assert.Contains("+250,00 ₸", component.Markup);
    }

    [Fact]
    public void MoneyText_ShowsThePlainAmountForAnExpense()
    {
        IRenderedComponent<MoneyText> component = Render<MoneyText>(parameters => parameters
            .Add(p => p.Amount, Money.FromDecimal(250m))
            .Add(p => p.Kind, TransactionKind.Expense));

        Assert.Contains("250,00 ₸", component.Markup);
        Assert.DoesNotContain("+250,00", component.Markup);
    }

    [Fact]
    public void CategoryBadge_ShowsTheNameOfTheCategory()
    {
        IRenderedComponent<CategoryBadge> component = Render<CategoryBadge>(parameters => parameters
            .Add(p => p.Name, "Groceries")
            .Add(p => p.Color, "#F59E0B")
            .Add(p => p.Icon, "cart"));

        Assert.Contains("Groceries", component.Markup);
        Assert.Contains("#F59E0B", component.Markup);
    }

    [Fact]
    public void EmptyState_ShowsTheTitleAndTheMessage()
    {
        IRenderedComponent<EmptyState> component = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No transactions found")
            .Add(p => p.Message, "Change the filter."));

        Assert.Contains("No transactions found", component.Markup);
        Assert.Contains("Change the filter.", component.Markup);
    }

    [Fact]
    public void PageHeader_ShowsTheTitleTheSubtitleAndTheActions()
    {
        IRenderedComponent<PageHeader> component = Render<PageHeader>(parameters => parameters
            .Add(p => p.Title, "Dashboard")
            .Add(p => p.Subtitle, "An overview of your money.")
            .Add(p => p.Actions, builder => builder.AddMarkupContent(0, "<button>Add</button>")));

        Assert.Contains("Dashboard", component.Markup);
        Assert.Contains("An overview of your money.", component.Markup);
        Assert.Contains("Add", component.Markup);
    }

    [Fact]
    public void ConfirmDialog_IsHiddenUntilItIsVisible()
    {
        IRenderedComponent<ConfirmDialog> hidden = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.Visible, false)
            .Add(p => p.Title, "Delete the transaction?"));

        Assert.DoesNotContain("Delete the transaction?", hidden.Markup);

        IRenderedComponent<ConfirmDialog> visible = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.Title, "Delete the transaction?"));

        Assert.Contains("Delete the transaction?", visible.Markup);
    }

    [Fact]
    public async Task ConfirmDialog_RaisesTheEvents()
    {
        bool confirmed = false;
        bool cancelled = false;

        IRenderedComponent<ConfirmDialog> component = Render<ConfirmDialog>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.OnConfirm, () => confirmed = true)
            .Add(p => p.OnCancel, () => cancelled = true));

        await component.Find("button.btn-danger").ClickAsync();
        Assert.True(confirmed);

        await component.Find("button.btn-ghost").ClickAsync();
        Assert.True(cancelled);
    }

    [Fact]
    public void LineChart_DrawsALineForTheGivenValues()
    {
        IRenderedComponent<LineChart> component = Render<LineChart>(parameters => parameters
            .Add(p => p.Values, new List<double> { 1d, 5d, 3d })
            .Add(p => p.Labels, new List<string> { "a", "b", "c" }));

        Assert.Contains("chart-line", component.Markup);
        Assert.Contains("M ", component.Markup);
        Assert.Contains("chart-point", component.Markup);
    }

    [Fact]
    public void BarChart_DrawsTwoBarsForEveryGroup()
    {
        IRenderedComponent<BarChart> component = Render<BarChart>(parameters => parameters
            .Add(p => p.Groups, new List<BarChart.BarGroup>
            {
                new(100d, 50d),
                new(200d, 120d)
            })
            .Add(p => p.Labels, new List<string> { "2026-01", "2026-02" }));

        int bars = component.FindAll("rect.chart-bar").Count;

        Assert.Equal(4, bars);
    }

    [Fact]
    public void DonutChart_DrawsOneSegmentForEveryShare()
    {
        IRenderedComponent<DonutChart> component = Render<DonutChart>(parameters => parameters
            .Add(p => p.Segments, new List<DonutChart.Segment>
            {
                new("Food", 0.6d, "#FF0000"),
                new("Rent", 0.4d, "#00FF00")
            })
            .Add(p => p.CenterLabel, "1 000,00 ₸"));

        Assert.Equal(2, component.FindAll("path").Count);
        Assert.Contains("1 000,00 ₸", component.Markup);
    }

    [Fact]
    public void MonthPicker_ShowsTheMonth()
    {
        IRenderedComponent<MonthPicker> component = Render<MonthPicker>(parameters => parameters
            .Add(p => p.Year, 2026)
            .Add(p => p.Month, 3));

        Assert.Contains("March 2026", component.Markup);
    }

    [Fact]
    public void ToastHost_ShowsTheMessagesOfTheCascadingValue()
    {
        ToastService toastService = new();
        Services.AddSingleton(toastService);

        ToastMessage message = new(Guid.NewGuid(), ToastKind.Success, "Saved", "The transaction was stored.");

        IRenderedComponent<ToastHost> component = Render<ToastHost>(parameters => parameters
            .AddCascadingValue<IReadOnlyList<ToastMessage>>([message]));

        Assert.Contains("The transaction was stored.", component.Markup);
        Assert.Contains("toast-success", component.Markup);
    }

    [Fact]
    public void AppIcon_RendersTheIconOfTheGivenName()
    {
        IRenderedComponent<AppIcon> component = Render<AppIcon>(parameters => parameters
            .Add(p => p.Name, "wallet"));

        Assert.Contains("<svg", component.Markup);
        Assert.Contains("class=\"icon", component.Markup);
    }
}

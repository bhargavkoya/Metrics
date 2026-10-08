using Metrics.Application.Common;
using Metrics.Application.Formulas;
using Metrics.Application.Metrics;
using Metrics.Domain;
using Metrics.Domain.Entities;
using Moq;

namespace Metrics.Tests.Metrics;

public class MetricServiceTests
{
    private static readonly Guid AutomationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMetricRepository> _repo = new();
    private readonly List<MetricDefinition> _live = [];
    private readonly MetricService _sut;
    private Mock<global::Metrics.Application.Logs.IChangeNotifier> Notifier { get; } = new();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public MetricServiceTests()
    {
        _repo.Setup(r => r.AutomationExistsAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ListLiveAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live.ToList());
        _repo.Setup(r => r.FindLiveAsync(AutomationId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _live.FirstOrDefault(m => m.Id == id));
        _repo.Setup(r => r.AddAsync(It.IsAny<MetricDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<MetricDefinition, CancellationToken>((d, _) => _live.Add(d)).Returns(Task.CompletedTask);
        _repo.Setup(r => r.SoftDeleteAsync(It.IsAny<MetricDefinition>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<MetricDefinition, DateTime, CancellationToken>((d, _, _) => _live.Remove(d)).Returns(Task.CompletedTask);
        _sut = new MetricService(_repo.Object, new FormulaEngine(), new FixedClock(Now), Notifier.Object);
    }

    private Task<MetricDefinitionDto> AddInput(string label, MetricValueType type, string? currency = null) =>
        _sut.CreateAsync(AutomationId, UserId, new CreateMetricRequest(label, MetricKind.Input, type, currency, null), default);

    private Task<MetricDefinitionDto> AddComputed(string label, string formula) =>
        _sut.CreateAsync(AutomationId, UserId, new CreateMetricRequest(label, MetricKind.Computed, null, null, formula), default);

    private static async Task<ValidationFailedException> ValidationOf(Task t) =>
        await Assert.ThrowsAsync<ValidationFailedException>(() => t);

    [Fact]
    public async Task CreateInput_StoresTypeAndMetadata()
    {
        var dto = await AddInput("  Records processed ", MetricValueType.Number);

        Assert.Equal("Records processed", dto.Label);
        Assert.Equal(MetricKind.Input, dto.Kind);
        Assert.Equal(MetricValueType.Number, dto.ValueType);
        Assert.Null(dto.FormulaText);
        var stored = Assert.Single(_live);
        Assert.Equal(UserId, stored.CreatedBy);
        Assert.Equal(Now.UtcDateTime, stored.CreatedAt);
        Assert.Equal(AutomationId, stored.AutomationId);
    }

    [Fact]
    public async Task CreateAndDelete_SignalTheChange_ButRejectionsDoNot()
    {
        var created = await AddInput("Records", MetricValueType.Number);
        Notifier.Verify(n => n.Notify(AutomationId), Times.Once);

        await Assert.ThrowsAsync<ValidationFailedException>(() => AddInput("", MetricValueType.Number));
        await Assert.ThrowsAsync<ConflictException>(() => AddInput("records", MetricValueType.Number));
        Notifier.Verify(n => n.Notify(AutomationId), Times.Once); // still just the one

        await _sut.DeleteAsync(AutomationId, created.Id, default);
        Notifier.Verify(n => n.Notify(AutomationId), Times.Exactly(2));
    }

    [Fact]
    public async Task BlockedDelete_DoesNotSignal()
    {
        var manual = await AddInput("Manual", MetricValueType.Duration);
        await AddComputed("Doubled", "[Manual] * 2");
        Notifier.Invocations.Clear();

        await Assert.ThrowsAsync<ConflictException>(() => _sut.DeleteAsync(AutomationId, manual.Id, default));

        Notifier.Verify(n => n.Notify(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CurrencyInput_DefaultsToUsd_AndNormalizesCode()
    {
        Assert.Equal("USD", (await AddInput("Cost", MetricValueType.Currency)).CurrencyCode);
        Assert.Equal("EUR", (await AddInput("Cost EUR", MetricValueType.Currency, " eur ")).CurrencyCode);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    public async Task CurrencyInput_BadCode_IsRejected(string code)
    {
        var ex = await ValidationOf(AddInput("Cost", MetricValueType.Currency, code));

        Assert.Contains("currencyCode", ex.Errors.Keys);
    }

    [Fact]
    public async Task NonCurrencyInput_WithCurrencyCode_IsRejected()
    {
        var ex = await ValidationOf(AddInput("Records", MetricValueType.Number, "USD"));

        Assert.Contains("currencyCode", ex.Errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Has [brackets]")]
    [InlineData("Bad ] bracket")]
    public async Task BadLabels_AreRejected(string label)
    {
        var ex = await ValidationOf(AddInput(label, MetricValueType.Number));

        Assert.Contains("label", ex.Errors.Keys);
        Assert.Empty(_live);
    }

    [Fact]
    public async Task LabelOver100Chars_IsRejected()
    {
        var ex = await ValidationOf(AddInput(new string('x', 101), MetricValueType.Number));

        Assert.Contains("label", ex.Errors.Keys);
    }

    [Fact]
    public async Task Input_RequiresAValidType_AndNoFormula()
    {
        var noType = await ValidationOf(_sut.CreateAsync(AutomationId, UserId,
            new CreateMetricRequest("A", MetricKind.Input, null, null, null), default));
        Assert.Contains("valueType", noType.Errors.Keys);

        var badType = await ValidationOf(_sut.CreateAsync(AutomationId, UserId,
            new CreateMetricRequest("A", MetricKind.Input, (MetricValueType)42, null, null), default));
        Assert.Contains("valueType", badType.Errors.Keys);

        var withFormula = await ValidationOf(_sut.CreateAsync(AutomationId, UserId,
            new CreateMetricRequest("A", MetricKind.Input, MetricValueType.Number, null, "[B] + 1"), default));
        Assert.Contains("formula", withFormula.Errors.Keys);
    }

    [Fact]
    public async Task UnknownKind_IsRejected()
    {
        var ex = await ValidationOf(_sut.CreateAsync(AutomationId, UserId,
            new CreateMetricRequest("A", (MetricKind)9, MetricValueType.Number, null, null), default));

        Assert.Contains("kind", ex.Errors.Keys);
    }

    [Theory]
    [InlineData("Records", "records")]
    [InlineData("Records", "  RECORDS  ")]
    public async Task DuplicateLabel_IsConflict_CaseInsensitive(string first, string second)
    {
        await AddInput(first, MetricValueType.Number);

        await Assert.ThrowsAsync<ConflictException>(() => AddInput(second, MetricValueType.Duration));
        Assert.Single(_live);
    }

    [Fact]
    public async Task ComputedAndInputLabels_ShareOneNamespace()
    {
        await AddInput("Time", MetricValueType.Duration);
        await AddInput("Other", MetricValueType.Duration);

        await Assert.ThrowsAsync<ConflictException>(() => AddComputed("Time", "[Time] - [Other]"));
    }

    [Fact]
    public async Task CreateComputed_InfersResultType_AndStoresFormulaAsEntered()
    {
        await AddInput("Manual", MetricValueType.Duration);
        await AddInput("Auto", MetricValueType.Duration);

        var dto = await AddComputed("Time saved", "  [Manual] - [Auto]  ");

        Assert.Equal(MetricKind.Computed, dto.Kind);
        Assert.Equal(MetricValueType.Duration, dto.ValueType);
        Assert.Equal("[Manual] - [Auto]", dto.FormulaText);
    }

    [Fact]
    public async Task CreateComputed_Currency_InfersCode()
    {
        await AddInput("Cost", MetricValueType.Currency, "GBP");
        await AddInput("Rate", MetricValueType.Percentage);

        var dto = await AddComputed("Saving", "[Cost] * [Rate]");

        Assert.Equal(MetricValueType.Currency, dto.ValueType);
        Assert.Equal("GBP", dto.CurrencyCode);
    }

    [Fact]
    public async Task CreateComputed_IgnoresAnyClientSuppliedType()
    {
        await AddInput("Manual", MetricValueType.Duration);
        await AddInput("Auto", MetricValueType.Duration);

        var dto = await _sut.CreateAsync(AutomationId, UserId,
            new CreateMetricRequest("Saved", MetricKind.Computed, MetricValueType.Currency, "USD", "[Manual] - [Auto]"), default);

        Assert.Equal(MetricValueType.Duration, dto.ValueType);
        Assert.Null(dto.CurrencyCode);
    }

    [Fact]
    public async Task CreateComputed_InvalidFormula_IsRejectedWithPositions_AndNothingSaved()
    {
        await AddInput("Records", MetricValueType.Number);
        await AddInput("Time", MetricValueType.Duration);

        var ex = await ValidationOf(AddComputed("Rate", "[Records] / [Time]"));

        var message = Assert.Single(ex.Errors["formula"]);
        Assert.Contains("Cannot divide Number by Duration", message);
        Assert.Contains("position", message);
        Assert.Equal(2, _live.Count);
    }

    [Fact]
    public async Task CreateComputed_CannotReferenceAnotherComputedMetric()
    {
        await AddInput("A", MetricValueType.Number);
        await AddComputed("Double", "[A] * 2");

        var ex = await ValidationOf(AddComputed("Quad", "[Double] * 2"));

        Assert.Contains("Unknown metric [Double]", Assert.Single(ex.Errors["formula"]));
    }

    [Fact]
    public async Task CreateComputed_FormulaMissing_IsRejected()
    {
        var ex = await ValidationOf(AddComputed("X", "  "));

        Assert.Contains("formula", ex.Errors.Keys);
    }

    [Fact]
    public async Task ValidateFormula_ReturnsTypeOrErrors_WithoutSaving()
    {
        await AddInput("Manual", MetricValueType.Duration);
        await AddInput("Records", MetricValueType.Number);

        var ok = await _sut.ValidateFormulaAsync(AutomationId, new ValidateFormulaRequest("[Manual] * 2"), default);
        Assert.True(ok.Valid);
        Assert.Equal(MetricValueType.Duration, ok.ResultType);
        Assert.Equal(["Manual"], ok.References);

        var bad = await _sut.ValidateFormulaAsync(AutomationId, new ValidateFormulaRequest("[Records] / 0"), default);
        Assert.False(bad.Valid);
        Assert.Null(bad.ResultType);
        Assert.Equal("DivisionByZeroLiteral", Assert.Single(bad.Errors).Code);

        Assert.Equal(2, _live.Count);
    }

    [Fact]
    public async Task DeleteInput_UsedByLiveFormula_IsConflict_NamingTheDependent()
    {
        var manual = await AddInput("Manual", MetricValueType.Duration);
        await AddInput("Auto", MetricValueType.Duration);
        await AddComputed("Time saved", "[Manual] - [Auto]");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _sut.DeleteAsync(AutomationId, manual.Id, default));

        Assert.Contains("Time saved", ex.Message);
        Assert.Equal(3, _live.Count);
    }

    [Fact]
    public async Task DeleteComputed_ThenItsInput_IsAllowed_AndRemovesGoingForward()
    {
        var manual = await AddInput("Manual", MetricValueType.Duration);
        await AddInput("Auto", MetricValueType.Duration);
        var saved = await AddComputed("Time saved", "[Manual] - [Auto]");

        await _sut.DeleteAsync(AutomationId, saved.Id, default);
        await _sut.DeleteAsync(AutomationId, manual.Id, default);

        Assert.Equal(["Auto"], (await _sut.ListAsync(AutomationId, default)).Select(m => m.Label));
        _repo.Verify(r => r.SoftDeleteAsync(It.IsAny<MetricDefinition>(), Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteInput_NotReferenced_IsAllowed()
    {
        await AddInput("Manual", MetricValueType.Duration);
        var unused = await AddInput("Unused", MetricValueType.Number);
        await AddComputed("Doubled", "[Manual] * 2");

        await _sut.DeleteAsync(AutomationId, unused.Id, default);

        Assert.DoesNotContain(_live, m => m.Label == "Unused");
    }

    [Fact]
    public async Task Delete_Unknown_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(AutomationId, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task LabelCanBeReused_AfterDelete()
    {
        var first = await AddInput("Records", MetricValueType.Number);
        await _sut.DeleteAsync(AutomationId, first.Id, default);

        var again = await AddInput("Records", MetricValueType.Duration);

        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal(MetricValueType.Duration, again.ValueType);
    }

    [Fact]
    public async Task UnknownAutomation_IsNotFound_ForEveryOperation()
    {
        var other = Guid.NewGuid();
        _repo.Setup(r => r.AutomationExistsAsync(other, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListAsync(other, default));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CreateAsync(other, UserId,
            new CreateMetricRequest("A", MetricKind.Input, MetricValueType.Number, null, null), default));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ValidateFormulaAsync(other, new ValidateFormulaRequest("[A]+1"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(other, Guid.NewGuid(), default));
    }
}

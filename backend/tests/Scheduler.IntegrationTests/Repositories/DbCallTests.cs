using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Abstractions;
using Scheduler.Infrastructure.Repositories;

namespace Scheduler.IntegrationTests.Repositories;

/// <summary>
/// No database needed. SqlException cannot be constructed in a test, so the SQL error numbers are covered by the
/// repository integration tests; here the TimeoutException path and the pass-through behaviour are checked.
/// </summary>
public sealed class DbCallTests
{
    [Fact]
    public void Classifier_treats_a_timeout_and_a_wrapped_timeout_as_transient()
    {
        SqlErrorClassifier.Classify(new TimeoutException()).Should().Be(SqlErrorKind.Transient);
        SqlErrorClassifier.Classify(new DbUpdateException("save failed", new TimeoutException()))
            .Should().Be(SqlErrorKind.Transient);
    }

    [Fact]
    public void Classifier_does_not_know_other_exceptions()
    {
        SqlErrorClassifier.Classify(new InvalidOperationException()).Should().Be(SqlErrorKind.Unknown);
        SqlErrorClassifier.Classify(new OperationCanceledException()).Should().Be(SqlErrorKind.Unknown);
    }

    [Fact]
    public async Task RunAsync_returns_the_query_result()
    {
        (await DbCall.RunAsync(() => Task.FromResult(42))).Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_turns_a_transient_error_into_TransientDatabaseException()
    {
        var timeout = new InvalidOperationException("query failed", new TimeoutException());

        var act = () => DbCall.RunAsync<int>(() => throw timeout);

        (await act.Should().ThrowAsync<TransientDatabaseException>()).Which.InnerException.Should().BeSameAs(timeout);
    }

    [Fact]
    public async Task RunAsync_lets_other_exceptions_through_unchanged()
    {
        var bug = new InvalidOperationException("bug");
        var cancelled = new OperationCanceledException();

        (await FluentActions.Awaiting(() => DbCall.RunAsync<int>(() => throw bug))
            .Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(bug);
        (await FluentActions.Awaiting(() => DbCall.RunAsync<int>(() => throw cancelled))
            .Should().ThrowAsync<OperationCanceledException>()).Which.Should().BeSameAs(cancelled);
    }
}

using KnowledgeSystem.Agents.Context;
using KnowledgeSystem.Agents.Context.TokenEstimation;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using OpenAI.Chat;

namespace KnowledgeSystem.Tests;

public class AgentTests
{
    #region ContextCompactor

    [Fact]
    public void CompactToolResults_ReplacesConsumedLargeResult()
    {
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        var largeContent = new string('#', 5000);
        context.InsertChat(new ToolChatMessage("call_1", largeContent));
        context.InsertAssistant("Based on results, X is Y");

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var toolElement = (ChatElement)elements[2];
        var toolMsg = Assert.IsType<ToolChatMessage>(toolElement.Message);
        Assert.Contains("compacted", toolMsg.Content.First().Text);
        Assert.DoesNotContain(new string('#', 100), toolMsg.Content.First().Text);
    }

    [Fact]
    public void CompactToolResults_PreservesUnconsumedResult()
    {
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        var largeContent = new string('#', 5000);
        context.InsertChat(new ToolChatMessage("call_1", largeContent));

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var toolElement = (ChatElement)elements[2];
        var toolMsg = Assert.IsType<ToolChatMessage>(toolElement.Message);
        Assert.Equal(largeContent, toolMsg.Content.First().Text);
    }

    [Fact]
    public void CompactToolResults_PreservesSmallResults()
    {
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        context.InsertChat(new ToolChatMessage("call_1", "small result"));
        context.InsertAssistant("Done");

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var toolElement = (ChatElement)elements[2];
        var toolMsg = Assert.IsType<ToolChatMessage>(toolElement.Message);
        Assert.Equal("small result", toolMsg.Content.First().Text);
    }

    [Fact]
    public void CompactToolResults_PreservesHeaderInStub()
    {
        var largeResult = "# fast_context: 3 documents found. Extracted 5 sections:\n" + new string('x', 5000);
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        context.InsertChat(new ToolChatMessage("call_1", largeResult));
        context.InsertAssistant("Done");

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var toolElement = (ChatElement)elements[2];
        var toolMsg = Assert.IsType<ToolChatMessage>(toolElement.Message);
        var stub = toolMsg.Content.First().Text;
        Assert.StartsWith("# fast_context: 3 documents found", stub);
        Assert.Contains("compacted", stub);
    }

    [Fact]
    public void CompactToolResults_HandlesMultipleToolResults()
    {
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        context.InsertChat(new ToolChatMessage("call_1", new string('A', 5000)));
        context.InsertChat(new ToolChatMessage("call_2", new string('B', 3000)));
        context.InsertAssistant("Done");

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var msg1 = Assert.IsType<ToolChatMessage>(((ChatElement)elements[2]).Message);
        var msg2 = Assert.IsType<ToolChatMessage>(((ChatElement)elements[3]).Message);
        Assert.Contains("compacted", msg1.Content.First().Text);
        Assert.Contains("compacted", msg2.Content.First().Text);
    }

    [Fact]
    public void CompactToolResults_NonHeaderResult_GetsGenericStub()
    {
        var largeResult = new string('x', 5000);
        var context = new AgentContext();
        context.InsertSystem("system");
        context.InsertUser("search");
        context.InsertChat(new ToolChatMessage("call_1", largeResult));
        context.InsertAssistant("Done");

        var compactor = new ContextCompactor(MockTokenEstimator.Instance, toolResultCharThreshold: 2000);
        compactor.CompactToolResults(context);

        var elements = context.Elements;
        var toolElement = (ChatElement)elements[2];
        var toolMsg = Assert.IsType<ToolChatMessage>(toolElement.Message);
        var stub = toolMsg.Content.First().Text;
        Assert.StartsWith("[Tool result compacted:", stub);
        Assert.Contains("5000 chars]", stub);
    }

    #endregion

    #region ToolExecutionResult

    [Fact]
    public void ToolExecutionResult_Success_ReturnsOutput()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, true, "hello", null, null);

        Assert.True(result.IsSuccessful);
        Assert.Equal("hello", result.Output);
    }

    [Fact]
    public void ToolExecutionResult_Failure_ThrowsOnOutputAccess()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, false, null, "error msg", null);

        Assert.False(result.IsSuccessful);
        Assert.Throws<InvalidOperationException>(() => result.Output);
    }

    [Fact]
    public void ToolExecutionResult_Success_ThrowsOnErrorAccess()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, true, "ok", null, null);

        Assert.Throws<InvalidOperationException>(() => result.ErrorMessage);
        Assert.Throws<InvalidOperationException>(() => result.ThrownException);
    }

    [Fact]
    public void ToolExecutionResult_FormatError_WithMessage()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, false, null, "bad args", null);

        Assert.Equal("bad args", result.FormatError());
    }

    [Fact]
    public void ToolExecutionResult_FormatError_WithException()
    {
        var tool = CreateTestTool("test_tool");
        var ex = new InvalidOperationException("boom");
        var result = new ToolExecutionResult(tool, false, null, null, ex);

        Assert.Equal("boom", result.FormatError());
    }

    [Fact]
    public void ToolExecutionResult_FormatError_WithMessageAndException()
    {
        var tool = CreateTestTool("test_tool");
        var ex = new InvalidOperationException("boom");
        var result = new ToolExecutionResult(tool, false, null, "tool failed", ex);

        Assert.Equal("tool failed. Exception: boom", result.FormatError());
    }

    [Fact]
    public void ToolExecutionResult_FormatError_Unspecified()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, false, null, null, null);

        Assert.Equal("Unspecified error", result.FormatError());
    }

    [Fact]
    public void ToolExecutionResult_ToString_Success()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, true, "output", null, null);

        Assert.Equal("output", result.ToString());
    }

    [Fact]
    public void ToolExecutionResult_ToString_Error()
    {
        var tool = CreateTestTool("test_tool");
        var result = new ToolExecutionResult(tool, false, null, "err", null);

        Assert.Equal("err", result.ToString());
    }

    #endregion

    #region AgentToolFrame

    [Fact]
    public void HallucinationFrame_StoresCallIdFunctionNameAndMessage()
    {
        var frame = new AgentToolFrame.Hallucination("call_abc", "fake_tool", "No such tool");

        Assert.Equal("call_abc", frame.CallId);
        Assert.Equal("fake_tool", frame.FunctionName);
        Assert.Equal("No such tool", frame.ErrorMessage);
    }

    [Fact]
    public void MissingArgsFrame_StoresCallIdAndMessage()
    {
        var frame = new AgentToolFrame.MissingArgs("call_xyz", "Missing arg 'query'");

        Assert.Equal("call_xyz", frame.CallId);
        Assert.Equal("Missing arg 'query'", frame.ErrorMessage);
    }

    [Fact]
    public void PlainRunningFrame_StoresCallIdToolAndArgs()
    {
        var tool = CreateTestTool("search");
        var args = CreateSuccessArgs();
        var frame = new AgentToolFrame.PlainRunningFrame("call_123", tool, args);

        Assert.Equal("call_123", frame.CallId);
        Assert.Same(tool, frame.Tool);
        Assert.Null(frame.StartedTask);
        Assert.Null(frame.Result);
    }

    [Fact]
    public void RunningSubAgentFrame_StoresCallIdToolAndArgs()
    {
        var tool = CreateTestTool("sub_agent");
        var args = CreateSuccessArgs();
        var frame = new AgentToolFrame.RunningSubAgent("call_456", tool, args);

        Assert.Equal("call_456", frame.CallId);
        Assert.Same(tool, frame.Tool);
        Assert.Null(frame.Proxy);
        Assert.Null(frame.Result);
    }

    [Fact]
    public void RunningFrame_ResultCanBeSet()
    {
        var tool = CreateTestTool("search");
        var args = CreateSuccessArgs();
        var frame = new AgentToolFrame.PlainRunningFrame("call_1", tool, args);

        Assert.Null(frame.Result);

        var result = new ToolExecutionResult(tool, true, "found it", null, null);
        frame.Result = result;

        Assert.NotNull(frame.Result);
        Assert.Equal("found it", frame.Result.Output);
    }

    #endregion

    #region AgentExecutionError

    [Fact]
    public void AgentExecutionError_StoresMessageAndCriticality()
    {
        var error = new AgentExecutionError("something broke", true);

        Assert.Equal("something broke", error.Message);
        Assert.True(error.IsCritical);
    }

    [Fact]
    public void AgentExecutionError_ToString_ReturnsMessage()
    {
        var error = new AgentExecutionError("oops", false);
        Assert.Equal("oops", error.ToString());
    }

    [Fact]
    public void AgentToolHallucinationError_StoresToolIdAndIndex()
    {
        var error = new AgentToolHallucinationError("Invalid tool", false, "fake_tool", 2);

        Assert.Equal("Invalid tool", error.Message);
        Assert.False(error.IsCritical);
        Assert.Equal("fake_tool", error.ToolId);
        Assert.Equal(2, error.Index);
    }

    [Fact]
    public void AgentToolIncompleteArgumentsError_StoresExtractionResult()
    {
        var extraction = CreateSuccessArgs();
        var error = new AgentToolIncompleteArgumentsError("Missing args", false, "search", 0, extraction);

        Assert.Equal("Missing args", error.Message);
        Assert.Equal("search", error.ToolId);
        Assert.Same(extraction, error.ExtractionResult);
    }

    #endregion

    #region AgentCompletionResult

    [Fact]
    public void AgentCompletionResult_CompletesWithNoError()
    {
        var result = new AgentCallbackResult(true, null);

        Assert.True(result.CompletesExecution);
        Assert.Null(result.Error);
    }

    [Fact]
    public void AgentCompletionResult_CompletesWithError()
    {
        var error = new AgentExecutionError("fail", true);
        var result = new AgentCallbackResult(true, error);

        Assert.True(result.CompletesExecution);
        Assert.NotNull(result.Error);
        Assert.Equal("fail", result.Error.Message);
    }

    [Fact]
    public void AgentCompletionResult_DoesNotComplete()
    {
        var result = new AgentCallbackResult(false, null);

        Assert.False(result.CompletesExecution);
        Assert.Null(result.Error);
    }

    #endregion

    #region Helpers

    private static AgentTool CreateTestTool(string toolId)
    {
        return new AgentTool
        {
            ToolId = toolId,
            Description = $"Test tool {toolId}",
            Arguments = [],
            RequiredArguments = [],
            Tool = ChatTool.CreateFunctionTool(toolId, $"Test tool {toolId}", BinaryData.FromString("{}"))
        };
    }

    private static ArgumentExtractionResult CreateSuccessArgs()
    {
        return new ArgumentExtractionResult
        {
            Status = ArgumentExtractionResult.ExtractionStatus.Success,
            Arguments = new Dictionary<ToolArgument, string?>(),
            MissingArguments = []
        };
    }

    private sealed class MockTokenEstimator : ITokenEstimator
    {
        public static readonly MockTokenEstimator Instance = new();
        public int CountTokens(IEnumerable<ChatMessage> messages) => 99999;
        public void Dispose() { }
    }

    #endregion
}

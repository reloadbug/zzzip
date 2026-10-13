using System.Text;

namespace zzzip.SelfTest;

public enum TestOutcome { Pass, Fail, Skip }

public sealed record TestResult(string Name, TestOutcome Outcome, string Detail, TimeSpan Elapsed);

/// <summary>极简测试框架：零依赖，退出码 = 失败用例数，可直接被 CI 判定。</summary>
public sealed class TestHarness
{
    private readonly List<TestResult> _results = [];
    private readonly StringBuilder _log = new();

    public string CurrentName { get; private set; } = string.Empty;

    /// <summary>单个用例的超时上限（秒）。超时只让该用例失败并继续，不拖死整轮。</summary>
    public int TestTimeoutSeconds { get; set; } = 180;

    public void Run(string name, Action body)
    {
        CurrentName = name;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 在独立任务里执行，配合超时看门狗：任何交互式阻塞都不会挂死整个测试进程。
        var task = System.Threading.Tasks.Task.Run(body);
        var finished = task.Wait(TimeSpan.FromSeconds(TestTimeoutSeconds));
        sw.Stop();

        if (!finished)
        {
            _results.Add(new TestResult(name, TestOutcome.Fail,
                $"超时：超过 {TestTimeoutSeconds} 秒未返回，可能存在交互式阻塞", sw.Elapsed));
            Write($"  [失败] {name}  —— 超时（>{TestTimeoutSeconds}s）");
            return;
        }

        try
        {
            task.GetAwaiter().GetResult();
            _results.Add(new TestResult(name, TestOutcome.Pass, "通过", sw.Elapsed));
            Write($"  [通过] {name}  ({sw.ElapsedMilliseconds} ms)");
        }
        catch (SkipTestException ex)
        {
            _results.Add(new TestResult(name, TestOutcome.Skip, ex.Message, sw.Elapsed));
            Write($"  [跳过] {name}  —— {ex.Message}");
        }
        catch (Exception ex)
        {
            var detail = ex.Message;
            _results.Add(new TestResult(name, TestOutcome.Fail, detail, sw.Elapsed));
            Write($"  [失败] {name}");
            Write($"         {ex.GetType().Name}: {detail}");
            if (ex.StackTrace is { } st)
                foreach (var line in st.Split('\n').Take(4))
                    Write("         " + line.Trim());
        }
    }

    // ---- 断言 ----

    public static void True(bool condition, string message = "断言失败：期望为真")
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void False(bool condition, string message = "断言失败：期望为假")
    {
        if (condition) throw new AssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException(message ?? $"断言失败：期望 [{expected}]，实际 [{actual}]");
    }

    public static void Contains(string haystack, string needle, string? message = null)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new AssertionException(message ?? $"断言失败：未找到 [{needle}]");
    }

    public static TException Throws<TException>(Action action, string? message = null)
        where TException : Exception
    {
        try { action(); }
        catch (TException ex) { return ex; }
        catch (Exception other)
        {
            throw new AssertionException(message ??
                $"断言失败：期望抛出 {typeof(TException).Name}，实际抛出 {other.GetType().Name}: {other.Message}");
        }
        throw new AssertionException(message ?? $"断言失败：期望抛出 {typeof(TException).Name}，但没有抛出任何异常");
    }

    public static void Skip(string reason) => throw new SkipTestException(reason);

    // ---- 输出 ----

    public void Write(string line)
    {
        _log.AppendLine(line);
        Console.WriteLine(line);
    }

    public int Report(string logPath)
    {
        var pass = _results.Count(r => r.Outcome == TestOutcome.Pass);
        var fail = _results.Count(r => r.Outcome == TestOutcome.Fail);
        var skip = _results.Count(r => r.Outcome == TestOutcome.Skip);

        Write(string.Empty);
        Write(new string('-', 58));
        Write($"合计 {_results.Count} 项：通过 {pass}，失败 {fail}，跳过 {skip}");

        if (fail > 0)
        {
            Write("失败用例：");
            foreach (var r in _results.Where(r => r.Outcome == TestOutcome.Fail))
                Write($"  - {r.Name}: {r.Detail}");
        }

        try { File.WriteAllText(logPath, _log.ToString(), Encoding.UTF8); } catch { }

        return fail; // 退出码 = 失败数
    }

    public IReadOnlyList<TestResult> Results => _results;
}

public sealed class AssertionException(string message) : Exception(message);

public sealed class SkipTestException(string message) : Exception(message);
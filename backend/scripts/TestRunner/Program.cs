using System.Reflection;
using System.Runtime.Loader;

// In-process раннер фактов и теорий xUnit.
//
// VSTest в этой песочнице недоступен (testhost падает на OpenProcess), поэтому тесты
// запускаются здесь: находим [Fact]/[Theory], подставляем [InlineData] и вызываем методы.
// Проверки — это обычные Assert-исключения xunit.assert, ничего специфичного для VSTest нет.

var root = FindRepositoryRoot(AppContext.BaseDirectory);
if (root is null)
{
    Console.Error.WriteLine("Solution root (backend/Pingboard.sln) was not found in any parent directory.");
    return 2;
}

var testProjects = new[]
{
    // Пути — от корня решения: FindRepositoryRoot находит каталог с backend/Pingboard.sln,
    // то есть каталог backend/, а тестовые проекты лежат в нём как tests/<Name>.
    "tests/Pingboard.Domain.Tests/Pingboard.Domain.Tests.csproj",
    "tests/Pingboard.Application.Tests/Pingboard.Application.Tests.csproj",
};

var exitCode = 0;

foreach (var project in testProjects)
{
    var name = Path.GetFileNameWithoutExtension(project);
    var assemblyPath = Path.Combine(root, "tests", name, "bin", "Debug", "net10.0", $"{name}.dll");

    if (!File.Exists(assemblyPath))
    {
        Console.Error.WriteLine($"Test assembly not found: {assemblyPath}. Build the solution with: dotnet build backend/Pingboard.sln -m:1");
        exitCode = 2;
        continue;
    }

    Console.WriteLine($"== {name}");

    var resolver = new TestAssemblyLoadContext(assemblyPath);
    var assembly = resolver.LoadFromAssemblyPath(assemblyPath);

    var passed = 0;
    var failed = 0;

    foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true }))
    {
        var methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttributes<Xunit.FactAttribute>(inherit: true).Any())
            .ToArray();

        if (methods.Length == 0)
        {
            continue;
        }

        foreach (var method in methods)
        {
            foreach (var arguments in BuildArgumentSets(method))
            {
                var title = arguments.Length == 0
                    ? $"{type.Name}.{method.Name}"
                    : $"{type.Name}.{method.Name}({string.Join(", ", arguments.Select(Format))})";

                try
                {
                    var instance = Activator.CreateInstance(type);
                    var result = method.Invoke(instance, arguments);

                    if (result is Task task)
                    {
                        task.GetAwaiter().GetResult();
                    }

                    passed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    var actual = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
                    Console.WriteLine($"  FAIL {title}");
                    Console.WriteLine($"       {actual.GetType().Name}: {actual.Message}");
                }
            }
        }
    }

    Console.WriteLine($"  passed={passed} failed={failed}");
    resolver.Unload();

    if (failed > 0)
    {
        exitCode = 1;
    }
}

Console.WriteLine(exitCode == 0 ? "ALL TESTS PASSED" : "SOME TESTS FAILED");
return exitCode;

static IEnumerable<object?[]> BuildArgumentSets(MethodInfo method)
{
    var parameters = method.GetParameters();
    if (parameters.Length == 0)
    {
        yield return [];
        yield break;
    }

    var inlineData = method.GetCustomAttributes<Xunit.InlineDataAttribute>(inherit: true).ToArray();
    if (inlineData.Length == 0)
    {
        // Без данных теорию не выполнить — играем как один факт без аргументов.
        yield return parameters.Select(_ => (object?)null).ToArray();
        yield break;
    }

    foreach (var data in inlineData)
    {
        var values = data.GetData(method).ToArray();

        // InlineDataAttribute.GetData возвращает object[]{ object[]{ ...аргументы } }.
        yield return values.Length == 1 && values[0] is object?[] inner ? inner : values;
    }
}

static string Format(object? value) => value switch
{
    null => "null",
    string text => $"\"{text}\"",
    _ => value.ToString() ?? "?",
};

static string? FindRepositoryRoot(string start)
{
    var directory = new DirectoryInfo(start);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Pingboard.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

/// <summary>
/// Грузит сборку тестов вместе с её зависимостями из папки bin.
/// Сборки xunit намеренно НЕ грузим сюда: тесты должны видеть те же типы Assert/Fact,
/// что и раннер, иначе атрибуты не находятся (разные identity у одного типа).
/// </summary>
internal sealed class TestAssemblyLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null
            || assemblyName.Name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase))
        {
            return null; // отдаём в default context — там уже есть xunit для раннера
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);

        return path is null || IsAlreadyLoadedInDefaultContext(assemblyName)
            ? null
            : LoadFromAssemblyPath(path);
    }

    private static bool IsAlreadyLoadedInDefaultContext(AssemblyName assemblyName) =>
        Default.Assemblies.Any(loaded =>
            AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), assemblyName));
}

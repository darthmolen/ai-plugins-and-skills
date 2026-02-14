---
name: csharp-quality-expert
description: Enforce C# coding standards including StyleCop rules (SA1028, SA1518, SA1101, etc.), file formatting (CRLF, trailing whitespace), naming conventions (this. prefix, no underscore prefix), XML documentation, LoggerMessage patterns, and brace placement. Use when writing C# code, reviewing .cs files, fixing StyleCop violations, or ensuring code quality compliance.
---

# Coding Standards Reminder for MSSQL MCP Server

## Pre-Coding Checklist

Before writing ANY code, remember these critical rules to avoid rework:

### 1. File Format Requirements
- **Line Endings**: Use Windows CRLF (`\r\n`) for all C# files
- **End of File**: ALWAYS end with exactly ONE newline character
- **No Trailing Spaces**: Never leave spaces at end of lines
- **Indentation**: Use 4 spaces (not tabs) for C# code
- **Line Endings - Unix**: Use Unix LF (`\n`) for all .sh files

### 2. Common StyleCop Rules to Follow

#### Most Frequently Violated Rules (Fix These First!)
- **SA1028**: Code should not contain trailing whitespace
- **SA1518**: File must end with single newline character
- **SA1101**: Prefix local calls with `this.`
- **SA1116**: Parameters should begin on line after declaration for multi-line
- **SA1500**: Braces for multi-line statements should not share line
- **SA1505**: Opening brace should not be followed by blank line
- **SA1108**: Block statements should not contain embedded comments
- **SA1513**: Closing brace should be followed by blank line

#### Documentation Rules
- **SA1614**: Element parameter documentation should have text (not just `<param name="x"></param>`)
- **SA1616**: Element return value documentation should have text (not just `<returns></returns>`)
- **SA1629**: Documentation text should end with a period
- **SA1623**: Property documentation should begin with "Gets or sets a value indicating whether" for bool properties

#### File Organization Rules
- **SA1402**: File may only contain a single type
- **SA1649**: File name should match first type name
- **SA1201**: Elements should be ordered correctly (enums should not follow classes)
- **SA1210**: Using directives should be ordered alphabetically by the namespaces

#### Other Important Rules
- **IDE0055**: Fix formatting (proper spacing and indentation)
- **IDE0040**: Interface members must have explicit accessibility modifiers (public)
- **SA1633**: File must have header (we suppress this, but include copyright)
- **SA1309**: Field names should not begin with underscore
- **SA1111**: Closing parenthesis should be on line of last parameter
- **SA1009**: Closing parenthesis should not be preceded by a space
- **CA1812**: Avoid uninstantiated internal classes (commonly suppressed for IoC/DI scenarios)

### 3. C# Code Style
```csharp
// CORRECT: File starts with copyright
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;                          // System usings first
using Microsoft.Extensions.Logging;   // Then Microsoft
using MssqlMcp.Application;           // Then project namespaces

namespace MssqlMcp.Application;       // File-scoped namespace

/// <summary>
/// Class documentation.
/// </summary>
public sealed class ExampleClass      // sealed when appropriate
{
    private readonly ILogger logger;  // readonly, camelCase, no underscore

    /// <summary>
    /// Constructor documentation.
    /// </summary>
    public ExampleClass(ILogger logger)
    {
        this.logger = logger;         // Use 'this.' qualification
    }

    /// <inheritdoc/>
    public async Task<Result> DoWork()
    {
        this.logger.LogDebug("Working");  // No trailing space
        return new Result();              // No trailing space
    }                                     // No trailing space
}                                         // No trailing space
// File ends with exactly one newline
```

### 4. Common Mistakes to Avoid

#### Trailing Spaces and File Endings
```csharp
// WRONG: Trailing spaces (SA1028)
public void Method()
{
    var x = 1;
}

// WRONG: No newline at end of file (SA1518)
public class MyClass
{
}
// WRONG: Multiple newlines at end


// CORRECT: No trailing spaces, single newline at end
public void Method()
{
    var x = 1;
}
```

#### Field Names and 'this.' Usage
```csharp
// WRONG: Using underscore prefix (SA1309)
private readonly ILogger _logger;

// WRONG: Not using 'this.' (SA1101)
logger.LogDebug("message");

// CORRECT: No underscore, use 'this.'
private readonly ILogger logger;
this.logger.LogDebug("message");
```

#### Multi-line Parameters (SA1116)
```csharp
// WRONG: Parameters on same line as method name
builder.Services.AddOptions<MyOptions>(options =>
    {
        options.Value = "test";
    });

// CORRECT: Parameters start on next line
builder.Services.AddOptions<MyOptions>(
    options =>
    {
        options.Value = "test";
    });
```

#### Documentation Issues
```csharp
// WRONG: Empty documentation tags (SA1614, SA1616)
/// <summary>
/// Processes data.
/// </summary>
/// <param name="input"></param>
/// <returns></returns>
public string Process(string input) { }

// WRONG: Missing period (SA1629)
/// <summary>
/// Processes data
/// </summary>

// WRONG: Bool property documentation (SA1623)
/// <summary>
/// Gets or sets whether logging is enabled.
/// </summary>
public bool IsLoggingEnabled { get; set; }

// CORRECT: Complete documentation
/// <summary>
/// Processes the input data.
/// </summary>
/// <param name="input">The input string to process.</param>
/// <returns>The processed result.</returns>
public string Process(string input) { }

/// <summary>
/// Gets or sets a value indicating whether logging is enabled.
/// </summary>
public bool IsLoggingEnabled { get; set; }
```

#### Embedded Comments (SA1108)
```csharp
// WRONG: Comment inside foreach
foreach (var item in items.TakeLast(3)) // Log last 3 items
{
    Console.WriteLine(item);
}

// CORRECT: Comment on separate line
// Log last 3 items
foreach (var item in items.TakeLast(3))
{
    Console.WriteLine(item);
}
```

#### CA1812: Avoid Uninstantiated Internal Classes
```csharp
// WRONG: Internal class never instantiated (CA1812)
internal class UnusedService
{
    public void DoWork() { }
}

// WRONG: Inline suppression pollutes code
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by IoC container")]
internal class InjectedService
{
    public void DoWork() { }
}

// CORRECT: Use GlobalSuppressions.cs for IoC/DI scenarios
// In GlobalSuppressions.cs:
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by dependency injection container",
    Scope = "type",
    Target = "~T:MyNamespace.InjectedService")]

// CORRECT: Use InternalsVisibleTo for test projects
// In .csproj file:
<ItemGroup>
  <InternalsVisibleTo Include="MyProject.Tests" />
  <InternalsVisibleTo Include="MyProject.IntegrationTests" />
</ItemGroup>

// Or in AssemblyInfo.cs:
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("MyProject.Tests")]
```

#### Using GlobalSuppressions.cs
```csharp
// GlobalSuppressions.cs file in project root
using System.Diagnostics.CodeAnalysis;

// Suppress for entire namespace (all DI services)
[assembly: SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Classes are instantiated by dependency injection",
    Scope = "namespaceanddescendants",
    Target = "~N:MyProject.Services")]

// Suppress for specific type
[assembly: SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by IoC container",
    Scope = "type",
    Target = "~T:MyProject.Services.EmailService")]

// Suppress for member (method, property, etc.)
[assembly: SuppressMessage(
    "Style",
    "IDE0060:Remove unused parameter",
    Justification = "Required by interface contract",
    Scope = "member",
    Target = "~M:MyProject.Services.MyService.Process(System.String)")]
```

#### Brace Placement (SA1500, SA1505, SA1513)
```csharp
// WRONG: Opening brace on same line (SA1500)
app.MapGet("/", () => {
    return "Hello";
});

// WRONG: Blank line after opening brace (SA1505)
public void Method()
{

    var x = 1;
}

// WRONG: No blank line after closing brace (SA1513)
if (condition)
{
    DoSomething();
}
else
{
    DoSomethingElse();
}

// CORRECT: Proper brace placement
app.MapGet("/", () =>
{
    return "Hello";
});

public void Method()
{
    var x = 1;
}

if (condition)
{
    DoSomething();
}
else
{
    DoSomethingElse();
}
```

### 5. LoggerMessage Pattern - Use Decorator Pattern

#### Preferred Pattern for High-Performance Logging
```csharp
// CORRECT: Use LoggerMessage attribute with partial method
[LoggerMessage(LogLevel.Information, "Entering {ClassName} for OrderNumber {orderNumber}, TaskNumber {TaskNumber}")]
private partial void LogEnter(string orderNumber, string taskNumber, string className = nameof(OrderService));

[LoggerMessage(LogLevel.Error, "Failed to process order {OrderId}")]
private partial void LogOrderError(int orderId, Exception exception);

// Usage
this.LogEnter(order.Number, task.Number);
```

#### Important: LoggerMessage Analyzer Behavior
```csharp
// IMPORTANT: LoggerMessage analyzers may show errors if other code issues exist
// Always fix other errors FIRST, then LoggerMessage warnings will resolve
// This is due to how .NET handles generated code and analyzers

// Example: If you have this pattern but see LoggerMessage warnings:
// 1. Fix all other compilation errors first
// 2. Fix all StyleCop violations
// 3. LoggerMessage warnings should disappear
// 4. If not, then address the LoggerMessage pattern
```

#### When to Use LoggerMessage
```csharp
// High-frequency logging (loops, hot paths)
foreach (var item in items)
{
    this.LogProcessingItem(item.Id); // Uses generated high-performance method
}

// Standard logging - regular ILogger is fine
this.logger.LogInformation("Application started"); // One-time or infrequent
```

### 6. Before Submitting Code
1. Check for trailing spaces (especially after `;` or `}`)
2. Verify file ends with single newline
3. Ensure proper indentation (4 spaces)
4. Verify all fields use `this.` qualification
5. Check that readonly fields are marked as such
6. Fix all compilation errors before addressing LoggerMessage warnings

### 7. Project-Specific Rules
- Use `Microsoft.Identity.Client` for OAuth
- Use `Microsoft.Data.SqlClient` (not System.Data.SqlClient)
- Always validate operations against `RestrictToReadOnly` flag
- Use structured logging with proper parameter names
- Handle async operations properly with ConfigureAwait(false) where appropriate

### 8. Testing Your Code
Before committing:
```bash
# Build will catch most issues
dotnet build

# Run tests
dotnet test

# Check for any formatting issues
# Visual Studio: Ctrl+K, Ctrl+D to format document
# VS Code: Shift+Alt+F to format document
```

### 9. Internal Visibility Best Practices

#### When to Use InternalsVisibleTo
```xml
<!-- In .csproj file for proper internal visibility -->
<ItemGroup>
  <!-- Allow test projects to access internals -->
  <InternalsVisibleTo Include="$(AssemblyName).Tests" />
  <InternalsVisibleTo Include="$(AssemblyName).IntegrationTests" />

  <!-- For strong-named assemblies, include public key -->
  <InternalsVisibleTo Include="MyProject.Tests, PublicKey=..." />
</ItemGroup>
```

#### Benefits of Proper Internal Visibility
- **Security**: Keep implementation details hidden from external consumers
- **Testing**: Allow unit tests to access internal classes without making them public
- **API Surface**: Reduce public API surface area for better maintainability
- **Encapsulation**: Better encapsulation of implementation details

```csharp
// CORRECT: Internal service with test access
namespace MyProject.Services;

internal sealed class EmailService : IEmailService
{
    // Implementation details hidden from external assemblies
    // But accessible to MyProject.Tests via InternalsVisibleTo
}

// CORRECT: Public interface, internal implementation
public interface IEmailService
{
    Task SendAsync(string to, string subject, string body);
}
```

### 10. Git Configuration
Ensure git handles line endings correctly:
```bash
# On Windows
git config core.autocrlf true

# Verify setting
git config --get core.autocrlf
```

## Quick Reference

| Rule | Description | Example |
|------|-------------|---------|
| SA1518 | End with newline | `}\n` not `}` |
| SA1028 | No trailing spaces | `var x = 1;` not `var x = 1;  ` |
| IDE0055 | Proper formatting | `if (x)` not `if(x)` |
| SA1101 | Use this. | `this.field` not `field` |
| SA1309 | No underscore | `logger` not `_logger` |
| CA1812 | Uninstantiated internal classes | Use GlobalSuppressions.cs for DI |
| - | InternalsVisibleTo | Allow tests to access internals |
| - | GlobalSuppressions.cs | Keep suppressions out of code |

Remember: **It's better to write code correctly the first time than to fix it later!**

## Quality Error Fix Strategy

When encountering StyleCop or code quality errors during refactoring:

### Iteration Strategy:
1. **Iteration 1**: Manually fix issues based on specific error messages
2. **Iteration 2**: Try another round of manual fixes if needed
3. **Iteration 3**: Final attempt at manual fixes
4. **After 3 iterations**: STOP and alert the user - Visual Studio UI is faster for complex fixes

### Important Note:
Do NOT use `dotnet format` as it can:
- Remove necessary using statements (especially ai-plugins-and-skills-specific ones)
- Get confused by source-generated code (LoggerMessage patterns)
- Make incorrect "fixes" that break the build

### Example Alert After 3 Iterations:
```
Multiple quality errors remaining. Please fix manually in Visual Studio:
 - SA1101: Add 'this.' qualifiers
 - SA1518: Missing newline at end of file
 - CS0246: Missing using directives
 - etc.
```

This prevents endless fix attempts and leverages Visual Studio's built-in quick fixes.

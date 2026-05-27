using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SDAnalyzers;

/// <summary>
/// Flags non-premultiplied <c>new Color(R, G, B, A &lt; 255)</c> and
/// <c>new Color(Color, A &lt; 255)</c> literals regardless of how they're consumed.
/// The default MonoGame SpriteBatch BlendState is AlphaBlend (premultiplied), so a raw
/// non-premul vertex Color with low alpha renders as additive RGB instead of as a faded
/// tint. The flag is broad on purpose: a low-alpha Color literal is almost never what you
/// want under premul rendering, and erring toward false positives is cheaper than missing
/// new sites. Append <c>.Premultiplied()</c> to the literal, or use <c>Color.Alpha(float)</c>
/// (which premultiplies as of commit c966860fe). The trivial no-op cases — pure-black RGB,
/// RGB == alpha (already in premul form), and <c>Color.Black</c> base in the 2-arg form —
/// are skipped.
/// </summary>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public class PremulColorAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "SD0001";

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Non-premultiplied Color literal with alpha < 255",
        messageFormat: "'new Color(...)' with alpha < 255 renders additive-bright under premul AlphaBlend; chain '.Premultiplied()' or use 'Color.Alpha(float)'",
        category: "Rendering",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The default SpriteBatch BlendState is premultiplied AlphaBlend. " +
                     "A raw 'new Color(R, G, B, A < 255)' literal feeds non-premul RGB through " +
                     "the premul shader, rendering as additive RGB instead of a faded tint. " +
                     "Append '.Premultiplied()' to the literal, or build the color via 'Color.Alpha(float)'.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectCreationExpression);
    }

    static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        // Resolve type — must be Microsoft.Xna.Framework.Color regardless of alias
        var typeSymbol = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (typeSymbol is null || typeSymbol.ToDisplayString() != "Microsoft.Xna.Framework.Color")
            return;

        var argList = creation.ArgumentList;
        if (argList is null) return;
        var args = argList.Arguments;

        // 4-arg byte ctor: new Color(R, G, B, A)
        if (args.Count == 4)
        {
            if (!TryGetLiteralIntValue(args[3].Expression, out int alpha)) return;
            if (alpha >= 255) return;

            bool rIsLit = TryGetLiteralIntValue(args[0].Expression, out int r);
            bool gIsLit = TryGetLiteralIntValue(args[1].Expression, out int g);
            bool bIsLit = TryGetLiteralIntValue(args[2].Expression, out int b);

            // Skip pure-black RGB — premul is a no-op on (0,0,0)
            if (rIsLit && r == 0 && gIsLit && g == 0 && bIsLit && b == 0)
                return;

            // Skip RGB == alpha — premul of (R,G,B,A) equals (R,G,B,A) iff R==G==B==A,
            // so the literal is already in premul form (intentional "pre-baked" tint).
            if (rIsLit && gIsLit && bIsLit && r == alpha && g == alpha && b == alpha)
                return;

            if (IsAlreadyHandled(creation)) return;
            context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation()));
            return;
        }

        // 2-arg ctor: new Color(Color, byte_or_float)
        if (args.Count == 2)
        {
            // Skip if the first arg is Color.Black — pure black, premul is a no-op
            if (IsColorBlackReference(args[0].Expression, context.SemanticModel, context.CancellationToken))
                return;

            if (TryGetLiteralIntValue(args[1].Expression, out int alphaByte))
            {
                if (alphaByte >= 255) return;
                if (IsAlreadyHandled(creation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation()));
                return;
            }

            if (TryGetLiteralFloatValue(args[1].Expression, out double alphaFloat))
            {
                if (alphaFloat >= 1.0) return;
                if (IsAlreadyHandled(creation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation()));
                return;
            }
        }
    }

    /// <summary>
    /// Returns true if the expression resolves to the field
    /// <c>Microsoft.Xna.Framework.Color.Black</c> specifically. Syntactic name matching
    /// alone is not enough — any unrelated <c>X.Black</c> would otherwise suppress SD0001.
    /// </summary>
    static bool IsColorBlackReference(ExpressionSyntax expr, SemanticModel semanticModel, System.Threading.CancellationToken ct)
    {
        if (expr is not MemberAccessExpressionSyntax member)
            return false;
        if (member.Name.Identifier.ValueText != "Black")
            return false;

        var symbol = semanticModel.GetSymbolInfo(member, ct).Symbol;
        return symbol is (IFieldSymbol or IPropertySymbol)
            && symbol.Name == "Black"
            && symbol.ContainingType?.ToDisplayString() == "Microsoft.Xna.Framework.Color";
    }

    /// <summary>
    /// Returns true if <c>new Color(...)</c> is chained with .Premultiplied() or .Alpha(...),
    /// walking through enclosing parentheses and ternary arms so patterns like
    /// <c>(cond ? new Color(...) : new Color(...)).Premultiplied()</c> are recognized.
    /// </summary>
    static bool IsAlreadyHandled(ExpressionSyntax expr)
    {
        SyntaxNode? parent = expr.Parent;
        while (parent is ParenthesizedExpressionSyntax or ConditionalExpressionSyntax)
            parent = parent.Parent;

        if (parent is MemberAccessExpressionSyntax memberAccess)
        {
            var name = memberAccess.Name.Identifier.ValueText;
            return name == "Premultiplied" || name == "Alpha";
        }
        return false;
    }

    static bool TryGetLiteralIntValue(ExpressionSyntax expr, out int value)
    {
        value = 0;
        // Strip casts like (byte)100
        while (expr is CastExpressionSyntax cast)
            expr = cast.Expression;

        if (expr is LiteralExpressionSyntax lit && lit.Token.Value is int i)
        {
            value = i;
            return true;
        }
        return false;
    }

    static bool TryGetLiteralFloatValue(ExpressionSyntax expr, out double value)
    {
        value = 0;
        while (expr is CastExpressionSyntax cast)
            expr = cast.Expression;

        if (expr is LiteralExpressionSyntax lit)
        {
            if (lit.Token.Value is float f) { value = f; return true; }
            if (lit.Token.Value is double d) { value = d; return true; }
        }
        return false;
    }
}
